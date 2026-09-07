namespace HNovaReader.Services.Library;

public interface IFilePathNormalizer
{
    NormalizedFilePath Normalize(string filePath);
}
