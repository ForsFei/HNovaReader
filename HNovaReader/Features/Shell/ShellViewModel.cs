using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HNovaReader.Features.Library;
using HNovaReader.Features.Reader;
using HNovaReader.Features.Shared;
using HNovaReader.Infrastructure.Navigation;
using HNovaReader.Services.Import;
using HNovaReader.Services.Settings;

namespace HNovaReader.Features.Shell;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly ITextImportService _textImportService;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly ILibraryInteractionService _libraryInteractionService;
    private readonly IAppSettingsService _settingsService;
    private CancellationTokenSource? _importCancellationSource;

    public ShellViewModel(
        INavigationService navigationService,
        ITextImportService textImportService,
        LibraryViewModel libraryViewModel,
        ILibraryInteractionService libraryInteractionService,
        IAppSettingsService settingsService)
    {
        _navigationService = navigationService;
        _textImportService = textImportService;
        _libraryViewModel = libraryViewModel;
        _libraryInteractionService = libraryInteractionService;
        _settingsService = settingsService;
        currentPage = _navigationService.CurrentPage;
        canGoBack = _navigationService.CanGoBack;
        importStatus = "可将 TXT 文件或文件夹拖入窗口";
        _navigationService.CurrentPageChanged += OnCurrentPageChanged;
        _libraryViewModel.BookOpenRequested += OnBookOpenRequestedAsync;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReaderPage))]
    private PageViewModel? currentPage;

    [ObservableProperty]
    private bool canGoBack;

    [ObservableProperty]
    private bool isImporting;

    [ObservableProperty]
    private string importStatus;

    public bool IsReaderPage => CurrentPage is ReaderViewModel;

    public LibraryViewModel Library => _libraryViewModel;

    [RelayCommand]
    private Task NavigateAsync(NavigationRoute route)
    {
        return _navigationService.NavigateAsync(route, CancellationToken.None);
    }

    [RelayCommand]
    private Task GoBackAsync()
    {
        return _navigationService.GoBackAsync(CancellationToken.None);
    }

    public async Task ImportPathsAsync(
        IReadOnlyCollection<string> paths,
        LibraryTreeNodeViewModel? target = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0 || IsImporting)
        {
            return;
        }

        _importCancellationSource = new CancellationTokenSource();
        IsImporting = true;
        ImportStatus = "正在扫描 TXT 文件…";

        try
        {
            var progress = new Progress<TextImportProgress>(OnImportProgress);
            var result = await _textImportService.ImportAsync(
                new TextImportRequest
                {
                    Paths = paths,
                    StorageMode = _settingsService.Current.ImportBehavior,
                    ManagedLibraryDirectory = _settingsService.Current.LibraryFolder,
                    FallbackEncoding = _settingsService.Current.DefaultTextEncoding,
                    TargetFolderId = target?.FolderId,
                    TargetSeriesId = target?.Kind == LibraryTreeNodeKind.Series ? target.SeriesId : null,
                },
                progress,
                _importCancellationSource.Token);
            if (result.ImportedCount > 0)
            {
                await _libraryViewModel.ReloadAsync(_importCancellationSource.Token);
            }

            ImportStatus = $"导入完成：{result.ImportedCount} 本，跳过 {result.SkippedCount} 本，失败 {result.FailedCount} 本";
        }
        catch (OperationCanceledException) when (_importCancellationSource.IsCancellationRequested)
        {
            ImportStatus = "导入已取消";
        }
        catch (Exception exception)
        {
            ImportStatus = $"导入失败：{exception.Message}";
        }
        finally
        {
            IsImporting = false;
            _importCancellationSource.Dispose();
            _importCancellationSource = null;
        }
    }

    public async Task SelectLibraryNodeAsync(LibraryTreeNodeViewModel node)
    {
        await _navigationService.NavigateAsync(NavigationRoute.Library, CancellationToken.None);
        _libraryViewModel.SelectLibraryNode(node);
    }

    public Task MoveSelectedBooksAsync(LibraryTreeNodeViewModel target)
    {
        return _libraryViewModel.MoveSelectedBooksAsync(target);
    }

    public Task MoveSeriesAsync(Guid seriesId, LibraryTreeNodeViewModel target)
    {
        return _libraryViewModel.MoveSeriesAsync(seriesId, target);
    }

    [RelayCommand(CanExecute = nameof(IsImporting))]
    private void CancelImport()
    {
        _importCancellationSource?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanImportTextFiles))]
    private async Task ImportTextFilesAsync()
    {
        var paths = _libraryInteractionService.PickTextFiles();
        if (paths.Count > 0)
        {
            await ImportPathsAsync(paths);
        }
    }

    public void Dispose()
    {
        _importCancellationSource?.Cancel();
        _importCancellationSource?.Dispose();
        _navigationService.CurrentPageChanged -= OnCurrentPageChanged;
        _libraryViewModel.BookOpenRequested -= OnBookOpenRequestedAsync;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        return _navigationService.ShutdownAsync(cancellationToken);
    }

    partial void OnIsImportingChanged(bool value)
    {
        CancelImportCommand.NotifyCanExecuteChanged();
        ImportTextFilesCommand.NotifyCanExecuteChanged();
    }

    private bool CanImportTextFiles()
    {
        return !IsImporting;
    }

    private void OnImportProgress(TextImportProgress progress)
    {
        ImportStatus = progress.Stage switch
        {
            TextImportStage.Scanning => $"正在扫描…已找到 {progress.DiscoveredCount} 本",
            TextImportStage.CheckingDuplicate => $"正在检查重复：{GetFileName(progress.CurrentPath)}",
            TextImportStage.DetectingEncoding => $"正在检测编码：{GetFileName(progress.CurrentPath)}",
            TextImportStage.Copying => $"正在复制：{GetFileName(progress.CurrentPath)}",
            TextImportStage.Saving => $"正在导入 {progress.ProcessedCount}/{progress.TotalCount ?? progress.DiscoveredCount}",
            TextImportStage.Completed => "正在完成导入…",
            _ => ImportStatus,
        };
    }

    private static string GetFileName(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFileName(path);
    }

    private void OnCurrentPageChanged(object? sender, EventArgs e)
    {
        CurrentPage = _navigationService.CurrentPage;
        CanGoBack = _navigationService.CanGoBack;
    }

    private async Task OnBookOpenRequestedAsync(
        Services.Library.BookDetails book,
        CancellationToken cancellationToken)
    {
        try
        {
            await _navigationService.OpenReaderAsync(book, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _libraryInteractionService.ShowMessage(
                "无法打开书籍",
                exception.Message,
                isError: true);
        }
    }
}
