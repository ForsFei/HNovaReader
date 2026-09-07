using HNovaReader.Data;
using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HNovaReader.Services.Reader;

public sealed class EfReadingPositionService(
    IDbContextFactory<HNovaReaderDbContext> dbContextFactory,
    TimeProvider timeProvider) : IReadingPositionService
{
    public async Task<ReadingPositionSnapshot?> GetAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(bookId, Guid.Empty);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var position = await dbContext.ReadingPositions
            .AsNoTracking()
            .SingleOrDefaultAsync(position => position.BookId == bookId, cancellationToken);
        return position is null
            ? null
            : new ReadingPositionSnapshot(
                position.BookId,
                position.CharacterOffset,
                position.ByteOffsetHint,
                position.Progress,
                position.ChapterTitleSnapshot,
                position.AnchorPrefix,
                position.AnchorSuffix,
                new ReaderFileVersion(position.FileSize, EnsureUtc(position.LastWriteTimeUtc)),
                position.EncodingName,
                EnsureUtc(position.UpdatedAtUtc));
    }

    public async Task SaveAsync(
        SaveReadingPositionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var book = await dbContext.Books
            .Include(candidate => candidate.ReadingPosition)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.BookId, cancellationToken)
            ?? throw new InvalidOperationException("无法保存阅读位置：书籍记录不存在。");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var position = book.ReadingPosition;
        if (position is null)
        {
            position = new ReadingPosition { BookId = request.BookId };
            book.ReadingPosition = position;
        }

        position.CharacterOffset = request.CharacterOffset;
        position.ByteOffsetHint = request.ByteOffsetHint;
        position.Progress = request.Progress;
        position.ChapterTitleSnapshot = Truncate(request.ChapterTitleSnapshot, 240);
        position.AnchorPrefix = Truncate(request.AnchorPrefix, 256);
        position.AnchorSuffix = Truncate(request.AnchorSuffix, 256);
        position.FileSize = request.FileVersion.FileSize;
        position.LastWriteTimeUtc = EnsureUtc(request.FileVersion.LastWriteTimeUtc);
        position.EncodingName = request.EncodingName.Trim();
        position.UpdatedAtUtc = now;
        book.LastOpenedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(SaveReadingPositionRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(request.BookId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(request.CharacterOffset);
        if (request.ByteOffsetHint is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (!double.IsFinite(request.Progress) || request.Progress is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(request.FileVersion.FileSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EncodingName);
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength
            ? trimmed
            : trimmed[..maximumLength];
    }
}
