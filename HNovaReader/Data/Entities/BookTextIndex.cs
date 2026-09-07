namespace HNovaReader.Data.Entities;

public sealed class BookTextIndex
{
    public Guid Id { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public string EncodingName { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public int PatternVersion { get; set; }

    public long TotalCharacterCount { get; set; }

    public bool IsComplete { get; set; }

    public DateTime BuiltAtUtc { get; set; }

    public ICollection<TextCheckpoint> Checkpoints { get; } = new List<TextCheckpoint>();

    public ICollection<Chapter> Chapters { get; } = new List<Chapter>();
}
