using HNovaReader.Features.Reader;

namespace HNovaReader.Tests.Features;

public sealed class PagedLayoutStateTests
{
    [Fact]
    public void EnsureLayout_WhenMetricsChange_InvalidatesCachedPageBoundaries()
    {
        var state = new PagedLayoutState();
        var original = CreateSignature(viewportWidth: 900, fontSize: 18);
        Assert.True(state.EnsureLayout(original, 1200));
        state.SelectPage(2, 1800);

        Assert.False(state.EnsureLayout(original, 1800));
        Assert.True(state.TryGetPageBoundary(2, out var cachedOffset));
        Assert.Equal(1800, cachedOffset);

        var resized = CreateSignature(viewportWidth: 760, fontSize: 18);
        Assert.True(state.EnsureLayout(resized, state.ReflowAnchor));
        Assert.Null(state.CurrentPageNumber);
        Assert.Empty(state.PageBoundaries);
        Assert.Equal(1800, state.ReflowAnchor);
    }

    [Fact]
    public void EnsureLayout_WhenTypographyChanges_PreservesAnchorAndInvalidatesPages()
    {
        var state = new PagedLayoutState();
        state.EnsureLayout(CreateSignature(900, 18), 400);
        state.SelectPage(0, 400);

        var changed = state.EnsureLayout(CreateSignature(900, 20), state.ReflowAnchor);

        Assert.True(changed);
        Assert.Equal(400, state.ReflowAnchor);
        Assert.Empty(state.PageBoundaries);
    }

    private static PagedLayoutSignature CreateSignature(
        double viewportWidth,
        double fontSize)
    {
        return new PagedLayoutSignature(
            "content-v1",
            400,
            20_000,
            viewportWidth,
            720,
            1,
            1,
            "Microsoft YaHei UI",
            fontSize,
            400,
            fontSize * 1.75,
            0,
            Math.Min(viewportWidth, 960),
            64,
            42,
            64,
            72);
    }
}
