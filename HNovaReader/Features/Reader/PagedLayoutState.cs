namespace HNovaReader.Features.Reader;

public enum ReaderLayoutMode
{
    Scroll,
    Paged,
}

public sealed record PagedLayoutSignature(
    string ContentVersion,
    long ContentStartCharacterOffset,
    int ContentLength,
    double ViewportWidth,
    double ViewportHeight,
    double DpiScaleX,
    double DpiScaleY,
    string FontFamily,
    double FontSize,
    int FontWeight,
    double LineHeight,
    double ParagraphSpacing,
    double BodyWidth,
    double PageMarginLeft,
    double PageMarginTop,
    double PageMarginRight,
    double PageMarginBottom);

public sealed class PagedLayoutState
{
    private readonly Dictionary<int, long> pageBoundaries = [];

    public PagedLayoutSignature? Signature { get; private set; }

    public long ReflowAnchor { get; private set; }

    public int? CurrentPageNumber { get; private set; }

    public IReadOnlyDictionary<int, long> PageBoundaries => pageBoundaries;

    public bool EnsureLayout(PagedLayoutSignature signature, long reflowAnchor)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ReflowAnchor = reflowAnchor;
        if (signature == Signature)
        {
            return false;
        }

        Signature = signature;
        CurrentPageNumber = null;
        pageBoundaries.Clear();
        return true;
    }

    public void SelectPage(int pageNumber, long characterOffset)
    {
        CachePageBoundary(pageNumber, characterOffset);
        CurrentPageNumber = pageNumber;
        ReflowAnchor = characterOffset;
    }

    public void CachePageBoundary(int pageNumber, long characterOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(characterOffset);
        pageBoundaries[pageNumber] = characterOffset;
    }

    public bool TryGetPageBoundary(int pageNumber, out long characterOffset)
    {
        return pageBoundaries.TryGetValue(pageNumber, out characterOffset);
    }

    public void Reset(long reflowAnchor)
    {
        Signature = null;
        ReflowAnchor = reflowAnchor;
        CurrentPageNumber = null;
        pageBoundaries.Clear();
    }
}
