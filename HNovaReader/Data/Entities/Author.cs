namespace HNovaReader.Data.Entities;

public sealed class Author
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public ICollection<Book> Books { get; } = new List<Book>();
}
