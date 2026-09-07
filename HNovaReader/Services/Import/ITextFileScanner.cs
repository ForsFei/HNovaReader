namespace HNovaReader.Services.Import;

public interface ITextFileScanner
{
    Task<TextFileScanResult> ScanAsync(
        IReadOnlyCollection<string> paths,
        bool includeSubdirectories,
        IProgress<TextImportProgress>? progress,
        CancellationToken cancellationToken);
}
