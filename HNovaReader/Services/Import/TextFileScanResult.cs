namespace HNovaReader.Services.Import;

public sealed record TextFileScanResult(
    IReadOnlyList<string> Files,
    IReadOnlyList<FileScanError> Errors);
