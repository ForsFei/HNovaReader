using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HNovaReader.Data.Entities;
using HNovaReader.Features.Shared;
using HNovaReader.Services.Import;
using HNovaReader.Services.Library;

namespace HNovaReader.Features.Library;

public sealed partial class LibraryViewModel : PageViewModel, IDisposable
{
    private const int GridColumnCount = 4;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(225);
    private readonly ILibraryService _libraryService;
    private readonly ITextEncodingDetector _encodingDetector;
    private readonly ICoverFileStore _coverFileStore;
    private readonly ILibraryInteractionService _interactionService;
    private readonly ILibraryOrganizationService _organizationService;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellationSource = new();
    private IReadOnlyList<LibraryBookItemViewModel> _allBooks = [];
    private CancellationTokenSource? _searchCancellationSource;
    private Task _pendingSearch = Task.CompletedTask;
    private readonly HashSet<Guid> _selectedBookIds = [];
    private Guid? _selectionAnchorId;

    public LibraryViewModel(
        ILibraryService libraryService,
        ITextEncodingDetector encodingDetector,
        ICoverFileStore coverFileStore,
        ILibraryInteractionService interactionService,
        ILibraryOrganizationService organizationService)
        : base("我的书库", "管理和阅读保存在本机的 TXT 电子书")
    {
        _libraryService = libraryService;
        _encodingDetector = encodingDetector;
        _coverFileStore = coverFileStore;
        _interactionService = interactionService;
        _organizationService = organizationService;
        statusMessage = "正在载入书库…";
    }

    public ObservableCollection<LibraryBookItemViewModel> Books { get; } = [];

    public ObservableCollection<LibraryBookRowViewModel> BookRows { get; } = [];

    public ObservableCollection<LibraryGroupViewModel> Groups { get; } = [];

    public ObservableCollection<LibraryTreeNodeViewModel> LibraryTree { get; } = [];

    public event Func<BookDetails, CancellationToken, Task>? BookOpenRequested;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridView))]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    private LibraryViewMode viewMode = LibraryViewMode.Grid;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasBooks;

    [ObservableProperty]
    private bool hasVisibleBooks;

    [ObservableProperty]
    private string statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionTitle))]
    [NotifyPropertyChangedFor(nameof(SectionDescription))]
    [NotifyPropertyChangedFor(nameof(IsGroupSection))]
    [NotifyPropertyChangedFor(nameof(IsGroupOverviewVisible))]
    [NotifyPropertyChangedFor(nameof(IsBookCollectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsContinueReadingVisible))]
    private LibrarySection section;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGroupOverviewVisible))]
    [NotifyPropertyChangedFor(nameof(IsBookCollectionVisible))]
    private string? activeGroupName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContinueReadingVisible))]
    private LibraryBookItemViewModel? continueReadingBook;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionTitle))]
    [NotifyPropertyChangedFor(nameof(SectionDescription))]
    private LibraryTreeNodeViewModel? selectedLibraryNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedBooks))]
    private int selectedBookCount;

    public bool HasSelectedBooks => SelectedBookCount > 0;

    public bool IsGridView => ViewMode == LibraryViewMode.Grid;

    public bool IsListView => ViewMode == LibraryViewMode.List;

    public string SectionTitle => Section switch
    {
        LibrarySection.Recent => "最近阅读",
        LibrarySection.Reading => "阅读中",
        LibrarySection.Unread => "未读",
        LibrarySection.Finished => "已读",
        LibrarySection.Favorites => "收藏",
        LibrarySection.Series => ActiveGroupName is null ? "系列" : ActiveGroupName,
        LibrarySection.Tags => ActiveGroupName is null ? "标签" : ActiveGroupName,
        _ => SelectedLibraryNode?.Name ?? "我的书库",
    };

    public string SectionDescription => Section switch
    {
        LibrarySection.Recent => "按最后打开时间浏览本地书籍",
        LibrarySection.Reading => "阅读进度在 0% 到 95% 之间",
        LibrarySection.Unread => "尚未开始阅读的书籍",
        LibrarySection.Finished => "阅读进度达到 95% 的书籍",
        LibrarySection.Favorites => "快速找到收藏的书籍",
        LibrarySection.Series => ActiveGroupName is null ? "按系列组织书籍" : "按系列序号排列",
        LibrarySection.Tags => ActiveGroupName is null ? "按标签组织书籍" : "该标签下的书籍",
        _ => SelectedLibraryNode is null
            ? "管理和阅读保存在本机的 TXT 电子书"
            : SelectedLibraryNode.Kind == LibraryTreeNodeKind.Folder
                ? "该文件夹中的全部书籍"
                : "书籍位置与磁盘目录保持同步",
    };

    public bool IsGroupSection => Section is LibrarySection.Series or LibrarySection.Tags;

    public bool IsGroupOverviewVisible => IsGroupSection && ActiveGroupName is null;

    public bool IsBookCollectionVisible => !IsGroupSection || ActiveGroupName is not null;

    public bool IsContinueReadingVisible => Section == LibrarySection.All
        && ContinueReadingBook is not null;

    public string EmptyMessage => _allBooks.Count == 0
        ? "将 TXT 文件或文件夹拖入窗口，或使用右上角导入按钮。"
        : "没有匹配当前搜索条件的书籍。";

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return ReloadAsync(cancellationToken);
    }

    public void SetSection(LibrarySection section)
    {
        SelectedLibraryNode = null;
        ClearSelection();
        Section = section;
        ActiveGroupName = null;
        ApplyFilter();
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        IsBusy = true;
        try
        {
            var folders = await _organizationService.GetFoldersAsync(cancellationToken);
            var books = await _libraryService.GetBooksAsync(cancellationToken);
            (Guid FolderId, Guid? SeriesId, LibraryTreeNodeKind Kind)? selectedTarget = SelectedLibraryNode is null
                ? null
                : (SelectedLibraryNode.FolderId, SelectedLibraryNode.SeriesId, SelectedLibraryNode.Kind);
            _allBooks = books
                .Select(book => new LibraryBookItemViewModel(book, this))
                .ToArray();
            RebuildLibraryTree(folders, selectedTarget);
            RestoreSelection();
            HasBooks = _allBooks.Count > 0;
            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            StatusMessage = $"书库加载失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            _reloadGate.Release();
        }
    }

    public Task WaitForPendingSearchAsync()
    {
        return _pendingSearch;
    }

    [RelayCommand]
    private void ShowGrid()
    {
        ViewMode = LibraryViewMode.Grid;
    }

    [RelayCommand]
    private void ShowList()
    {
        ViewMode = LibraryViewMode.List;
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        return ReloadAsync(_lifetimeCancellationSource.Token);
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        var name = _interactionService.PromptForName("新建文件夹", "文件夹名称");
        if (name is null)
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            var created = await _organizationService.CreateFolderAsync(name, cancellationToken);
            await ReloadAsync(cancellationToken);
            SelectLibraryNode(FindTreeNode(created.Id, null, LibraryTreeNodeKind.Folder));
            StatusMessage = $"已创建文件夹：{created.Name}";
        });
    }

    [RelayCommand]
    private async Task CreateSeriesAsync()
    {
        var folderId = SelectedLibraryNode?.FolderId;
        if (folderId is null)
        {
            _interactionService.ShowMessage("选择文件夹", "请先在左侧选择要创建系列的文件夹。", isError: true);
            return;
        }

        var name = _interactionService.PromptForName("新建系列", "系列名称");
        if (name is null)
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            var created = await _organizationService.CreateSeriesAsync(folderId.Value, name, cancellationToken);
            await ReloadAsync(cancellationToken);
            SelectLibraryNode(FindTreeNode(created.FolderId, created.Id, LibraryTreeNodeKind.Series));
            StatusMessage = $"已创建系列：{created.Name}";
        });
    }

    [RelayCommand]
    private async Task RenameLibraryNodeAsync()
    {
        var node = SelectedLibraryNode;
        if (node is null || !node.CanRename)
        {
            return;
        }

        var name = _interactionService.PromptForName(
            node.Kind switch
            {
                LibraryTreeNodeKind.Folder => "重命名文件夹",
                LibraryTreeNodeKind.Unsorted => "重命名未分系列",
                _ => "重命名系列",
            },
            "新名称",
            node.Name);
        if (name is null || string.Equals(name, node.Name, StringComparison.CurrentCulture))
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            if (node.Kind == LibraryTreeNodeKind.Folder)
            {
                await _organizationService.RenameFolderAsync(node.FolderId, name, cancellationToken);
            }
            else if (node.Kind == LibraryTreeNodeKind.Unsorted)
            {
                await _organizationService.RenameUnsortedAsync(node.FolderId, name, cancellationToken);
            }
            else if (node.SeriesId is { } seriesId)
            {
                await _organizationService.RenameSeriesAsync(seriesId, name, cancellationToken);
            }

            var target = (node.FolderId, node.SeriesId, node.Kind);
            await ReloadAsync(cancellationToken);
            SelectLibraryNode(FindTreeNode(target.FolderId, target.SeriesId, target.Kind));
            StatusMessage = $"已重命名为：{name}";
        });
    }

    [RelayCommand]
    private async Task DeleteLibraryNodeAsync()
    {
        var node = SelectedLibraryNode;
        if (node is null || !node.CanDelete)
        {
            return;
        }

        var folderNode = LibraryTree.First(item => item.FolderId == node.FolderId);
        var message = node.Kind == LibraryTreeNodeKind.Series
            ? $"确定删除系列“{node.Name}”吗？书籍和 TXT 文件不会删除，将先备份并移至“{folderNode.Children.First(item => item.Kind == LibraryTreeNodeKind.Unsorted).Name}”。"
            : $"确定删除文件夹“{node.Name}”吗？其中的书籍和 TXT 文件不会删除，将先备份并移至系统收件箱。";
        if (!_interactionService.Confirm("确认删除", message, isDangerous: true))
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            if (node.Kind == LibraryTreeNodeKind.Series && node.SeriesId is { } seriesId)
            {
                await _organizationService.DeleteSeriesAsync(seriesId, cancellationToken);
                await ReloadAsync(cancellationToken);
                SelectLibraryNode(FindTreeNode(node.FolderId, null, LibraryTreeNodeKind.Unsorted));
                StatusMessage = $"已删除系列“{node.Name}”，书籍已移至未分系列";
            }
            else
            {
                await _organizationService.DeleteFolderAsync(node.FolderId, cancellationToken);
                await ReloadAsync(cancellationToken);
                SelectLibraryNode(FindTreeNode(
                    LibraryOrganizationDefaults.InboxFolderId,
                    null,
                    LibraryTreeNodeKind.Folder));
                StatusMessage = $"已删除文件夹“{node.Name}”，书籍已移至收件箱";
            }
        });
    }

    public void SelectLibraryNode(LibraryTreeNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        ClearSelection();
        SelectedLibraryNode = node;
        Section = LibrarySection.All;
        ActiveGroupName = null;
        ApplyFilter();
    }

    public async Task MoveSelectedBooksAsync(LibraryTreeNodeViewModel target)
    {
        var ids = _selectedBookIds.ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            var result = await _organizationService.MoveBooksAsync(
                new LibraryMoveRequest(
                    ids,
                    target.FolderId,
                    target.Kind == LibraryTreeNodeKind.Series ? target.SeriesId : null),
                cancellationToken);
            ClearSelection();
            await ReloadAsync(cancellationToken);
            SelectLibraryNode(FindTreeNode(
                target.FolderId,
                target.Kind == LibraryTreeNodeKind.Series ? target.SeriesId : null,
                target.Kind));

            StatusMessage = result.Failures.Count == 0
                ? $"已移动 {result.MovedCount} 本书"
                : $"已移动 {result.MovedCount} 本，{result.Failures.Count} 本未移动";
            if (result.Failures.Count > 0)
            {
                _interactionService.ShowMessage(
                    "部分书籍未移动",
                    string.Join(Environment.NewLine, result.Failures.Select(item => $"《{item.Title}》：{item.Message}")),
                    isError: true);
            }
        });
    }

    public async Task MoveSeriesAsync(Guid seriesId, LibraryTreeNodeViewModel target)
    {
        if (target.Kind != LibraryTreeNodeKind.Folder)
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            var moved = await _organizationService.MoveSeriesAsync(
                seriesId,
                target.FolderId,
                cancellationToken);
            await ReloadAsync(cancellationToken);
            SelectLibraryNode(FindTreeNode(
                moved.FolderId,
                moved.Id,
                LibraryTreeNodeKind.Series));
            StatusMessage = $"已将系列“{moved.Name}”移动到“{target.Name}”";
        });
    }

    public void SelectBook(LibraryBookItemViewModel item, bool ctrlPressed, bool shiftPressed)
    {
        if (shiftPressed && _selectionAnchorId is { } anchorId)
        {
            var anchorIndex = Books.ToList().FindIndex(book => book.Id == anchorId);
            var currentIndex = Books.IndexOf(item);
            if (anchorIndex >= 0 && currentIndex >= 0)
            {
                if (!ctrlPressed)
                {
                    _selectedBookIds.Clear();
                }

                var start = Math.Min(anchorIndex, currentIndex);
                var end = Math.Max(anchorIndex, currentIndex);
                for (var index = start; index <= end; index++)
                {
                    _selectedBookIds.Add(Books[index].Id);
                }
            }
        }
        else if (ctrlPressed)
        {
            if (!_selectedBookIds.Add(item.Id))
            {
                _selectedBookIds.Remove(item.Id);
            }

            _selectionAnchorId = item.Id;
        }
        else
        {
            _selectedBookIds.Clear();
            _selectedBookIds.Add(item.Id);
            _selectionAnchorId = item.Id;
        }

        RestoreSelection();
    }

    public IReadOnlyCollection<Guid> GetSelectedBookIdsForDrag(LibraryBookItemViewModel item)
    {
        if (!_selectedBookIds.Contains(item.Id))
        {
            SelectBook(item, ctrlPressed: false, shiftPressed: false);
        }

        return _selectedBookIds.ToArray();
    }

    internal async Task OpenBookAsync(LibraryBookItemViewModel item)
    {
        if (item.IsFileMissing)
        {
            StatusMessage = $"文件丢失：{item.Title}";
            _interactionService.ShowMessage(
                "文件丢失",
                "找不到书籍对应的 TXT 文件。请使用“重新定位”选择新的文件位置。",
                isError: true);
            return;
        }

        var handlers = BookOpenRequested?.GetInvocationList()
            .Cast<Func<BookDetails, CancellationToken, Task>>()
            .ToArray();
        if (handlers is null || handlers.Length == 0)
        {
            _interactionService.ShowMessage("无法打开书籍", "阅读页面尚未完成初始化。", isError: true);
            return;
        }

        foreach (var handler in handlers)
        {
            await handler(item.Details, _lifetimeCancellationSource.Token);
        }
    }

    internal async Task ToggleFavoriteAsync(LibraryBookItemViewModel item)
    {
        await UpdateBookAsync(
            item,
            CreateUpdateRequest(item.Details, isFavorite: !item.Details.IsFavorite),
            item.Details.CoverPath,
            null,
            item.Details.IsFavorite ? "已取消收藏" : "已加入收藏");
    }

    internal async Task EditBookAsync(LibraryBookItemViewModel item)
    {
        string? copiedCoverPath = null;
        var seriesMoved = false;
        try
        {
            var result = _interactionService.EditBook(item.Details);
            if (result is null)
            {
                return;
            }

            var desiredSeriesName = string.IsNullOrWhiteSpace(result.Update.SeriesName)
                ? null
                : result.Update.SeriesName.Trim();
            if (!string.Equals(
                    desiredSeriesName,
                    item.Details.SeriesName,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                var folderId = item.Details.FolderId ?? LibraryOrganizationDefaults.InboxFolderId;
                var folders = await _organizationService.GetFoldersAsync(_lifetimeCancellationSource.Token);
                var folder = folders.Single(candidate => candidate.Id == folderId);
                Guid? targetSeriesId = null;
                if (desiredSeriesName is not null)
                {
                    var targetSeries = folder.Series.FirstOrDefault(series => string.Equals(
                        series.Name,
                        desiredSeriesName,
                        StringComparison.CurrentCultureIgnoreCase));
                    targetSeriesId = targetSeries?.Id
                        ?? (await _organizationService.CreateSeriesAsync(
                            folderId,
                            desiredSeriesName,
                            _lifetimeCancellationSource.Token)).Id;
                }

                var moveResult = await _organizationService.MoveBooksAsync(
                    new LibraryMoveRequest(
                        [item.Id],
                        folderId,
                        targetSeriesId,
                        "编辑书籍系列"),
                    _lifetimeCancellationSource.Token);
                if (moveResult.Failures.Count > 0 || moveResult.MovedCount != 1)
                {
                    throw new InvalidOperationException(
                        moveResult.Failures.FirstOrDefault()?.Message ?? "无法移动书籍到目标系列。");
                }

                seriesMoved = true;
            }

            var targetCoverPath = item.Details.CoverPath;
            if (result.IsCoverChanged)
            {
                targetCoverPath = null;
                if (!string.IsNullOrWhiteSpace(result.SelectedCoverPath))
                {
                    copiedCoverPath = await _coverFileStore.CopyAsync(
                        item.Id,
                        result.SelectedCoverPath,
                        _lifetimeCancellationSource.Token);
                    targetCoverPath = copiedCoverPath;
                }
            }

            await UpdateBookAsync(
                item,
                result.Update with { CoverPath = targetCoverPath },
                targetCoverPath,
                copiedCoverPath,
                "书籍信息已更新");
        }
        catch (Exception exception)
        {
            if (seriesMoved)
            {
                try
                {
                    await _organizationService.MoveBooksAsync(
                        new LibraryMoveRequest(
                            [item.Id],
                            item.Details.FolderId ?? LibraryOrganizationDefaults.InboxFolderId,
                            item.Details.SeriesId,
                            "回退书籍系列修改"),
                        CancellationToken.None);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(copiedCoverPath))
            {
                _coverFileStore.DeleteManagedCopy(copiedCoverPath);
            }

            ReportError("无法更新书籍", exception);
        }
    }

    internal async Task ChangeCoverAsync(LibraryBookItemViewModel item)
    {
        string? copiedCoverPath = null;
        try
        {
            var selectedPath = _interactionService.PickCoverImage();
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            copiedCoverPath = await _coverFileStore.CopyAsync(
                item.Id,
                selectedPath,
                _lifetimeCancellationSource.Token);
            await UpdateBookAsync(
                item,
                CreateUpdateRequest(item.Details, coverPath: copiedCoverPath),
                copiedCoverPath,
                copiedCoverPath,
                "封面已更新");
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrWhiteSpace(copiedCoverPath))
            {
                _coverFileStore.DeleteManagedCopy(copiedCoverPath);
            }

            ReportError("无法更新封面", exception);
        }
    }

    internal void RevealFile(LibraryBookItemViewModel item)
    {
        try
        {
            _interactionService.RevealFile(item.Details.FilePath);
            StatusMessage = $"已定位：{item.Title}";
        }
        catch (Exception exception)
        {
            ReportError("无法打开文件位置", exception);
        }
    }

    internal async Task RefreshFileInfoAsync(LibraryBookItemViewModel item)
    {
        await RunOperationAsync(async cancellationToken =>
        {
            var file = new FileInfo(item.Details.FilePath);
            if (!file.Exists)
            {
                throw new FileNotFoundException("找不到书籍对应的 TXT 文件。", file.FullName);
            }

            var encoding = await _encodingDetector.DetectAsync(file.FullName, cancellationToken);
            await _libraryService.RefreshBookFileInfoAsync(
                item.Id,
                new RefreshBookFileInfoRequest
                {
                    FileSize = file.Length,
                    LastWriteTimeUtc = file.LastWriteTimeUtc,
                    FileEncoding = encoding.EncodingName,
                },
                cancellationToken);
            await ReloadAsync(cancellationToken);
            StatusMessage = $"已重新读取：{item.Title}";
        });
    }

    internal async Task RelocateFileAsync(LibraryBookItemViewModel item)
    {
        var selectedPath = _interactionService.PickReplacementTextFile(item.Details);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            var file = new FileInfo(selectedPath);
            if (!file.Exists)
            {
                throw new FileNotFoundException("找不到选择的 TXT 文件。", selectedPath);
            }

            var encoding = await _encodingDetector.DetectAsync(file.FullName, cancellationToken);
            var relocated = await _libraryService.RelocateBookFileAsync(
                item.Id,
                new RelocateBookFileRequest
                {
                    FilePath = file.FullName,
                    FileSize = file.Length,
                    LastWriteTimeUtc = file.LastWriteTimeUtc,
                    FileEncoding = encoding.EncodingName,
                },
                cancellationToken);
            if (relocated is null)
            {
                throw new InvalidOperationException("书籍记录已不存在。");
            }

            await ReloadAsync(cancellationToken);
            StatusMessage = $"已重新定位：{item.Title}";
        });
    }

    internal async Task RemoveAsync(LibraryBookItemViewModel item)
    {
        if (!_interactionService.Confirm(
                "从书库移除",
                $"确定从书库移除《{item.Title}》吗？\n\nTXT 文件不会被删除。"))
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            if (await _libraryService.RemoveBookAsync(item.Id, cancellationToken))
            {
                _coverFileStore.DeleteManagedCopy(item.Details.CoverPath);
                await ReloadAsync(cancellationToken);
                StatusMessage = $"已从书库移除：{item.Title}";
            }
        });
    }

    internal async Task DeleteFileAsync(LibraryBookItemViewModel item)
    {
        var fileDescription = item.Details.StorageMode == BookStorageMode.ManagedCopy
            ? "将把 HNovaReader 管理的内部副本移入回收站；原始源文件仍会保留。"
            : "将把原始 TXT 文件移入 Windows 回收站。";
        if (!_interactionService.Confirm(
                "删除磁盘文件",
                $"确定删除《{item.Title}》吗？\n\n{fileDescription}\n此操作同时会从书库移除记录。",
                isDangerous: true))
        {
            return;
        }

        await RunOperationAsync(async cancellationToken =>
        {
            await _organizationService.BackupBooksAsync(
                [item.Id],
                "删除磁盘文件",
                cancellationToken);
            _interactionService.MoveFileToRecycleBin(item.Details.FilePath);
            if (!await _libraryService.RemoveBookAsync(item.Id, cancellationToken))
            {
                throw new InvalidOperationException("书籍记录已不存在。");
            }

            _coverFileStore.DeleteManagedCopy(item.Details.CoverPath);
            await ReloadAsync(cancellationToken);
            StatusMessage = $"已移入回收站：{item.Title}";
        });
    }

    public void Dispose()
    {
        _lifetimeCancellationSource.Cancel();
        _searchCancellationSource?.Cancel();
        _searchCancellationSource?.Dispose();
        _lifetimeCancellationSource.Dispose();
        _reloadGate.Dispose();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCancellationSource?.Cancel();
        _searchCancellationSource?.Dispose();
        _searchCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellationSource.Token);
        _pendingSearch = DebounceSearchAsync(_searchCancellationSource.Token);
    }

    partial void OnViewModeChanged(LibraryViewMode value)
    {
        StatusMessage = $"已切换到{(value == LibraryViewMode.Grid ? "书架" : "列表")}视图";
    }

    [RelayCommand]
    private void OpenGroup(LibraryGroupViewModel? group)
    {
        if (group is null)
        {
            return;
        }

        ActiveGroupName = group.Name;
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));
        ApplyFilter();
    }

    [RelayCommand]
    private void BackToGroups()
    {
        ActiveGroupName = null;
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));
        ApplyFilter();
    }

    private async Task DebounceSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDelay, cancellationToken);
            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        IReadOnlyList<LibraryBookItemViewModel> searched = string.IsNullOrWhiteSpace(query)
            ? _allBooks
            : _allBooks.Where(book => Matches(book.Details, query)).ToArray();

        if (SelectedLibraryNode is { } node && Section == LibrarySection.All)
        {
            searched = searched.Where(book => node.Kind switch
            {
                LibraryTreeNodeKind.Folder => book.Details.FolderId == node.FolderId,
                LibraryTreeNodeKind.Series => book.Details.SeriesId == node.SeriesId,
                LibraryTreeNodeKind.Unsorted => book.Details.FolderId == node.FolderId
                    && book.Details.SeriesId is null,
                _ => true,
            }).ToArray();
        }

        ContinueReadingBook = _allBooks
            .Where(book => book.Details.LastOpenedAtUtc is not null)
            .OrderByDescending(book => book.Details.LastOpenedAtUtc)
            .FirstOrDefault();

        RebuildGroups(searched);
        IReadOnlyList<LibraryBookItemViewModel> filtered = Section switch
        {
            LibrarySection.Recent => searched
                .Where(book => book.Details.LastOpenedAtUtc is not null)
                .OrderByDescending(book => book.Details.LastOpenedAtUtc)
                .ToArray(),
            LibrarySection.Reading => searched
                .Where(book => book.ReadingProgress is > 0 and < 0.95)
                .ToArray(),
            LibrarySection.Unread => searched
                .Where(book => book.Details.ReadingProgress is null or <= 0)
                .ToArray(),
            LibrarySection.Finished => searched
                .Where(book => book.ReadingProgress >= 0.95)
                .ToArray(),
            LibrarySection.Favorites => searched.Where(book => book.Details.IsFavorite).ToArray(),
            LibrarySection.Series or LibrarySection.Tags when ActiveGroupName is not null =>
                Groups.FirstOrDefault(group => group.Name == ActiveGroupName)?.Books ?? [],
            LibrarySection.Series or LibrarySection.Tags => [],
            _ => searched,
        };

        Books.Clear();
        foreach (var book in filtered)
        {
            Books.Add(book);
        }

        BookRows.Clear();
        foreach (var row in filtered.Chunk(GridColumnCount))
        {
            BookRows.Add(new LibraryBookRowViewModel(row));
        }

        HasVisibleBooks = IsGroupOverviewVisible ? Groups.Count > 0 : Books.Count > 0;
        OnPropertyChanged(nameof(EmptyMessage));
        StatusMessage = IsGroupOverviewVisible
            ? $"共 {Groups.Count} 个{(Section == LibrarySection.Series ? "系列" : "标签")}"
            : string.IsNullOrWhiteSpace(query)
                ? $"共 {Books.Count} 本书"
                : $"找到 {Books.Count} / {_allBooks.Count} 本书";
    }

    private void RebuildLibraryTree(
        IReadOnlyList<LibraryFolderDetails> folders,
        (Guid FolderId, Guid? SeriesId, LibraryTreeNodeKind Kind)? selectedTarget)
    {
        LibraryTree.Clear();
        foreach (var folder in folders)
        {
            var folderNode = new LibraryTreeNodeViewModel
            {
                Kind = LibraryTreeNodeKind.Folder,
                FolderId = folder.Id,
                Name = folder.Name,
                Count = folder.BookCount,
                IsSystem = folder.IsSystem,
            };
            folderNode.Children.Add(new LibraryTreeNodeViewModel
            {
                Kind = LibraryTreeNodeKind.Unsorted,
                FolderId = folder.Id,
                Name = folder.UnsortedName,
                Count = folder.UnsortedBookCount,
                IsSystem = true,
            });
            foreach (var series in folder.Series)
            {
                folderNode.Children.Add(new LibraryTreeNodeViewModel
                {
                    Kind = LibraryTreeNodeKind.Series,
                    FolderId = folder.Id,
                    SeriesId = series.Id,
                    Name = series.Name,
                    Count = series.BookCount,
                });
            }

            LibraryTree.Add(folderNode);
        }

        SelectedLibraryNode = selectedTarget is { } target
            ? FindTreeNode(target.FolderId, target.SeriesId, target.Kind)
            : null;
    }

    private LibraryTreeNodeViewModel? FindTreeNode(
        Guid folderId,
        Guid? seriesId,
        LibraryTreeNodeKind kind)
    {
        foreach (var folder in LibraryTree)
        {
            if (kind == LibraryTreeNodeKind.Folder && folder.FolderId == folderId)
            {
                return folder;
            }

            var child = folder.Children.FirstOrDefault(node =>
                node.Kind == kind
                && node.FolderId == folderId
                && node.SeriesId == seriesId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private void ClearSelection()
    {
        _selectedBookIds.Clear();
        _selectionAnchorId = null;
        RestoreSelection();
    }

    private void RestoreSelection()
    {
        foreach (var book in _allBooks)
        {
            book.IsSelected = _selectedBookIds.Contains(book.Id);
        }

        SelectedBookCount = _selectedBookIds.Count;
    }

    private void RebuildGroups(IReadOnlyList<LibraryBookItemViewModel> books)
    {
        IEnumerable<LibraryGroupViewModel> groups = Section switch
        {
            LibrarySection.Series => books
                .Where(book => !string.IsNullOrWhiteSpace(book.Details.SeriesName))
                .GroupBy(book => book.Details.SeriesName!, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new LibraryGroupViewModel(
                    group.Key,
                    group.OrderBy(book => book.Details.SeriesIndex is null)
                        .ThenBy(book => book.Details.SeriesIndex)
                        .ThenBy(book => book.Title)
                        .ToArray())),
            LibrarySection.Tags => books
                .SelectMany(book => book.Details.Tags.Select(tag => (Tag: tag, Book: book)))
                .GroupBy(item => item.Tag, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new LibraryGroupViewModel(
                    group.Key,
                    group.Select(item => item.Book).OrderBy(book => book.Title).ToArray())),
            _ => [],
        };

        Groups.Clear();
        foreach (var group in groups.OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Groups.Add(group);
        }
    }

    private async Task UpdateBookAsync(
        LibraryBookItemViewModel item,
        UpdateBookRequest request,
        string? targetCoverPath,
        string? copiedCoverPath,
        string successMessage)
    {
        try
        {
            var updated = await _libraryService.UpdateBookAsync(
                item.Id,
                request,
                _lifetimeCancellationSource.Token);
            if (updated is null)
            {
                throw new InvalidOperationException("书籍记录已不存在。");
            }

            if (!string.Equals(
                    item.Details.CoverPath,
                    targetCoverPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _coverFileStore.DeleteManagedCopy(item.Details.CoverPath);
            }

            await ReloadAsync(_lifetimeCancellationSource.Token);
            StatusMessage = successMessage;
        }
        catch (Exception exception)
        {
            if (!string.IsNullOrWhiteSpace(copiedCoverPath))
            {
                _coverFileStore.DeleteManagedCopy(copiedCoverPath);
            }

            ReportError("无法更新书籍", exception);
        }
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        IsBusy = true;
        try
        {
            await operation(_lifetimeCancellationSource.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellationSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportError("操作失败", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReportError(string title, Exception exception)
    {
        StatusMessage = $"{title}：{exception.Message}";
        _interactionService.ShowMessage(title, exception.Message, isError: true);
    }

    private static bool Matches(BookDetails book, string query)
    {
        return Contains(book.Title, query)
            || Contains(book.AuthorName, query)
            || Contains(book.SeriesName, query)
            || book.Tags.Any(tag => Contains(tag, query));
    }

    private static bool Contains(string? value, string query)
    {
        return value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    private static UpdateBookRequest CreateUpdateRequest(
        BookDetails book,
        string? coverPath = null,
        bool? isFavorite = null)
    {
        return new UpdateBookRequest
        {
            Title = book.Title,
            Description = book.Description,
            CoverPath = coverPath ?? book.CoverPath,
            AuthorName = book.AuthorName,
            SeriesName = book.SeriesName,
            SeriesIndex = book.SeriesIndex,
            Tags = book.Tags,
            IsFavorite = isFavorite ?? book.IsFavorite,
        };
    }
}
