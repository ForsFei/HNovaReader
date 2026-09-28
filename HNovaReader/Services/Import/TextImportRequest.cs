using HNovaReader.Data.Entities;

namespace HNovaReader.Services.Import;

public sealed record TextImportRequest
{
    public required IReadOnlyCollection<string> Paths { get; init; }

    public bool IncludeSubdirectories { get; init; } = true;

    public bool PreserveDirectoryStructure { get; init; } = true;

    public BookStorageMode StorageMode { get; init; } = BookStorageMode.ManagedCopy;

    public string? ManagedLibraryDirectory { get; init; }

    public Guid? TargetFolderId { get; init; }

    public Guid? TargetSeriesId { get; init; }

    public string? EncodingOverride { get; init; }

    public string? FallbackEncoding { get; init; }
}
