namespace HNovaReader.Data.Entities;

public sealed class Series
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public Guid? FolderId { get; set; }

    public LibraryFolder? Folder { get; set; }

    public ICollection<Book> Books { get; } = new List<Book>();
}
