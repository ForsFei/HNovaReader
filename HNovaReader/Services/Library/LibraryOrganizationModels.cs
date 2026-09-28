namespace HNovaReader.Services.Library;

public sealed record LibrarySeriesDetails(Guid Id, Guid FolderId, string Name, int BookCount);

public sealed record LibraryFolderDetails(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsSystem,
    string UnsortedName,
    int UnsortedBookCount,
    int BookCount,
    IReadOnlyList<LibrarySeriesDetails> Series,
    Guid? ParentId = null,
    string RelativePath = "");

public sealed record LibraryMoveRequest(
    IReadOnlyCollection<Guid> BookIds,
    Guid FolderId,
    Guid? SeriesId,
    string OperationName = "移动书籍");

public sealed record LibraryMoveFailure(Guid BookId, string Title, string Message);

public sealed record LibraryMoveResult(
    int MovedCount,
    IReadOnlyList<LibraryMoveFailure> Failures,
    string? BackupDirectory);

public sealed record LibraryImportTarget(
    Guid FolderId,
    Guid? SeriesId,
    string DestinationDirectory);

public sealed record LibraryBackupItem(Guid? BookId, string SourcePath, string? TargetPath);

public sealed record LibraryBackupResult(string DirectoryPath, string ManifestPath);
