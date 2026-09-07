namespace HNovaReader.Services.Library;

public interface ILibraryService
{
    Task<IReadOnlyList<BookDetails>> GetBooksAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BookDetails>> GetSeriesBooksAsync(
        Guid seriesId,
        CancellationToken cancellationToken);

    Task<BookDetails?> GetBookAsync(Guid bookId, CancellationToken cancellationToken);

    Task<bool> ContainsFilePathAsync(string filePath, CancellationToken cancellationToken);

    Task<BookDetails> AddBookAsync(
        AddBookRequest request,
        CancellationToken cancellationToken);

    Task<BookDetails?> UpdateBookAsync(
        Guid bookId,
        UpdateBookRequest request,
        CancellationToken cancellationToken);

    Task<BookDetails?> RefreshBookFileInfoAsync(
        Guid bookId,
        RefreshBookFileInfoRequest request,
        CancellationToken cancellationToken);

    Task<BookDetails?> RelocateBookFileAsync(
        Guid bookId,
        RelocateBookFileRequest request,
        CancellationToken cancellationToken);

    Task<bool> RemoveBookAsync(Guid bookId, CancellationToken cancellationToken);
}
