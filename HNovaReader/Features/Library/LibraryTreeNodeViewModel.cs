using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HNovaReader.Features.Library;

public enum LibraryTreeNodeKind
{
    Folder,
    Series,
    Unsorted,
}

public sealed partial class LibraryTreeNodeViewModel : ObservableObject
{
    public required LibraryTreeNodeKind Kind { get; init; }

    public required Guid FolderId { get; init; }

    public Guid? SeriesId { get; init; }

    public required string Name { get; init; }

    public required int Count { get; init; }

    public bool IsSystem { get; init; }

    public ObservableCollection<LibraryTreeNodeViewModel> Children { get; } = [];

    public bool CanCreateSeries => Kind == LibraryTreeNodeKind.Folder;

    public bool CanRename => true;

    public bool CanDelete => Kind == LibraryTreeNodeKind.Series
        || (Kind == LibraryTreeNodeKind.Folder && !IsSystem);

    public string DeleteActionText => Kind == LibraryTreeNodeKind.Series ? "删除系列" : "删除文件夹";

    [ObservableProperty]
    private bool isExpanded = true;
}
