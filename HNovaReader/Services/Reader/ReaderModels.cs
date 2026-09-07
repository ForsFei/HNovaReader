using System.IO;

namespace HNovaReader.Services.Reader;

public sealed record ReaderFileVersion(long FileSize, DateTime LastWriteTimeUtc)
{
    public static ReaderFileVersion FromPath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var file = new FileInfo(filePath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("找不到要读取的 TXT 文件。", filePath);
        }

        return new ReaderFileVersion(
            file.Length,
            DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc));
    }
}

public sealed record ReaderOpenRequest(
    Guid BookId,
    string FilePath,
    string? EncodingName = null,
    long InitialCharacterOffset = 0,
    int InitialWindowLength = 64 * 1024);

public sealed record TextReadWindow(
    long StartCharacterOffset,
    string Text,
    bool IsStartOfDocument,
    bool IsEndOfDocument)
{
    public long EndCharacterOffset => StartCharacterOffset + Text.Length;
}

public sealed record TextIndexCheckpoint(long CharacterOffset, long ByteOffset);

public sealed record ChapterInfo(
    string Title,
    int Level,
    long StartOffset,
    long EndOffset);

public sealed record ChapterMatch(string Title, int Level, long StartOffset);

public sealed record TextIndexSnapshot(
    ReaderFileVersion FileVersion,
    string EncodingName,
    int PatternVersion,
    long TotalCharacterCount,
    bool IsComplete,
    IReadOnlyList<TextIndexCheckpoint> Checkpoints,
    IReadOnlyList<ChapterInfo> Chapters);

public sealed record TextIndexProgress(
    long BytesProcessed,
    long TotalBytes,
    long CharactersProcessed,
    IReadOnlyList<ChapterInfo> ChaptersAdded)
{
    public double Fraction => TotalBytes == 0
        ? 1
        : Math.Clamp((double)BytesProcessed / TotalBytes, 0, 1);
}

public sealed class ReaderFileChangedException(string filePath)
    : IOException($"TXT 文件在阅读期间发生了变化：{filePath}")
{
    public string FilePath { get; } = filePath;
}
