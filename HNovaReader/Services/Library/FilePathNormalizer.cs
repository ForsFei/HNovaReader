using System.IO;

namespace HNovaReader.Services.Library;

public sealed class FilePathNormalizer : IFilePathNormalizer
{
    public NormalizedFilePath Normalize(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath.Trim());
        return new NormalizedFilePath(fullPath, fullPath.ToUpperInvariant());
    }
}
