namespace HNovaReader.Services.Reader;

public interface IReaderEngine
{
    bool CanOpen(string filePath);

    Task<ITextDocumentSession> OpenAsync(
        ReaderOpenRequest request,
        IProgress<TextIndexProgress>? indexProgress,
        CancellationToken cancellationToken);
}

public interface IReaderEngineFactory
{
    Task<ITextDocumentSession> OpenAsync(
        ReaderOpenRequest request,
        IProgress<TextIndexProgress>? indexProgress,
        CancellationToken cancellationToken);
}
