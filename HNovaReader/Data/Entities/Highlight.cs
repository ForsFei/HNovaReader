namespace HNovaReader.Data.Entities;

public sealed class Highlight
{
    public Guid Id { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public long StartOffset { get; set; }

    public int Length { get; set; }

    public string SelectedText { get; set; } = string.Empty;

    public HighlightStyle Style { get; set; }

    public string? AnchorPrefix { get; set; }

    public string? AnchorSuffix { get; set; }

    public long FileSize { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public string EncodingName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Note> Notes { get; } = new List<Note>();
}
