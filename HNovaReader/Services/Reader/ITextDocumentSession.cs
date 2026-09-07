namespace HNovaReader.Services.Reader;

public interface ITextDocumentSession : IAsyncDisposable
{
    Guid BookId { get; }

    string FilePath { get; }

    string EncodingName { get; }

    ReaderFileVersion OpenedFileVersion { get; }

    TextReadWindow InitialWindow { get; }

    TextIndexSnapshot? CurrentIndex { get; }

    Task<TextIndexSnapshot> IndexingTask { get; }

    Task<TextReadWindow> ReadAsync(
        long characterOffset,
        int maximumCharacters,
        CancellationToken cancellationToken);

    Task<bool> HasFileChangedAsync(CancellationToken cancellationToken);

    void CancelIndexing();
}
