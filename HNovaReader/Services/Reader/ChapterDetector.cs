namespace HNovaReader.Services.Reader;

public sealed class ChapterDetector : IChapterDetector
{
    private const string Number = "[0-9０-９零〇一二三四五六七八九十百千万两壹贰叁肆伍陆柒捌玖拾佰仟]+";
    private const string Suffix = "(?:\\s+.+|[:：、.\\-—]\\s*.+)?";
    private readonly IReadOnlyList<ChapterPattern> patterns;

    public ChapterDetector()
        : this(
        [
            new ChapterPattern("ChineseVolume", $"^第{Number}[卷部篇集]{Suffix}$", level: 1),
            new ChapterPattern("ChineseChapter", $"^第{Number}[章节回]{Suffix}$", level: 2),
            new ChapterPattern(
                "EnglishChapter",
                "^Chapter\\s+(?:[0-9]+|[IVXLCDM]+)(?:\\s+.+|[:.\\-—]\\s*.+)?$",
                level: 2),
        ])
    {
    }

    public ChapterDetector(IReadOnlyList<ChapterPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        if (patterns.Count == 0)
        {
            throw new ArgumentException("至少需要一个章节规则。", nameof(patterns));
        }

        this.patterns = patterns;
    }

    public int PatternVersion => 1;

    public int MaximumLineLength => 160;

    public ChapterMatch? Detect(string line, long lineStartOffset)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegative(lineStartOffset);

        var title = line.Trim();
        if (title.Length == 0 || title.Length > MaximumLineLength)
        {
            return null;
        }

        foreach (var pattern in patterns)
        {
            if (pattern.IsMatch(title))
            {
                return new ChapterMatch(title, pattern.Level, lineStartOffset);
            }
        }

        return null;
    }
}
