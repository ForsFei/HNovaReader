namespace HNovaReader.Data.Entities;

public sealed class Tag
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public ICollection<BookTag> BookTags { get; } = new List<BookTag>();
}
