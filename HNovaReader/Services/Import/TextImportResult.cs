namespace HNovaReader.Services.Import;

public sealed record TextImportResult(IReadOnlyList<TextImportItemResult> Items)
{
    public int ImportedCount => Items.Count(item => item.Status == TextImportItemStatus.Imported);

    public int SkippedCount => Items.Count(item => item.Status == TextImportItemStatus.SkippedDuplicate);

    public int FailedCount => Items.Count(item => item.Status == TextImportItemStatus.Failed);
}
