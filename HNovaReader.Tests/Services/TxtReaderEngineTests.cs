using System.Text;
using HNovaReader.Services.Import;
using HNovaReader.Services.Reader;

namespace HNovaReader.Tests.Services;

public sealed class TxtReaderEngineTests
{
    [Fact]
    public async Task OpenAsync_ReadsBoundedWindowAndBuildsChapterAndSparseIndexes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "novel.txt");
            var body = string.Concat(Enumerable.Repeat("星😀河流动。\r\n", 12_000));
            var text = $"序言\r\n第一卷 银河\r\n第一章 启程\r\n{body}第十章 归来\r\n终章正文";
            await File.WriteAllTextAsync(
                path,
                text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);
            var store = new InMemoryTextIndexStore();
            var engine = CreateEngine(store);

            await using var session = await engine.OpenAsync(
                new ReaderOpenRequest(Guid.NewGuid(), path, InitialWindowLength: 64),
                indexProgress: null,
                cancellationToken);

            Assert.Equal(text[..64], session.InitialWindow.Text);
            Assert.True(session.InitialWindow.IsStartOfDocument);
            Assert.False(session.InitialWindow.IsEndOfDocument);

            var index = await session.IndexingTask;

            Assert.True(index.IsComplete);
            Assert.Equal(text.Length, index.TotalCharacterCount);
            Assert.True(index.Checkpoints.Count > 2);
            Assert.Equal(new TextIndexCheckpoint(0, 3), index.Checkpoints[0]);
            Assert.Equal(
                ["第一卷 银河", "第一章 启程", "第十章 归来"],
                index.Chapters.Select(chapter => chapter.Title));
            Assert.Equal(text.IndexOf("第一卷", StringComparison.Ordinal), index.Chapters[0].StartOffset);
            Assert.Equal(index.Chapters[1].StartOffset, index.Chapters[0].EndOffset);
            Assert.Equal(text.Length, index.Chapters[^1].EndOffset);

            var readOffset = text.IndexOf("第十章", StringComparison.Ordinal) - 73;
            var window = await session.ReadAsync(readOffset, 160, cancellationToken);
            Assert.Equal(readOffset, window.StartCharacterOffset);
            Assert.Equal(text[readOffset..], window.Text);
            Assert.True(window.IsEndOfDocument);
            Assert.Equal(1, store.SaveCount);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task ReadAsync_UsesGb18030CheckpointsWithoutSplittingCharacters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            var path = Path.Combine(root, "gb18030.txt");
            var text = string.Concat(Enumerable.Repeat("繁星穿过银河，故事仍在继续。\r\n", 8_000));
            await File.WriteAllBytesAsync(
                path,
                Encoding.GetEncoding("GB18030").GetBytes(text),
                cancellationToken);
            var engine = CreateEngine(new InMemoryTextIndexStore());
            await using var session = await engine.OpenAsync(
                new ReaderOpenRequest(Guid.NewGuid(), path, "GB18030", InitialWindowLength: 128),
                indexProgress: null,
                cancellationToken);
            var index = await session.IndexingTask;
            var offset = 90_001L;

            var window = await session.ReadAsync(offset, 333, cancellationToken);

            Assert.Equal(text.Length, index.TotalCharacterCount);
            Assert.True(index.Checkpoints.Count > 2);
            Assert.Equal(text.Substring((int)offset, 333), window.Text);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task OpenAsync_ReusesMatchingIndexAndInvalidatesItAfterFileChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "cache.txt");
            await File.WriteAllTextAsync(
                path,
                "第一章\r\n正文",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            var bookId = Guid.NewGuid();
            var store = new InMemoryTextIndexStore();
            var engine = CreateEngine(store);
            await using (var first = await engine.OpenAsync(
                new ReaderOpenRequest(bookId, path, "utf-8"),
                indexProgress: null,
                cancellationToken))
            {
                await first.IndexingTask;
            }

            await using var cached = await engine.OpenAsync(
                new ReaderOpenRequest(bookId, path, "utf-8"),
                indexProgress: null,
                cancellationToken);
            Assert.True(cached.IndexingTask.IsCompletedSuccessfully);
            Assert.Equal(1, store.SaveCount);

            await File.AppendAllTextAsync(path, "\r\n新增正文", Encoding.UTF8, cancellationToken);
            File.SetLastWriteTimeUtc(path, cached.OpenedFileVersion.LastWriteTimeUtc.AddSeconds(2));

            Assert.True(await cached.HasFileChangedAsync(cancellationToken));
            await Assert.ThrowsAsync<ReaderFileChangedException>(() =>
                cached.ReadAsync(0, 10, cancellationToken));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task LargeSparseTxt_KeepsInitialAndRequestedWindowsBounded()
    {
        const int fileSize = 24 * 1024 * 1024;
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "large.txt");
            await using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                stream.SetLength(fileSize);
            }

            var engine = CreateEngine(new InMemoryTextIndexStore());
            await using var session = await engine.OpenAsync(
                new ReaderOpenRequest(
                    Guid.NewGuid(),
                    path,
                    "utf-8",
                    InitialWindowLength: 4096),
                indexProgress: null,
                cancellationToken);

            Assert.Equal(4096, session.InitialWindow.Text.Length);
            var index = await session.IndexingTask;
            Assert.Equal(fileSize, index.TotalCharacterCount);
            Assert.True(index.Checkpoints.Count > 300);

            var tail = await session.ReadAsync(fileSize - 1000, 2000, cancellationToken);
            Assert.Equal(1000, tail.Text.Length);
            Assert.True(tail.IsEndOfDocument);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                session.ReadAsync(0, (256 * 1024) + 1, cancellationToken));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task CancelIndexing_StopsBackgroundScanWithoutClosingReadableSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);
        var detector = new GatedChapterDetector();
        ITextDocumentSession? session = null;

        try
        {
            var path = Path.Combine(root, "cancel.txt");
            await File.WriteAllTextAsync(
                path,
                "第一章\r\n正文仍然可以读取。",
                Encoding.UTF8,
                cancellationToken);
            session = await new TxtReaderEngine(
                new TextEncodingDetector(),
                detector,
                new InMemoryTextIndexStore()).OpenAsync(
                    new ReaderOpenRequest(Guid.NewGuid(), path, "utf-8"),
                    indexProgress: null,
                    cancellationToken);
            await detector.Entered.WaitAsync(cancellationToken);

            session.CancelIndexing();
            detector.Release();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await session.IndexingTask);
            var window = await session.ReadAsync(0, 8, cancellationToken);
            Assert.Equal("第一章\r\n正文仍", window.Text);
        }
        finally
        {
            detector.Release();
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            detector.Dispose();
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task FileChangedDuringIndexing_FaultsIndexAndDoesNotSaveStaleCache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "changing.txt");
            var text = string.Concat(Enumerable.Repeat("正文内容保持流动。\r\n", 30_000));
            await File.WriteAllTextAsync(path, text, Encoding.UTF8, cancellationToken);
            var store = new InMemoryTextIndexStore();
            var changed = 0;
            var progress = new InlineProgress<TextIndexProgress>(_ =>
            {
                if (Interlocked.Exchange(ref changed, 1) == 0)
                {
                    File.AppendAllText(path, "外部修改", Encoding.UTF8);
                }
            });
            var engine = CreateEngine(store);

            await using var session = await engine.OpenAsync(
                new ReaderOpenRequest(Guid.NewGuid(), path, "utf-8", InitialWindowLength: 64),
                progress,
                cancellationToken);

            await Assert.ThrowsAsync<ReaderFileChangedException>(async () =>
                await session.IndexingTask);
            Assert.Equal(0, store.SaveCount);
            Assert.True(await session.HasFileChangedAsync(cancellationToken));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static TxtReaderEngine CreateEngine(ITextIndexStore indexStore)
    {
        return new TxtReaderEngine(
            new TextEncodingDetector(),
            new ChapterDetector(),
            indexStore);
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "HNovaReader.Tests",
            Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTempRoot(string root)
    {
        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HNovaReader.Tests"));
        var resolvedRoot = Path.GetFullPath(root);
        if (resolvedRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    private sealed class InMemoryTextIndexStore : ITextIndexStore
    {
        private TextIndexSnapshot? snapshot;

        public int SaveCount { get; private set; }

        public Task<TextIndexSnapshot?> TryLoadAsync(
            Guid bookId,
            ReaderFileVersion fileVersion,
            string encodingName,
            int patternVersion,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = snapshot is not null
                && snapshot.FileVersion == fileVersion
                && string.Equals(snapshot.EncodingName, encodingName, StringComparison.OrdinalIgnoreCase)
                && snapshot.PatternVersion == patternVersion;
            return Task.FromResult(match ? snapshot : null);
        }

        public Task SaveAsync(
            Guid bookId,
            TextIndexSnapshot value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = value;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }

    private sealed class GatedChapterDetector : IChapterDetector, IDisposable
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim release = new(initialState: false);
        private int hasEntered;

        public Task Entered => entered.Task;

        public int PatternVersion => 1;

        public int MaximumLineLength => 160;

        public ChapterMatch? Detect(string line, long lineStartOffset)
        {
            if (Interlocked.Exchange(ref hasEntered, 1) == 0)
            {
                entered.TrySetResult();
                release.Wait();
            }

            return null;
        }

        public void Release()
        {
            release.Set();
        }

        public void Dispose()
        {
            release.Dispose();
        }
    }
}
