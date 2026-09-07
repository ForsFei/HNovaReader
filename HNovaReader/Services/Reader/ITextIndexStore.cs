namespace HNovaReader.Services.Reader;

public interface ITextIndexStore
{
    Task<TextIndexSnapshot?> TryLoadAsync(
        Guid bookId,
        ReaderFileVersion fileVersion,
        string encodingName,
        int patternVersion,
        CancellationToken cancellationToken);

    Task SaveAsync(
        Guid bookId,
        TextIndexSnapshot snapshot,
        CancellationToken cancellationToken);
}
