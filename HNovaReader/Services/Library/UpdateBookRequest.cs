namespace HNovaReader.Services.Library;

public sealed record UpdateBookRequest
{
    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? CoverPath { get; init; }

    public string? AuthorName { get; init; }

    public string? SeriesName { get; init; }

    public double? SeriesIndex { get; init; }

    public IReadOnlyCollection<string> Tags { get; init; } = Array.Empty<string>();

    public bool IsFavorite { get; init; }
}
