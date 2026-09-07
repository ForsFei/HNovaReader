namespace HNovaReader.Data.Entities;

public sealed class TextCheckpoint
{
    public Guid BookTextIndexId { get; set; }

    public BookTextIndex BookTextIndex { get; set; } = null!;

    public long CharacterOffset { get; set; }

    public long ByteOffset { get; set; }
}
