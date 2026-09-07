namespace HNovaReader.Services.Reader;

public sealed record TextFileRangeEditRequest(
    string FilePath,
    string EncodingName,
    ReaderFileVersion ExpectedVersion,
    long StartCharacterOffset,
    int OriginalLength,
    string ReplacementText);

public sealed record TextFileRangeEditResult(ReaderFileVersion FileVersion);

public interface ITextFileEditService
{
    Task<TextFileRangeEditResult> ReplaceRangeAsync(
        TextFileRangeEditRequest request,
        CancellationToken cancellationToken);
}
