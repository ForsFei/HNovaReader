using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HNovaReader.Services.Library;

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

    public string RelativePath { get; init; } = "";

    public ObservableCollection<LibraryTreeNodeViewModel> Children { get; } = [];

    public bool CanRename => FolderId != LibraryOrganizationDefaults.RootFolderId;

    public bool CanDelete => !IsSystem;

    public string DeleteActionText => "删除文件夹";

    [ObservableProperty]
    private bool isExpanded = true;
}
