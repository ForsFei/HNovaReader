using HNovaReader.Services.Reader;

namespace HNovaReader.Tests.Services;

public sealed class ChapterDetectorTests
{
    [Theory]
    [InlineData("第一章", 2)]
    [InlineData("第十章 相遇", 2)]
    [InlineData("第一百二十三章：归途", 2)]
    [InlineData("第1章", 2)]
    [InlineData("第001章 序幕", 2)]
    [InlineData("第一回 风雪夜", 2)]
    [InlineData("第一卷", 1)]
    [InlineData("第二部 星海", 1)]
    [InlineData("Chapter 1", 2)]
    [InlineData("chapter IV: Return", 2)]
    public void Detect_RecognizesSupportedChapterTitles(string line, int expectedLevel)
    {
        var result = new ChapterDetector().Detect(line, 42);

        Assert.NotNull(result);
        Assert.Equal(line, result.Title);
        Assert.Equal(expectedLevel, result.Level);
        Assert.Equal(42, result.StartOffset);
    }

    [Theory]
    [InlineData("我们将在第一章讨论这个问题。")]
    [InlineData("第一章和第二章")]
    [InlineData("第X章")]
    [InlineData("")]
    public void Detect_RejectsProseAndUnsupportedTitles(string line)
    {
        Assert.Null(new ChapterDetector().Detect(line, 0));
    }

    [Fact]
    public void Detect_RejectsUnreasonablyLongStandaloneLine()
    {
        var line = $"第一章 {new string('长', 200)}";

        Assert.Null(new ChapterDetector().Detect(line, 0));
    }

    [Fact]
    public void CustomPattern_CanExtendDetectorWithoutChangingBuiltInRegexes()
    {
        var detector = new ChapterDetector(
        [
            new ChapterPattern("CustomPrologue", "^序幕$", level: 1),
        ]);

        var result = detector.Detect("序幕", 9);

        Assert.NotNull(result);
        Assert.Equal(1, result.Level);
        Assert.Equal(9, result.StartOffset);
    }
}
