using HNovaReader.Data.Entities;
using HNovaReader.Features.Library;
using HNovaReader.Services.Import;
using HNovaReader.Services.Library;

namespace HNovaReader.Tests.TestSupport;

public static class LibraryViewModelTestFactory
{
    public static LibraryViewModel Create(
        StubLibraryService? libraryService = null,
        StubTextEncodingDetector? encodingDetector = null,
        StubCoverFileStore? coverFileStore = null,
        StubLibraryInteractionService? interactionService = null,
        StubLibraryOrganizationService? organizationService = null)
    {
        libraryService ??= new StubLibraryService();
        return new LibraryViewModel(
            libraryService,
            encodingDetector ?? new StubTextEncodingDetector(),
            coverFileStore ?? new StubCoverFileStore(),
            interactionService ?? new StubLibraryInteractionService(),
            organizationService ?? new StubLibraryOrganizationService(libraryService));
    }
}

public sealed class StubLibraryService : ILibraryService
{
    public List<BookDetails> Books { get; } = [];

    public UpdateBookRequest? LastUpdateRequest { get; private set; }

    public Guid? LastUpdatedBookId { get; private set; }

    public Guid? LastRemovedBookId { get; private set; }

    public RelocateBookFileRequest? LastRelocateRequest { get; private set; }

    public Task<IReadOnlyList<BookDetails>> GetBooksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BookDetails>>(Books.ToArray());
    }

    public Task<IReadOnlyList<BookDetails>> GetSeriesBooksAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BookDetails>>(
            Books.Where(book => book.SeriesId == seriesId).ToArray());
    }

    public Task<BookDetails?> GetBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Books.SingleOrDefault(book => book.Id == bookId));
    }

    public Task<bool> ContainsFilePathAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Books.Any(book => string.Equals(
            book.SourceFilePath,
            filePath,
            StringComparison.OrdinalIgnoreCase)));
    }

    public Task<BookDetails> AddBookAsync(
        AddBookRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<BookDetails?> UpdateBookAsync(
        Guid bookId,
        UpdateBookRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastUpdatedBookId = bookId;
        LastUpdateRequest = request;
        var index = Books.FindIndex(book => book.Id == bookId);
        if (index < 0)
        {
            return Task.FromResult<BookDetails?>(null);
        }

        var existing = Books[index];
        var updated = existing with
        {
            Title = request.Title,
            Description = request.Description,
            CoverPath = request.CoverPath,
            AuthorName = request.AuthorName,
            SeriesName = request.SeriesName,
            SeriesIndex = request.SeriesIndex,
            Tags = request.Tags.ToArray(),
            IsFavorite = request.IsFavorite,
        };
        Books[index] = updated;
        return Task.FromResult<BookDetails?>(updated);
    }

    public Task<BookDetails?> RefreshBookFileInfoAsync(
        Guid bookId,
        RefreshBookFileInfoRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = Books.FindIndex(book => book.Id == bookId);
        if (index < 0)
        {
            return Task.FromResult<BookDetails?>(null);
        }

        var updated = Books[index] with
        {
            FileSize = request.FileSize,
            LastWriteTimeUtc = request.LastWriteTimeUtc,
            FileEncoding = request.FileEncoding,
        };
        Books[index] = updated;
        return Task.FromResult<BookDetails?>(updated);
    }

    public Task<BookDetails?> RelocateBookFileAsync(
        Guid bookId,
        RelocateBookFileRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRelocateRequest = request;
        var index = Books.FindIndex(book => book.Id == bookId);
        if (index < 0)
        {
            return Task.FromResult<BookDetails?>(null);
        }

        var updated = Books[index] with
        {
            FilePath = request.FilePath,
            SourceFilePath = request.FilePath,
            StorageMode = BookStorageMode.OriginalLocation,
            FileSize = request.FileSize,
            LastWriteTimeUtc = request.LastWriteTimeUtc,
            FileEncoding = request.FileEncoding,
        };
        Books[index] = updated;
        return Task.FromResult<BookDetails?>(updated);
    }

    public Task<bool> RemoveBookAsync(Guid bookId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRemovedBookId = bookId;
        return Task.FromResult(Books.RemoveAll(book => book.Id == bookId) > 0);
    }
}

public sealed class StubTextEncodingDetector : ITextEncodingDetector
{
    public TextEncodingDetectionResult Result { get; set; } = new(
        "utf-8",
        1,
        EncodingDetectionSource.StrictUtf8);

    public Task<TextEncodingDetectionResult> DetectAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result);
    }

    public TextEncodingDetectionResult GetUserOverride(string encodingName)
    {
        return Result with { EncodingName = encodingName };
    }
}

public sealed class StubCoverFileStore : ICoverFileStore
{
    public string? CopyDestination { get; set; }

    public List<string> DeletedPaths { get; } = [];

    public Task<string> CopyAsync(
        Guid bookId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CopyDestination ?? sourcePath);
    }

    public void DeleteManagedCopy(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            DeletedPaths.Add(filePath);
        }
    }
}

public sealed class StubLibraryInteractionService : ILibraryInteractionService
{
    public IReadOnlyList<string> TextFiles { get; set; } = Array.Empty<string>();

    public string? CoverImage { get; set; }

    public string? ReplacementTextFile { get; set; }

    public BookEditorResult? EditorResult { get; set; }

    public string? PromptNameResult { get; set; }

    public bool ConfirmResult { get; set; }

    public string? RevealedPath { get; private set; }

    public string? RecycledPath { get; private set; }

    public List<(string Title, string Message, bool IsError)> Messages { get; } = [];

    public IReadOnlyList<string> PickTextFiles()
    {
        return TextFiles;
    }

    public string? PickCoverImage()
    {
        return CoverImage;
    }

    public string? PickReplacementTextFile(BookDetails book)
    {
        return ReplacementTextFile;
    }

    public BookEditorResult? EditBook(BookDetails book)
    {
        return EditorResult;
    }

    public string? PromptForName(string title, string label, string? initialValue = null)
    {
        return PromptNameResult;
    }

    public bool Confirm(string title, string message, bool isDangerous = false)
    {
        return ConfirmResult;
    }

    public void RevealFile(string filePath)
    {
        RevealedPath = filePath;
    }

    public void MoveFileToRecycleBin(string filePath)
    {
        RecycledPath = filePath;
    }

    public void ShowMessage(string title, string message, bool isError = false)
    {
        Messages.Add((title, message, isError));
    }
}

public sealed class StubLibraryOrganizationService : ILibraryOrganizationService
{
    private readonly StubLibraryService _libraryService;

    public StubLibraryOrganizationService(StubLibraryService libraryService)
    {
        _libraryService = libraryService;
        Folders.Add(new LibraryFolderDetails(
            LibraryOrganizationDefaults.InboxFolderId,
            LibraryOrganizationDefaults.InboxFolderName,
            int.MinValue,
            true,
            LibraryOrganizationDefaults.UnsortedName,
            0,
            0,
            []));
    }

    public List<LibraryFolderDetails> Folders { get; } = [];

    public LibraryMoveRequest? LastMoveRequest { get; private set; }

    public (Guid SeriesId, Guid TargetFolderId)? LastSeriesMove { get; private set; }

    public Guid? LastDeletedSeriesId { get; private set; }

    public Guid? LastDeletedFolderId { get; private set; }

    public int BackupCallCount { get; private set; }

    public string? LastMigrationTarget { get; private set; }

    public Task EnsureDefaultsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<LibraryFolderDetails>> GetFoldersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LibraryFolderDetails>>(Folders.ToArray());

    public Task<LibraryFolderDetails> CreateFolderAsync(string name, CancellationToken cancellationToken)
    {
        var folder = new LibraryFolderDetails(
            Guid.NewGuid(),
            name,
            Folders.Count,
            false,
            LibraryOrganizationDefaults.UnsortedName,
            0,
            0,
            []);
        Folders.Add(folder);
        return Task.FromResult(folder);
    }

    public Task<LibrarySeriesDetails> CreateSeriesAsync(Guid folderId, string name, CancellationToken cancellationToken)
    {
        var series = new LibrarySeriesDetails(Guid.NewGuid(), folderId, name, 0);
        var index = Folders.FindIndex(folder => folder.Id == folderId);
        Folders[index] = Folders[index] with { Series = [.. Folders[index].Series, series] };
        return Task.FromResult(series);
    }

    public Task RenameFolderAsync(Guid folderId, string name, CancellationToken cancellationToken)
    {
        var index = Folders.FindIndex(folder => folder.Id == folderId);
        Folders[index] = Folders[index] with { Name = name };
        return Task.CompletedTask;
    }

    public Task RenameSeriesAsync(Guid seriesId, string name, CancellationToken cancellationToken)
    {
        var folderIndex = Folders.FindIndex(folder => folder.Series.Any(series => series.Id == seriesId));
        var folder = Folders[folderIndex];
        Folders[folderIndex] = folder with
        {
            Series = folder.Series.Select(series => series.Id == seriesId ? series with { Name = name } : series).ToArray(),
        };
        return Task.CompletedTask;
    }

    public Task RenameUnsortedAsync(Guid folderId, string name, CancellationToken cancellationToken)
    {
        var index = Folders.FindIndex(folder => folder.Id == folderId);
        Folders[index] = Folders[index] with { UnsortedName = name };
        return Task.CompletedTask;
    }

    public Task DeleteSeriesAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        LastDeletedSeriesId = seriesId;
        var folderIndex = Folders.FindIndex(folder => folder.Series.Any(series => series.Id == seriesId));
        var folder = Folders[folderIndex];
        Folders[folderIndex] = folder with
        {
            Series = folder.Series.Where(series => series.Id != seriesId).ToArray(),
        };
        for (var index = 0; index < _libraryService.Books.Count; index++)
        {
            var book = _libraryService.Books[index];
            if (book.SeriesId == seriesId)
            {
                _libraryService.Books[index] = book with { SeriesId = null, SeriesName = null };
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken)
    {
        LastDeletedFolderId = folderId;
        Folders.RemoveAll(folder => folder.Id == folderId);
        var inbox = Folders.Single(folder => folder.Id == LibraryOrganizationDefaults.InboxFolderId);
        for (var index = 0; index < _libraryService.Books.Count; index++)
        {
            var book = _libraryService.Books[index];
            if (book.FolderId == folderId)
            {
                _libraryService.Books[index] = book with
                {
                    FolderId = inbox.Id,
                    FolderName = inbox.Name,
                    SeriesId = null,
                    SeriesName = null,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<LibrarySeriesDetails> MoveSeriesAsync(
        Guid seriesId,
        Guid targetFolderId,
        CancellationToken cancellationToken)
    {
        LastSeriesMove = (seriesId, targetFolderId);
        var sourceFolderIndex = Folders.FindIndex(folder => folder.Series.Any(series => series.Id == seriesId));
        var sourceFolder = Folders[sourceFolderIndex];
        var series = sourceFolder.Series.Single(item => item.Id == seriesId);
        Folders[sourceFolderIndex] = sourceFolder with
        {
            Series = sourceFolder.Series.Where(item => item.Id != seriesId).ToArray(),
        };
        var targetFolderIndex = Folders.FindIndex(folder => folder.Id == targetFolderId);
        var moved = series with { FolderId = targetFolderId };
        Folders[targetFolderIndex] = Folders[targetFolderIndex] with
        {
            Series = [.. Folders[targetFolderIndex].Series, moved],
        };
        for (var index = 0; index < _libraryService.Books.Count; index++)
        {
            var book = _libraryService.Books[index];
            if (book.SeriesId == seriesId)
            {
                _libraryService.Books[index] = book with
                {
                    FolderId = targetFolderId,
                    FolderName = Folders[targetFolderIndex].Name,
                };
            }
        }

        return Task.FromResult(moved);
    }

    public Task<LibraryImportTarget> ResolveImportTargetAsync(
        Guid? folderId,
        Guid? seriesId,
        string? libraryRoot,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LibraryImportTarget(
            folderId ?? LibraryOrganizationDefaults.InboxFolderId,
            seriesId,
            libraryRoot ?? Path.GetTempPath()));

    public Task<LibraryMoveResult> MoveBooksAsync(LibraryMoveRequest request, CancellationToken cancellationToken)
    {
        LastMoveRequest = request;
        var folder = Folders.Single(item => item.Id == request.FolderId);
        var series = folder.Series.SingleOrDefault(item => item.Id == request.SeriesId);
        for (var index = 0; index < _libraryService.Books.Count; index++)
        {
            var book = _libraryService.Books[index];
            if (request.BookIds.Contains(book.Id))
            {
                _libraryService.Books[index] = book with
                {
                    FolderId = request.FolderId,
                    FolderName = folder.Name,
                    SeriesId = series?.Id,
                    SeriesName = series?.Name,
                };
            }
        }

        return Task.FromResult(new LibraryMoveResult(request.BookIds.Count, [], null));
    }

    public Task<LibraryBackupResult?> BackupBooksAsync(
        IReadOnlyCollection<Guid> bookIds,
        string operationName,
        CancellationToken cancellationToken)
    {
        BackupCallCount++;
        return Task.FromResult<LibraryBackupResult?>(new("backup", "manifest.json"));
    }

    public Task MigrateRootAsync(string targetRoot, CancellationToken cancellationToken)
    {
        LastMigrationTarget = targetRoot;
        return Task.CompletedTask;
    }
}
