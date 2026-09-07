using HNovaReader.Data;
using HNovaReader.Data.Entities;
using Microsoft.EntityFrameworkCore;
using DataChapter = HNovaReader.Data.Entities.Chapter;
using DataTextCheckpoint = HNovaReader.Data.Entities.TextCheckpoint;

namespace HNovaReader.Services.Reader;

public sealed class EfTextIndexStore(
    IDbContextFactory<HNovaReaderDbContext> dbContextFactory,
    TimeProvider timeProvider) : ITextIndexStore
{
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public async Task<TextIndexSnapshot?> TryLoadAsync(
        Guid bookId,
        ReaderFileVersion fileVersion,
        string encodingName,
        int patternVersion,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var index = await dbContext.BookTextIndexes
            .AsNoTracking()
            .Include(item => item.Checkpoints)
            .Include(item => item.Chapters)
            .SingleOrDefaultAsync(item => item.BookId == bookId, cancellationToken);

        if (index is null
            || !index.IsComplete
            || index.FileSize != fileVersion.FileSize
            || EnsureUtc(index.LastWriteTimeUtc) != fileVersion.LastWriteTimeUtc
            || !string.Equals(index.EncodingName, encodingName, StringComparison.OrdinalIgnoreCase)
            || index.PatternVersion != patternVersion)
        {
            return null;
        }

        return new TextIndexSnapshot(
            fileVersion,
            index.EncodingName,
            index.PatternVersion,
            index.TotalCharacterCount,
            index.IsComplete,
            index.Checkpoints
                .OrderBy(checkpoint => checkpoint.CharacterOffset)
                .Select(checkpoint => new TextIndexCheckpoint(
                    checkpoint.CharacterOffset,
                    checkpoint.ByteOffset))
                .ToArray(),
            index.Chapters
                .OrderBy(chapter => chapter.Ordinal)
                .Select(chapter => new ChapterInfo(
                    chapter.Title,
                    chapter.Level,
                    chapter.StartOffset,
                    chapter.EndOffset))
                .ToArray());
    }

    public async Task SaveAsync(
        Guid bookId,
        TextIndexSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsComplete)
        {
            throw new ArgumentException("只能持久化完整文本索引。", nameof(snapshot));
        }

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var existing = await dbContext.BookTextIndexes
                .SingleOrDefaultAsync(index => index.BookId == bookId, cancellationToken);
            if (existing is not null)
            {
                dbContext.BookTextIndexes.Remove(existing);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var indexId = Guid.NewGuid();
            var entity = new BookTextIndex
            {
                Id = indexId,
                BookId = bookId,
                EncodingName = snapshot.EncodingName,
                FileSize = snapshot.FileVersion.FileSize,
                LastWriteTimeUtc = snapshot.FileVersion.LastWriteTimeUtc,
                PatternVersion = snapshot.PatternVersion,
                TotalCharacterCount = snapshot.TotalCharacterCount,
                IsComplete = true,
                BuiltAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            };

            foreach (var checkpoint in snapshot.Checkpoints)
            {
                entity.Checkpoints.Add(new DataTextCheckpoint
                {
                    BookTextIndexId = indexId,
                    CharacterOffset = checkpoint.CharacterOffset,
                    ByteOffset = checkpoint.ByteOffset,
                });
            }

            for (var ordinal = 0; ordinal < snapshot.Chapters.Count; ordinal++)
            {
                var chapter = snapshot.Chapters[ordinal];
                entity.Chapters.Add(new DataChapter
                {
                    BookTextIndexId = indexId,
                    Ordinal = ordinal,
                    Title = chapter.Title,
                    Level = chapter.Level,
                    StartOffset = chapter.StartOffset,
                    EndOffset = chapter.EndOffset,
                });
            }

            dbContext.BookTextIndexes.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
