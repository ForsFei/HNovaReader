namespace HNovaReader.Data.Entities;

public sealed class Bookmark
{
    public Guid Id { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public long CharacterOffset { get; set; }

    public string? ChapterTitleSnapshot { get; set; }

    public string PreviewText { get; set; } = string.Empty;

    public string? AnchorPrefix { get; set; }

    public string? AnchorSuffix { get; set; }

    public long FileSize { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public string EncodingName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
