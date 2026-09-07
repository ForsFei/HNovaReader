using HNovaReader.Data.Entities;
using HNovaReader.Features.Shared;
using HNovaReader.Features.Shell;
using HNovaReader.Infrastructure.Navigation;
using HNovaReader.Services.Import;
using HNovaReader.Services.Library;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Features;

public sealed class ShellViewModelTests
{
    [Fact]
    public async Task ImportPathsAsync_ForwardsPathsAndShowsSummary()
    {
        var paths = new[] { @"C:\Books\one.txt", @"C:\Books\two.txt" };
        var importService = new StubTextImportService(
            new TextImportResult(
            [
                CreateResult(paths[0], TextImportItemStatus.Imported),
                CreateResult(paths[1], TextImportItemStatus.SkippedDuplicate),
                CreateResult(@"C:\Books\broken.txt", TextImportItemStatus.Failed),
            ]));
        using var libraryViewModel = LibraryViewModelTestFactory.Create();
        using var viewModel = new ShellViewModel(
            new StubNavigationService(),
            importService,
            libraryViewModel,
            new StubLibraryInteractionService(),
            new StubAppSettingsService());

        await viewModel.ImportPathsAsync(paths);

        Assert.NotNull(importService.Request);
        Assert.Same(paths, importService.Request.Paths);
        Assert.Equal(BookStorageMode.ManagedCopy, importService.Request.StorageMode);
        Assert.False(viewModel.IsImporting);
        Assert.Equal("导入完成：1 本，跳过 1 本，失败 1 本", viewModel.ImportStatus);
    }

    [Fact]
    public async Task CancelImportCommand_CancelsRunningImport()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var importService = new BlockingTextImportService();
        using var libraryViewModel = LibraryViewModelTestFactory.Create();
        using var viewModel = new ShellViewModel(
            new StubNavigationService(),
            importService,
            libraryViewModel,
            new StubLibraryInteractionService(),
            new StubAppSettingsService());

        var importTask = viewModel.ImportPathsAsync([@"C:\Books\long.txt"]);
        await importService.Started.Task.WaitAsync(cancellationToken);

        Assert.True(viewModel.IsImporting);
        Assert.True(viewModel.CancelImportCommand.CanExecute(null));

        viewModel.CancelImportCommand.Execute(null);
        await importTask.WaitAsync(cancellationToken);

        Assert.False(viewModel.IsImporting);
        Assert.False(viewModel.CancelImportCommand.CanExecute(null));
        Assert.Equal("导入已取消", viewModel.ImportStatus);
    }

    [Fact]
    public async Task ImportTextFilesCommand_UsesPickerAndForwardsSelection()
    {
        var paths = new[] { @"C:\Books\picked-one.txt", @"C:\Books\picked-two.txt" };
        var interaction = new StubLibraryInteractionService { TextFiles = paths };
        var importService = new StubTextImportService(new TextImportResult([]));
        using var libraryViewModel = LibraryViewModelTestFactory.Create();
        using var viewModel = new ShellViewModel(
            new StubNavigationService(),
            importService,
            libraryViewModel,
            interaction,
            new StubAppSettingsService());

        await viewModel.ImportTextFilesCommand.ExecuteAsync(null);

        Assert.NotNull(importService.Request);
        Assert.Same(paths, importService.Request.Paths);
        Assert.Equal("导入完成：0 本，跳过 0 本，失败 0 本", viewModel.ImportStatus);
    }

    private static TextImportItemResult CreateResult(string path, TextImportItemStatus status)
    {
        return new TextImportItemResult(path, null, status, null, null, null);
    }

    private sealed class StubTextImportService(TextImportResult result) : ITextImportService
    {
        public TextImportRequest? Request { get; private set; }

        public Task<TextImportResult> ImportAsync(
            TextImportRequest request,
            IProgress<TextImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingTextImportService : ITextImportService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TextImportResult> ImportAsync(
            TextImportRequest request,
            IProgress<TextImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new TextImportResult([]);
        }
    }

    private sealed class StubNavigationService : INavigationService
    {
        public PageViewModel? CurrentPage => null;

        public bool CanGoBack => false;

        public event EventHandler? CurrentPageChanged
        {
            add { }
            remove { }
        }

        public Task NavigateAsync(NavigationRoute route, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task GoBackAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task OpenReaderAsync(BookDetails book, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
