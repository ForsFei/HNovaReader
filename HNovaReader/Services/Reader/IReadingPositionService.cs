namespace HNovaReader.Services.Reader;

public sealed record ReadingPositionSnapshot(
    Guid BookId,
    long CharacterOffset,
    long? ByteOffsetHint,
    double Progress,
    string? ChapterTitleSnapshot,
    string? AnchorPrefix,
    string? AnchorSuffix,
    ReaderFileVersion FileVersion,
    string EncodingName,
    DateTime UpdatedAtUtc);

public sealed record SaveReadingPositionRequest(
    Guid BookId,
    long CharacterOffset,
    long? ByteOffsetHint,
    double Progress,
    string? ChapterTitleSnapshot,
    string? AnchorPrefix,
    string? AnchorSuffix,
    ReaderFileVersion FileVersion,
    string EncodingName);

public interface IReadingPositionService
{
    Task<ReadingPositionSnapshot?> GetAsync(Guid bookId, CancellationToken cancellationToken);

    Task SaveAsync(SaveReadingPositionRequest request, CancellationToken cancellationToken);
}
