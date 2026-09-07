using HNovaReader.Features.Library;
using HNovaReader.Services.Library;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Features;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task Search_FiltersTitleAuthorSeriesAndTagsAfterDebounce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        library.Books.AddRange(
        [
            TestBookFactory.Create(
                title: "三体",
                author: "刘慈欣",
                series: "地球往事",
                tags: ["科幻", "经典"]),
            TestBookFactory.Create(
                title: "银河帝国",
                author: "阿西莫夫",
                series: "基地",
                tags: ["太空歌剧"]),
        ]);
        using var viewModel = LibraryViewModelTestFactory.Create(libraryService: library);
        await viewModel.InitializeAsync(cancellationToken);

        await AssertSearchAsync(viewModel, "三体", "三体", cancellationToken);
        await AssertSearchAsync(viewModel, "阿西莫夫", "银河帝国", cancellationToken);
        await AssertSearchAsync(viewModel, "地球往事", "三体", cancellationToken);
        await AssertSearchAsync(viewModel, "太空歌剧", "银河帝国", cancellationToken);
    }

    [Fact]
    public async Task Initialize_WithFiveThousandBooks_BuildsBoundedRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        library.Books.AddRange(Enumerable.Range(1, 5001)
            .Select(index => TestBookFactory.Create(title: $"Book {index:0000}")));
        using var viewModel = LibraryViewModelTestFactory.Create(libraryService: library);

        await viewModel.InitializeAsync(cancellationToken);

        Assert.Equal(5001, viewModel.Books.Count);
        Assert.Equal(1251, viewModel.BookRows.Count);
        Assert.All(viewModel.BookRows, row => Assert.InRange(row.Books.Count, 1, 4));
    }

    [Fact]
    public async Task ToggleFavorite_UpdatesServiceAndReloadsBook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        library.Books.Add(TestBookFactory.Create(title: "收藏测试"));
        using var viewModel = LibraryViewModelTestFactory.Create(libraryService: library);
        await viewModel.InitializeAsync(cancellationToken);

        await viewModel.Books[0].ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.True(library.LastUpdateRequest?.IsFavorite);
        Assert.True(viewModel.Books[0].Details.IsFavorite);
        Assert.Equal("已加入收藏", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Remove_WithConfirmation_RemovesRecordButDoesNotRecycleFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var book = TestBookFactory.Create(title: "只移除记录", coverPath: @"C:\covers\old.jpg");
        library.Books.Add(book);
        var interaction = new StubLibraryInteractionService { ConfirmResult = true };
        var covers = new StubCoverFileStore();
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            coverFileStore: covers,
            interactionService: interaction);
        await viewModel.InitializeAsync(cancellationToken);

        await viewModel.Books[0].RemoveCommand.ExecuteAsync(null);

        Assert.Equal(book.Id, library.LastRemovedBookId);
        Assert.Null(interaction.RecycledPath);
        Assert.Contains(book.CoverPath!, covers.DeletedPaths);
        Assert.Empty(viewModel.Books);
    }

    [Fact]
    public async Task ChangeCover_CopiesNewCoverAndCleansOldManagedCover()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var book = TestBookFactory.Create(title: "封面测试", coverPath: @"C:\covers\old.jpg");
        library.Books.Add(book);
        var interaction = new StubLibraryInteractionService { CoverImage = @"D:\new.png" };
        var covers = new StubCoverFileStore { CopyDestination = @"C:\covers\new.png" };
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            coverFileStore: covers,
            interactionService: interaction);
        await viewModel.InitializeAsync(cancellationToken);

        await viewModel.Books[0].ChangeCoverCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\covers\new.png", library.LastUpdateRequest?.CoverPath);
        Assert.Contains(@"C:\covers\old.jpg", covers.DeletedPaths);
        Assert.DoesNotContain(@"C:\covers\new.png", covers.DeletedPaths);
    }

    [Fact]
    public async Task DeleteFile_WithConfirmation_RecyclesFileAndRemovesRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var book = TestBookFactory.Create(title: "删除测试", filePath: @"C:\books\delete.txt");
        library.Books.Add(book);
        var interaction = new StubLibraryInteractionService { ConfirmResult = true };
        var organization = new StubLibraryOrganizationService(library);
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            interactionService: interaction,
            organizationService: organization);
        await viewModel.InitializeAsync(cancellationToken);

        await viewModel.Books[0].DeleteFileCommand.ExecuteAsync(null);

        Assert.Equal(book.FilePath, interaction.RecycledPath);
        Assert.Equal(1, organization.BackupCallCount);
        Assert.Equal(book.Id, library.LastRemovedBookId);
        Assert.Empty(viewModel.Books);
    }

    [Fact]
    public async Task TreeSelectionFiltersAndCtrlSelectionMovesAllSelectedBooks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var folderId = Guid.NewGuid();
        var sourceSeriesId = Guid.NewGuid();
        var targetSeriesId = Guid.NewGuid();
        library.Books.AddRange(
        [
            TestBookFactory.Create(title: "第一本") with
            {
                FolderId = folderId,
                FolderName = "小说",
                SeriesId = sourceSeriesId,
                SeriesName = "旧系列",
            },
            TestBookFactory.Create(title: "第二本") with
            {
                FolderId = folderId,
                FolderName = "小说",
                SeriesId = sourceSeriesId,
                SeriesName = "旧系列",
            },
            TestBookFactory.Create(title: "未分类") with
            {
                FolderId = LibraryOrganizationDefaults.InboxFolderId,
                FolderName = LibraryOrganizationDefaults.InboxFolderName,
            },
        ]);
        var organization = new StubLibraryOrganizationService(library);
        organization.Folders.Add(new LibraryFolderDetails(
            folderId,
            "小说",
            1,
            false,
            LibraryOrganizationDefaults.UnsortedName,
            0,
            2,
            [
                new LibrarySeriesDetails(sourceSeriesId, folderId, "旧系列", 2),
                new LibrarySeriesDetails(targetSeriesId, folderId, "新系列", 0),
            ]));
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            organizationService: organization);
        await viewModel.InitializeAsync(cancellationToken);
        var folder = Assert.Single(viewModel.LibraryTree, node => node.FolderId == folderId);
        var source = Assert.Single(folder.Children, node => node.SeriesId == sourceSeriesId);
        var target = Assert.Single(folder.Children, node => node.SeriesId == targetSeriesId);

        viewModel.SelectLibraryNode(source);
        Assert.Equal(["第一本", "第二本"], viewModel.Books.Select(book => book.Title));
        viewModel.SelectBook(viewModel.Books[0], ctrlPressed: false, shiftPressed: false);
        viewModel.SelectBook(viewModel.Books[1], ctrlPressed: true, shiftPressed: false);

        await viewModel.MoveSelectedBooksAsync(target);

        Assert.Equal(2, organization.LastMoveRequest?.BookIds.Count);
        Assert.Equal(targetSeriesId, organization.LastMoveRequest?.SeriesId);
        Assert.Equal(0, viewModel.SelectedBookCount);
        Assert.All(library.Books.Take(2), book => Assert.Equal(targetSeriesId, book.SeriesId));
    }

    [Fact]
    public async Task RenameCommand_AllowsInboxAndSeriesNodes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var organization = new StubLibraryOrganizationService(library);
        var customFolder = await organization.CreateFolderAsync("小说", cancellationToken);
        var series = await organization.CreateSeriesAsync(customFolder.Id, "旧系列", cancellationToken);
        var interaction = new StubLibraryInteractionService { PromptNameResult = "导入箱" };
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            interactionService: interaction,
            organizationService: organization);
        await viewModel.InitializeAsync(cancellationToken);

        var inbox = Assert.Single(
            viewModel.LibraryTree,
            node => node.FolderId == LibraryOrganizationDefaults.InboxFolderId);
        viewModel.SelectLibraryNode(inbox);
        await viewModel.RenameLibraryNodeCommand.ExecuteAsync(null);

        Assert.Equal("导入箱", organization.Folders.Single(
            folder => folder.Id == LibraryOrganizationDefaults.InboxFolderId).Name);
        interaction.PromptNameResult = "新系列";
        var seriesNode = Assert.Single(
            viewModel.LibraryTree.SelectMany(folder => folder.Children),
            node => node.SeriesId == series.Id);
        viewModel.SelectLibraryNode(seriesNode);
        await viewModel.RenameLibraryNodeCommand.ExecuteAsync(null);

        Assert.Equal(
            "新系列",
            organization.Folders.Single(folder => folder.Id == customFolder.Id)
                .Series.Single(item => item.Id == series.Id).Name);
    }

    [Fact]
    public async Task RenameCommand_AllowsUnsortedNodeAndRefreshesItsName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var organization = new StubLibraryOrganizationService(library);
        var folder = await organization.CreateFolderAsync("散文", cancellationToken);
        var interaction = new StubLibraryInteractionService { PromptNameResult = "单行本" };
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            interactionService: interaction,
            organizationService: organization);
        await viewModel.InitializeAsync(cancellationToken);
        var unsorted = Assert.Single(
            Assert.Single(viewModel.LibraryTree, node => node.FolderId == folder.Id).Children,
            node => node.Kind == LibraryTreeNodeKind.Unsorted);

        viewModel.SelectLibraryNode(unsorted);
        await viewModel.RenameLibraryNodeCommand.ExecuteAsync(null);

        Assert.Equal("单行本", organization.Folders.Single(item => item.Id == folder.Id).UnsortedName);
        Assert.Equal("单行本", viewModel.SelectedLibraryNode?.Name);
        Assert.True(viewModel.SelectedLibraryNode?.CanRename);
    }

    [Fact]
    public async Task DeleteSeriesCommand_PreservesBookAndSelectsUnsorted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var organization = new StubLibraryOrganizationService(library);
        var folder = await organization.CreateFolderAsync("小说", cancellationToken);
        var series = await organization.CreateSeriesAsync(folder.Id, "旧系列", cancellationToken);
        var book = TestBookFactory.Create(title: "第一卷") with
        {
            FolderId = folder.Id,
            FolderName = folder.Name,
            SeriesId = series.Id,
            SeriesName = series.Name,
        };
        library.Books.Add(book);
        var interaction = new StubLibraryInteractionService { ConfirmResult = true };
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            interactionService: interaction,
            organizationService: organization);
        await viewModel.InitializeAsync(cancellationToken);
        var seriesNode = Assert.Single(
            viewModel.LibraryTree.SelectMany(node => node.Children),
            node => node.SeriesId == series.Id);

        viewModel.SelectLibraryNode(seriesNode);
        await viewModel.DeleteLibraryNodeCommand.ExecuteAsync(null);

        Assert.Equal(series.Id, organization.LastDeletedSeriesId);
        Assert.Null(Assert.Single(library.Books).SeriesId);
        Assert.Equal(LibraryTreeNodeKind.Unsorted, viewModel.SelectedLibraryNode?.Kind);
        Assert.Equal(folder.Id, viewModel.SelectedLibraryNode?.FolderId);
    }

    [Fact]
    public async Task DeleteFolderCommand_PreservesBookAndSelectsInbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var organization = new StubLibraryOrganizationService(library);
        var folder = await organization.CreateFolderAsync("临时", cancellationToken);
        library.Books.Add(TestBookFactory.Create(title: "草稿") with
        {
            FolderId = folder.Id,
            FolderName = folder.Name,
        });
        var interaction = new StubLibraryInteractionService { ConfirmResult = true };
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            interactionService: interaction,
            organizationService: organization);
        await viewModel.InitializeAsync(cancellationToken);
        var folderNode = Assert.Single(viewModel.LibraryTree, node => node.FolderId == folder.Id);

        viewModel.SelectLibraryNode(folderNode);
        await viewModel.DeleteLibraryNodeCommand.ExecuteAsync(null);

        Assert.Equal(folder.Id, organization.LastDeletedFolderId);
        Assert.Equal(LibraryOrganizationDefaults.InboxFolderId, Assert.Single(library.Books).FolderId);
        Assert.Equal(LibraryOrganizationDefaults.InboxFolderId, viewModel.SelectedLibraryNode?.FolderId);
        Assert.Equal(LibraryTreeNodeKind.Folder, viewModel.SelectedLibraryNode?.Kind);
    }

    [Fact]
    public async Task MoveSeries_UsesSeriesIdentityAndFolderTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        var organization = new StubLibraryOrganizationService(library);
        var source = await organization.CreateFolderAsync("来源", cancellationToken);
        var target = await organization.CreateFolderAsync("目标", cancellationToken);
        var series = await organization.CreateSeriesAsync(source.Id, "系列", cancellationToken);
        using var viewModel = LibraryViewModelTestFactory.Create(
            libraryService: library,
            organizationService: organization);
        await viewModel.InitializeAsync(cancellationToken);
        var targetNode = Assert.Single(viewModel.LibraryTree, node => node.FolderId == target.Id);

        await viewModel.MoveSeriesAsync(series.Id, targetNode);

        Assert.Equal((series.Id, target.Id), organization.LastSeriesMove);
        Assert.Equal(series.Id, viewModel.SelectedLibraryNode?.SeriesId);
        Assert.Equal(target.Id, viewModel.SelectedLibraryNode?.FolderId);
    }

    [Fact]
    public async Task OpenBook_RaisesReaderRequestWithSelectedBook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(Path.GetTempPath(), $"hnova-open-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(filePath, "test", cancellationToken);
        var library = new StubLibraryService();
        var book = TestBookFactory.Create(title: "进入阅读", filePath: filePath);
        library.Books.Add(book);
        try
        {
            using var viewModel = LibraryViewModelTestFactory.Create(libraryService: library);
            await viewModel.InitializeAsync(cancellationToken);
            HNovaReader.Services.Library.BookDetails? requestedBook = null;
            viewModel.BookOpenRequested += (details, _) =>
            {
                requestedBook = details;
                return Task.CompletedTask;
            };

            await viewModel.Books[0].OpenCommand.ExecuteAsync(null);

            Assert.Same(book, requestedBook);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task Sections_ClassifyProgressAndSelectLatestContinueReadingBook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        library.Books.AddRange(
        [
            TestBookFactory.Create(title: "未读"),
            TestBookFactory.Create(
                title: "阅读中",
                lastOpenedAtUtc: new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc),
                readingCharacterOffset: 400,
                readingProgress: 0.4),
            TestBookFactory.Create(
                title: "已读",
                lastOpenedAtUtc: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
                readingCharacterOffset: 950,
                readingProgress: 0.95),
        ]);
        using var viewModel = LibraryViewModelTestFactory.Create(libraryService: library);

        await viewModel.InitializeAsync(cancellationToken);

        Assert.Equal("已读", viewModel.ContinueReadingBook?.Title);
        Assert.True(viewModel.IsContinueReadingVisible);

        viewModel.SetSection(LibrarySection.Unread);
        Assert.Equal("未读", Assert.Single(viewModel.Books).Title);
        viewModel.SetSection(LibrarySection.Reading);
        Assert.Equal("阅读中", Assert.Single(viewModel.Books).Title);
        viewModel.SetSection(LibrarySection.Finished);
        Assert.Equal("已读", Assert.Single(viewModel.Books).Title);
    }

    [Fact]
    public async Task GroupSections_BuildTagMembershipAndSortFractionalSeriesIndexes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new StubLibraryService();
        library.Books.AddRange(
        [
            TestBookFactory.Create(title: "第二卷", series: "星海", seriesIndex: 1.5, tags: ["科幻", "长篇"]),
            TestBookFactory.Create(title: "第一卷", series: "星海", seriesIndex: 0.5, tags: ["科幻"]),
            TestBookFactory.Create(title: "外传", series: "星海", seriesIndex: null, tags: ["短篇"]),
        ]);
        using var viewModel = LibraryViewModelTestFactory.Create(libraryService: library);
        await viewModel.InitializeAsync(cancellationToken);

        viewModel.SetSection(LibrarySection.Series);
        var series = Assert.Single(viewModel.Groups);
        viewModel.OpenGroupCommand.Execute(series);
        Assert.Equal(["第一卷", "第二卷", "外传"], viewModel.Books.Select(book => book.Title));

        viewModel.SetSection(LibrarySection.Tags);
        var scienceFiction = Assert.Single(viewModel.Groups, group => group.Name == "科幻");
        Assert.Equal(2, scienceFiction.Count);
        viewModel.OpenGroupCommand.Execute(scienceFiction);
        Assert.Equal(
            ["第一卷", "第二卷"],
            viewModel.Books.Select(book => book.Title).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MissingFile_DoesNotOpenAndCanBeRelocated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var missingPath = Path.Combine(Path.GetTempPath(), $"hnova-missing-{Guid.NewGuid():N}.txt");
        var replacementPath = Path.Combine(Path.GetTempPath(), $"hnova-replacement-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(replacementPath, "replacement", cancellationToken);
        var library = new StubLibraryService();
        library.Books.Add(TestBookFactory.Create(title: "待找回", filePath: missingPath));
        var interaction = new StubLibraryInteractionService { ReplacementTextFile = replacementPath };
        try
        {
            using var viewModel = LibraryViewModelTestFactory.Create(
                libraryService: library,
                interactionService: interaction);
            await viewModel.InitializeAsync(cancellationToken);
            HNovaReader.Services.Library.BookDetails? requestedBook = null;
            viewModel.BookOpenRequested += (details, _) =>
            {
                requestedBook = details;
                return Task.CompletedTask;
            };

            await viewModel.Books[0].OpenCommand.ExecuteAsync(null);

            Assert.Null(requestedBook);
            Assert.Contains(interaction.Messages, message => message.Message.Contains("找不到"));

            await viewModel.Books[0].RelocateFileCommand.ExecuteAsync(null);

            Assert.Equal(replacementPath, library.LastRelocateRequest?.FilePath);
            Assert.Equal(replacementPath, Assert.Single(viewModel.Books).Details.FilePath);
            Assert.False(viewModel.Books[0].IsFileMissing);
        }
        finally
        {
            File.Delete(replacementPath);
        }
    }

    private static async Task AssertSearchAsync(
        LibraryViewModel viewModel,
        string query,
        string expectedTitle,
        CancellationToken cancellationToken)
    {
        viewModel.SearchText = query;
        await viewModel.WaitForPendingSearchAsync().WaitAsync(cancellationToken);
        var book = Assert.Single(viewModel.Books);
        Assert.Equal(expectedTitle, book.Title);
    }
}
