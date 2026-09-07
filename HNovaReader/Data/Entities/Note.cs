namespace HNovaReader.Data.Entities;

public sealed class Note
{
    public Guid Id { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public Guid? HighlightId { get; set; }

    public Highlight? Highlight { get; set; }

    public long? CharacterOffset { get; set; }

    public string PreviewText { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? AnchorPrefix { get; set; }

    public string? AnchorSuffix { get; set; }

    public long FileSize { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public string EncodingName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
