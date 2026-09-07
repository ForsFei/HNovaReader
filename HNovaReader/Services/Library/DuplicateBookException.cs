namespace HNovaReader.Services.Library;

public sealed class DuplicateBookException(string filePath, Exception? innerException = null)
    : InvalidOperationException($"书库中已存在文件：{filePath}", innerException)
{
    public string FilePath { get; } = filePath;
}
