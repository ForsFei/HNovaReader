using System.IO;
using HNovaReader.Services.Import;

namespace HNovaReader.Services.Reader;

public sealed class TxtReaderEngine(
    ITextEncodingDetector encodingDetector,
    IChapterDetector chapterDetector,
    ITextIndexStore indexStore) : IReaderEngine
{
    public bool CanOpen(string filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath)
            && string.Equals(Path.GetExtension(filePath), ".txt", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ITextDocumentSession> OpenAsync(
        ReaderOpenRequest request,
        IProgress<TextIndexProgress>? indexProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BookId == Guid.Empty)
        {
            throw new ArgumentException("BookId 不能为空。", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);
        ArgumentOutOfRangeException.ThrowIfNegative(request.InitialCharacterOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.InitialWindowLength);
        if (request.InitialWindowLength > TxtDocumentSession.MaximumReadWindowCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var filePath = Path.GetFullPath(request.FilePath);
        if (!CanOpen(filePath))
        {
            throw new NotSupportedException("第一版 Reader Engine 只支持 TXT 文件。");
        }

        var openedFileVersion = ReaderFileVersion.FromPath(filePath);
        var encodingName = string.IsNullOrWhiteSpace(request.EncodingName)
            ? (await encodingDetector.DetectAsync(filePath, cancellationToken)).EncodingName
            : encodingDetector.GetUserOverride(request.EncodingName).EncodingName;
        var encoding = TextEncodingUtilities.CreateEncoding(encodingName);
        var contentByteOffset = await TextEncodingUtilities.GetPreambleLengthAsync(
            filePath,
            encoding,
            cancellationToken);
        if (ReaderFileVersion.FromPath(filePath) != openedFileVersion)
        {
            throw new ReaderFileChangedException(filePath);
        }

        var cachedIndex = await indexStore.TryLoadAsync(
            request.BookId,
            openedFileVersion,
            encoding.WebName,
            chapterDetector.PatternVersion,
            cancellationToken);
        var session = new TxtDocumentSession(
            request.BookId,
            filePath,
            encoding,
            contentByteOffset,
            openedFileVersion,
            new TxtTextIndexer(chapterDetector),
            indexStore,
            indexProgress);

        try
        {
            await session.InitializeAsync(
                request.InitialCharacterOffset,
                request.InitialWindowLength,
                cachedIndex,
                cancellationToken);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}
