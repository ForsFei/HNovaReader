using HNovaReader.Features.Library;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Features;

public sealed class BookEditorViewModelTests
{
    [Fact]
    public void TryCreateResult_ParsesMetadataAndSelectedCover()
    {
        var book = TestBookFactory.Create(
            title: "旧书名",
            author: "旧作者",
            series: "旧系列",
            tags: ["旧标签"]);
        var viewModel = new BookEditorViewModel(book, () => @"C:\covers\new.png")
        {
            Title = " 新书名 ",
            AuthorName = " 新作者 ",
            Description = " 简介 ",
            SeriesName = " 新系列 ",
            SeriesIndexText = "2.5",
            TagsText = "科幻，经典, 科幻；长篇",
            IsFavorite = true,
        };

        viewModel.ChooseCoverCommand.Execute(null);
        var success = viewModel.TryCreateResult(out var result, out var error);

        Assert.True(success, error);
        Assert.NotNull(result);
        Assert.Equal("新书名", result.Update.Title);
        Assert.Equal("新作者", result.Update.AuthorName);
        Assert.Equal("简介", result.Update.Description);
        Assert.Equal("新系列", result.Update.SeriesName);
        Assert.Equal(2.5, result.Update.SeriesIndex);
        Assert.Equal(["科幻", "经典", "长篇"], result.Update.Tags);
        Assert.True(result.Update.IsFavorite);
        Assert.True(result.IsCoverChanged);
        Assert.Equal(@"C:\covers\new.png", result.SelectedCoverPath);
        Assert.Equal(@"C:\covers\new.png", viewModel.CoverPreviewPath);
    }

    [Fact]
    public void TryCreateResult_RejectsInvalidSeriesIndex()
    {
        var viewModel = new BookEditorViewModel(TestBookFactory.Create(), () => null)
        {
            SeriesName = "系列",
            SeriesIndexText = "第二本",
        };

        var success = viewModel.TryCreateResult(out var result, out var error);

        Assert.False(success);
        Assert.Null(result);
        Assert.Contains("必须是数字", error);
    }

    [Fact]
    public void RemoveCover_UsesDynamicDefaultCover()
    {
        var viewModel = new BookEditorViewModel(
            TestBookFactory.Create(coverPath: @"C:\covers\old.jpg"),
            () => null);

        viewModel.RemoveCoverCommand.Execute(null);
        Assert.True(viewModel.TryCreateResult(out var result, out var error), error);

        Assert.NotNull(result);
        Assert.True(result.IsCoverChanged);
        Assert.Null(result.SelectedCoverPath);
        Assert.Equal("使用动态默认封面", viewModel.CoverDisplayName);
        Assert.Null(viewModel.CoverPreviewPath);
    }
}
