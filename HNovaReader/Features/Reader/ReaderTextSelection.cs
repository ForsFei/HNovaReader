namespace HNovaReader.Features.Reader;

public sealed record ReaderTextSelection(
    long StartOffset,
    int Length,
    string SelectedText);
