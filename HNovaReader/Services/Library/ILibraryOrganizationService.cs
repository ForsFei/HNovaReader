namespace HNovaReader.Services.Library;

public interface ILibraryOrganizationService
{
    Task EnsureDefaultsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LibraryFolderDetails>> GetFoldersAsync(CancellationToken cancellationToken);

    Task<LibraryFolderDetails> CreateFolderAsync(
        string name,
        CancellationToken cancellationToken);

    Task<LibraryFolderDetails> CreateFolderAsync(Guid parentId, string name, CancellationToken cancellationToken);

    Task<LibraryFolderDetails> EnsureFolderPathAsync(string relativePath, CancellationToken cancellationToken);

    Task MoveFolderAsync(Guid folderId, Guid targetParentId, CancellationToken cancellationToken);

    Task PruneMissingFoldersAsync(CancellationToken cancellationToken);

    Task<LibrarySeriesDetails> CreateSeriesAsync(
        Guid folderId,
        string name,
        CancellationToken cancellationToken);

    Task RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken);

    Task RenameUnsortedAsync(Guid folderId, string name, CancellationToken cancellationToken);

    Task RenameSeriesAsync(Guid seriesId, string name, CancellationToken cancellationToken);

    Task DeleteSeriesAsync(Guid seriesId, CancellationToken cancellationToken);

    Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken);

    Task<LibrarySeriesDetails> MoveSeriesAsync(
        Guid seriesId,
        Guid targetFolderId,
        CancellationToken cancellationToken);

    Task<LibraryImportTarget> ResolveImportTargetAsync(
        Guid? folderId,
        Guid? seriesId,
        string? libraryRoot,
        CancellationToken cancellationToken);

    Task<LibraryMoveResult> MoveBooksAsync(
        LibraryMoveRequest request,
        CancellationToken cancellationToken);

    Task<LibraryBackupResult?> BackupBooksAsync(
        IReadOnlyCollection<Guid> bookIds,
        string operationName,
        CancellationToken cancellationToken);

    Task MigrateRootAsync(string targetRoot, CancellationToken cancellationToken);
}
