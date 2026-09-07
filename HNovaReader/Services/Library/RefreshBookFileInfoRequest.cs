namespace HNovaReader.Services.Library;

public sealed record RefreshBookFileInfoRequest
{
    public long FileSize { get; init; }

    public required DateTime LastWriteTimeUtc { get; init; }

    public required string FileEncoding { get; init; }
}
