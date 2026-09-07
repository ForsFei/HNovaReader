namespace HNovaReader.Data.Entities;

public sealed class ReadingPosition
{
    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public long CharacterOffset { get; set; }

    public long? ByteOffsetHint { get; set; }

    public double Progress { get; set; }

    public string? ChapterTitleSnapshot { get; set; }

    public string? AnchorPrefix { get; set; }

    public string? AnchorSuffix { get; set; }

    public long FileSize { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public string EncodingName { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}
