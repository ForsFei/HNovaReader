namespace HNovaReader.Features.Library;

public enum LibrarySection
{
    All,
    Recent,
    Reading,
    Unread,
    Finished,
    Favorites,
    Series,
    Tags,
}

public sealed record LibraryGroupViewModel(
    string Name,
    IReadOnlyList<LibraryBookItemViewModel> Books)
{
    public int Count => Books.Count;
}
