namespace HNovaReader.Features.Library;

public interface ICoverFileStore
{
    Task<string> CopyAsync(Guid bookId, string sourcePath, CancellationToken cancellationToken);

    void DeleteManagedCopy(string? filePath);
}
