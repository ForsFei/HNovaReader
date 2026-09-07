namespace HNovaReader.Services.Import;

public sealed record TextImportProgress(
    TextImportStage Stage,
    int DiscoveredCount,
    int? TotalCount,
    int ProcessedCount,
    int ImportedCount,
    int SkippedCount,
    int FailedCount,
    string? CurrentPath);
