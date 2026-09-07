using System.IO;
using System.Text;

namespace HNovaReader.Services.Reader;

internal sealed class TxtDocumentSession : ITextDocumentSession
{
    public const int MaximumReadWindowCharacters = 256 * 1024;
    private const int CachedCharacterCapacity = 1024 * 1024;
    private readonly Encoding encoding;
    private readonly long contentByteOffset;
    private readonly TxtTextIndexer indexer;
    private readonly ITextIndexStore indexStore;
    private readonly IProgress<TextIndexProgress>? indexProgress;
    private readonly CancellationTokenSource indexingCancellation = new();
    private readonly BoundedTextWindowCache windowCache = new(CachedCharacterCapacity);
    private TextIndexSnapshot? currentIndex;
    private Task<TextIndexSnapshot>? indexingTask;
    private bool isDisposed;

    public TxtDocumentSession(
        Guid bookId,
        string filePath,
        Encoding encoding,
        long contentByteOffset,
        ReaderFileVersion openedFileVersion,
        TxtTextIndexer indexer,
        ITextIndexStore indexStore,
        IProgress<TextIndexProgress>? indexProgress)
    {
        BookId = bookId;
        FilePath = filePath;
        this.encoding = encoding;
        this.contentByteOffset = contentByteOffset;
        OpenedFileVersion = openedFileVersion;
        this.indexer = indexer;
        this.indexStore = indexStore;
        this.indexProgress = indexProgress;
        InitialWindow = new TextReadWindow(0, string.Empty, true, true);
    }

    public Guid BookId { get; }

    public string FilePath { get; }

    public string EncodingName => encoding.WebName;

    public ReaderFileVersion OpenedFileVersion { get; }

    public TextReadWindow InitialWindow { get; private set; }

    public TextIndexSnapshot? CurrentIndex => Volatile.Read(ref currentIndex);

    public Task<TextIndexSnapshot> IndexingTask => indexingTask
        ?? throw new InvalidOperationException("文本会话尚未初始化。");

    public async Task InitializeAsync(
        long initialCharacterOffset,
        int initialWindowLength,
        TextIndexSnapshot? cachedIndex,
        CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref currentIndex, cachedIndex);
        InitialWindow = await ReadAsync(
            initialCharacterOffset,
            initialWindowLength,
            cancellationToken);

        if (cachedIndex is not null)
        {
            indexingTask = Task.FromResult(cachedIndex);
            return;
        }

        indexingTask = Task.Run(BuildAndSaveIndexAsync);
    }

    public async Task<TextReadWindow> ReadAsync(
        long characterOffset,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(characterOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        if (maximumCharacters > MaximumReadWindowCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                $"单个文本窗口不能超过 {MaximumReadWindowCharacters} 个字符。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (await HasFileChangedAsync(cancellationToken))
        {
            windowCache.Clear();
            throw new ReaderFileChangedException(FilePath);
        }

        var cacheKey = new TextWindowCacheKey(characterOffset, maximumCharacters);
        if (windowCache.TryGet(cacheKey, out var cachedWindow))
        {
            return cachedWindow;
        }

        var checkpoint = FindCheckpoint(characterOffset);
        var charactersToSkip = characterOffset - checkpoint.CharacterOffset;
        var actualStartOffset = checkpoint.CharacterOffset;
        var text = new StringBuilder(maximumCharacters + 1);
        var reachedCapacity = false;

        await foreach (var chunk in TextEncodingUtilities.ReadChunksAsync(
            FilePath,
            encoding,
            checkpoint.ByteOffset,
            cancellationToken))
        {
            var chunkOffset = 0;
            if (charactersToSkip > 0)
            {
                var skipped = (int)Math.Min(charactersToSkip, chunk.Text.Length);
                charactersToSkip -= skipped;
                chunkOffset += skipped;
                actualStartOffset += skipped;
                if (chunkOffset == chunk.Text.Length)
                {
                    continue;
                }
            }

            var available = Math.Min(
                chunk.Text.Length - chunkOffset,
                (maximumCharacters + 1) - text.Length);
            text.Append(chunk.Text, chunkOffset, available);
            if (text.Length > maximumCharacters)
            {
                reachedCapacity = true;
                break;
            }
        }

        if (text.Length > maximumCharacters)
        {
            text.Length = maximumCharacters;
        }

        var window = new TextReadWindow(
            actualStartOffset,
            text.ToString(),
            actualStartOffset == 0,
            !reachedCapacity);
        windowCache.Add(cacheKey, window);
        return window;
    }

    public Task<bool> HasFileChangedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(ReaderFileVersion.FromPath(FilePath) != OpenedFileVersion);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult(true);
        }
    }

    public void CancelIndexing()
    {
        if (!isDisposed)
        {
            indexingCancellation.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        indexingCancellation.Cancel();
        if (indexingTask is not null)
        {
            try
            {
                await indexingTask;
            }
            catch (OperationCanceledException) when (indexingCancellation.IsCancellationRequested)
            {
            }
            catch
            {
                // IndexingTask remains the explicit error-reporting surface.
            }
        }

        windowCache.Clear();
        indexingCancellation.Dispose();
    }

    private async Task<TextIndexSnapshot> BuildAndSaveIndexAsync()
    {
        var snapshot = await indexer.BuildAsync(
            FilePath,
            encoding,
            contentByteOffset,
            OpenedFileVersion,
            indexProgress,
            partial => Interlocked.Exchange(ref currentIndex, partial),
            indexingCancellation.Token);
        await indexStore.SaveAsync(BookId, snapshot, indexingCancellation.Token);
        Interlocked.Exchange(ref currentIndex, snapshot);
        return snapshot;
    }

    private TextIndexCheckpoint FindCheckpoint(long characterOffset)
    {
        var checkpoints = CurrentIndex?.Checkpoints;
        if (checkpoints is null || checkpoints.Count == 0)
        {
            return new TextIndexCheckpoint(0, contentByteOffset);
        }

        var low = 0;
        var high = checkpoints.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (checkpoints[middle].CharacterOffset <= characterOffset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return checkpoints[Math.Max(0, high)];
    }
}
