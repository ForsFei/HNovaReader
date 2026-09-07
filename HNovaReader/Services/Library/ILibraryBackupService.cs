namespace HNovaReader.Services.Library;

public interface ILibraryBackupService
{
    Task<LibraryBackupResult> BackupAsync(
        string libraryRoot,
        string operationName,
        IReadOnlyCollection<LibraryBackupItem> items,
        CancellationToken cancellationToken);
}
