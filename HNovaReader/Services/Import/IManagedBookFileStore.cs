namespace HNovaReader.Services.Import;

public interface IManagedBookFileStore
{
    Task<string> CopyAsync(
        string sourceFilePath,
        string destinationDirectory,
        CancellationToken cancellationToken);

    void DeleteCopy(string filePath);
}
