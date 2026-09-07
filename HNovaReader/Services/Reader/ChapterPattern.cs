using System.Text.RegularExpressions;

namespace HNovaReader.Services.Reader;

public sealed class ChapterPattern
{
    private readonly Regex regex;

    public ChapterPattern(string name, string expression, int level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(level);

        Name = name;
        Level = level;
        regex = new Regex(
            expression,
            RegexOptions.Compiled
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
    }

    public string Name { get; }

    public int Level { get; }

    public bool IsMatch(string title)
    {
        return regex.IsMatch(title);
    }
}
