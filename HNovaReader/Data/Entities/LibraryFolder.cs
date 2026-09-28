namespace HNovaReader.Data.Entities;

public sealed class LibraryFolder
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public string RelativePath { get; set; } = string.Empty;

    public string NormalizedRelativePath { get; set; } = string.Empty;

    public string UnsortedName { get; set; } = Services.Library.LibraryOrganizationDefaults.UnsortedName;

    public int SortOrder { get; set; }

    public bool IsSystem { get; set; }

    public ICollection<Series> Series { get; } = new List<Series>();

    public ICollection<Book> Books { get; } = new List<Book>();
}
