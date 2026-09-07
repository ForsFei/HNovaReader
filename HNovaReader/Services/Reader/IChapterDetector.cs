namespace HNovaReader.Services.Reader;

public interface IChapterDetector
{
    int PatternVersion { get; }

    int MaximumLineLength { get; }

    ChapterMatch? Detect(string line, long lineStartOffset);
}
