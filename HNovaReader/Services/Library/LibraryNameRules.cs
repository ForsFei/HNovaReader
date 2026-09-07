using System.IO;

namespace HNovaReader.Services.Library;

internal static class LibraryNameRules
{
    private static readonly HashSet<string> ReservedNames = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    public static string Clean(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameterName);
        var cleaned = name.Trim();
        if (cleaned.Length > 160)
        {
            throw new ArgumentException("名称不能超过 160 个字符。", parameterName);
        }

        if (cleaned.EndsWith('.')
            || cleaned.EndsWith(' ')
            || cleaned.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || ReservedNames.Contains(cleaned)
            || cleaned.Equals(".hnova-backups", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("名称包含 Windows 文件夹不允许使用的字符或保留名称。", parameterName);
        }

        return cleaned;
    }

    public static string Normalize(string name) => name.ToUpperInvariant();
}
