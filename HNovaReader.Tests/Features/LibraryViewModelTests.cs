using HNovaReader.Features.Library;
using HNovaReader.Services.Library;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Features;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task EditSeries_OnlyUpdatesMetadataWithoutMovingFiles()
    {
        var library = new StubLibraryService();
        var book = TestBookFactory.Create(series: "旧标记");
        library.Books.Add(book);
        var org = new StubLibraryOrganizationService(library);
        var interaction = new StubLibraryInteractionService
        {
            EditorResult = new BookEditorResult(new UpdateBookRequest
            {
                Title = book.Title,
                SeriesName = "新标记",
                Tags = ["测试标签"],
            }, false, null),
        };
        using var model = LibraryViewModelTestFactory.Create(libraryService: library,
            organizationService: org, interactionService: interaction);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.Books[0].EditCommand.ExecuteAsync(null);
        Assert.Null(org.LastMoveRequest);
        Assert.Equal(book.FilePath, Assert.Single(library.Books).FilePath);
        Assert.Equal("新标记", library.LastUpdateRequest!.SeriesName);
        Assert.Equal(["测试标签"], library.LastUpdateRequest.Tags);
    }

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
    public async Task NestedDirectorySelectionIncludesDescendantsAndCtrlMovePreservesSeries()
    {
        var library = new StubLibraryService();
        var org = new StubLibraryOrganizationService(library);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        org.Folders.Add(new(a, "A", 1, false, "", 0, 1, [], null, "A"));
        org.Folders.Add(new(b, "B", 2, false, "", 0, 1, [], a, "A\\B"));
        org.Folders.Add(new(c, "C", 3, false, "", 0, 1, [], b, "A\\B\\C"));
        var series = Guid.NewGuid();
        library.Books.Add(TestBookFactory.Create(title: "父目录") with { FolderId = a, SeriesId = series });
        library.Books.Add(TestBookFactory.Create(title: "深层目录") with { FolderId = c, SeriesId = series });
        using var model = LibraryViewModelTestFactory.Create(libraryService: library, organizationService: org);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var parent = model.LibraryTree.Single(node => node.FolderId == a);
        Assert.Equal(3, parent.Count);
        Assert.Equal("C", parent.Children.Single().Children.Single().Name);
        Assert.Empty(parent.Children.Single().Children.Single().Children);
        model.SelectLibraryNode(parent);
        Assert.Equal(2, model.Books.Count);
        model.SelectBook(model.Books[0], false, false);
        model.SelectBook(model.Books[1], true, false);
        Assert.Equal(2, model.SelectedBookCount);
        await model.MoveSelectedBooksAsync(model.LibraryTree.Single(node => node.FolderId == LibraryOrganizationDefaults.InboxFolderId));
        Assert.Equal(2, org.LastMoveRequest!.BookIds.Count);
        Assert.Null(org.LastMoveRequest.SeriesId);
        Assert.All(library.Books, book => Assert.Equal(series, book.SeriesId));
    }

    [Fact]
    public async Task RenameCommand_RenamesRealFolderAndRootCannotRename()
    {
        var library = new StubLibraryService();
        var org = new StubLibraryOrganizationService(library);
        var interaction = new StubLibraryInteractionService { PromptNameResult = "导入箱" };
        using var model = LibraryViewModelTestFactory.Create(libraryService: library, organizationService: org,
            interactionService: interaction);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.SelectLibraryNode(model.LibraryTree.Single());
        await model.RenameLibraryNodeCommand.ExecuteAsync(null);
        Assert.Equal("导入箱", model.SelectedLibraryNode!.Name);
        Assert.False(new LibraryTreeNodeViewModel
        {
            Kind = LibraryTreeNodeKind.Folder,
            FolderId = LibraryOrganizationDefaults.RootFolderId,
            Name = "根",
            Count = 0,
            IsSystem = true,
        }.CanRename);
    }

    [Fact]
    public async Task MoveFolder_UsesFolderIdentityAndParentTarget()
    {
        var library = new StubLibraryService();
        var org = new StubLibraryOrganizationService(library);
        var folder = await org.CreateFolderAsync("子目录", TestContext.Current.CancellationToken);
        using var model = LibraryViewModelTestFactory.Create(libraryService: library, organizationService: org);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var target = model.LibraryTree.Single(node => node.FolderId == LibraryOrganizationDefaults.InboxFolderId);
        await model.MoveFolderAsync(folder.Id, target);
        Assert.Equal((folder.Id, target.FolderId), org.LastFolderMove);
        Assert.Equal(folder.Id, model.SelectedLibraryNode!.FolderId);
        Assert.Equal(folder.Id, model.LibraryTree.Single(node => node.FolderId == target.FolderId).Children.Single().FolderId);
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
