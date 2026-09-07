namespace HNovaReader.Services.Library;

public sealed record RelocateBookFileRequest
{
    public required string FilePath { get; init; }

    public required long FileSize { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }

    public required string FileEncoding { get; init; }
}
