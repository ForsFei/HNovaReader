namespace HNovaReader.Services.Reader;

public sealed class ReaderEngineFactory(IEnumerable<IReaderEngine> engines) : IReaderEngineFactory
{
    private readonly IReadOnlyList<IReaderEngine> engines = engines.ToArray();

    public Task<ITextDocumentSession> OpenAsync(
        ReaderOpenRequest request,
        IProgress<TextIndexProgress>? indexProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var engine = engines.FirstOrDefault(candidate => candidate.CanOpen(request.FilePath))
            ?? throw new NotSupportedException($"没有可读取该文件的 Reader Engine：{request.FilePath}");
        return engine.OpenAsync(request, indexProgress, cancellationToken);
    }
}
