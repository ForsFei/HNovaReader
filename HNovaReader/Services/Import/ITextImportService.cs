namespace HNovaReader.Services.Import;

public interface ITextImportService
{
    Task<TextImportResult> ImportAsync(
        TextImportRequest request,
        IProgress<TextImportProgress>? progress,
        CancellationToken cancellationToken);
}
