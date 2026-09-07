using System.IO;

namespace HNovaReader.Services.Import;

public sealed class ManagedBookFileStore : IManagedBookFileStore
{
    private const int CopyBufferSize = 128 * 1024;

    public async Task<string> CopyAsync(
        string sourceFilePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var fullSourcePath = Path.GetFullPath(sourceFilePath);
        var fullDestinationDirectory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDestinationDirectory);

        var sourceInfo = new FileInfo(fullSourcePath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("找不到要复制的 TXT 文件。", fullSourcePath);
        }

        var baseName = Path.GetFileNameWithoutExtension(sourceInfo.Name);
        var extension = sourceInfo.Extension;
        for (var suffix = 1; ; suffix++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = suffix == 1
                ? sourceInfo.Name
                : $"{baseName} ({suffix}){extension}";
            var destinationPath = Path.Combine(fullDestinationDirectory, fileName);

            FileStream destination;
            try
            {
                destination = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    CopyBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                continue;
            }

            try
            {
                await using (destination)
                {
                    await using var source = new FileStream(
                        fullSourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        CopyBufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await source.CopyToAsync(destination, CopyBufferSize, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }

                File.SetLastWriteTimeUtc(destinationPath, sourceInfo.LastWriteTimeUtc);
                return destinationPath;
            }
            catch
            {
                await destination.DisposeAsync();
                DeleteCopy(destinationPath);
                throw;
            }
        }
    }

    public void DeleteCopy(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
