namespace HNovaReader.Data.Entities;

public sealed class Chapter
{
    public Guid BookTextIndexId { get; set; }

    public BookTextIndex BookTextIndex { get; set; } = null!;

    public int Ordinal { get; set; }

    public string Title { get; set; } = string.Empty;

    public int Level { get; set; }

    public long StartOffset { get; set; }

    public long EndOffset { get; set; }
}
