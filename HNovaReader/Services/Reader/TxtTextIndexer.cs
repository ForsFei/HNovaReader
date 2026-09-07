using System.Text;

namespace HNovaReader.Services.Reader;

internal sealed class TxtTextIndexer(IChapterDetector chapterDetector)
{
    private const long CheckpointIntervalCharacters = 64 * 1024;
    private const long SnapshotIntervalBytes = 4 * 1024 * 1024;

    public async Task<TextIndexSnapshot> BuildAsync(
        string filePath,
        Encoding encoding,
        long contentByteOffset,
        ReaderFileVersion openedFileVersion,
        IProgress<TextIndexProgress>? progress,
        Action<TextIndexSnapshot>? snapshotUpdated,
        CancellationToken cancellationToken)
    {
        var checkpoints = new List<TextIndexCheckpoint>
        {
            new(0, contentByteOffset),
        };
        var chapters = new List<ChapterInfo>();
        var progressChapters = new List<ChapterInfo>();
        var line = new StringBuilder(chapterDetector.MaximumLineLength + 1);
        var lineStartOffset = 0L;
        var characterOffset = 0L;
        var lastCheckpointOffset = 0L;
        var lastSnapshotByteOffset = contentByteOffset;
        var previousWasCarriageReturn = false;
        var lineExceededLimit = false;
        var finalByteOffset = contentByteOffset;

        void CompleteLine()
        {
            if (!lineExceededLimit)
            {
                var match = chapterDetector.Detect(line.ToString(), lineStartOffset);
                if (match is not null)
                {
                    if (chapters.Count > 0)
                    {
                        var completed = chapters[^1] with { EndOffset = match.StartOffset };
                        chapters[^1] = completed;
                        progressChapters.Add(completed);
                    }

                    chapters.Add(new ChapterInfo(
                        match.Title,
                        match.Level,
                        match.StartOffset,
                        match.StartOffset));
                }
            }

            line.Clear();
            lineExceededLimit = false;
        }

        await foreach (var chunk in TextEncodingUtilities.ReadChunksAsync(
            filePath,
            encoding,
            contentByteOffset,
            cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (characterOffset - lastCheckpointOffset >= CheckpointIntervalCharacters)
            {
                checkpoints.Add(new TextIndexCheckpoint(characterOffset, chunk.StartByteOffset));
                lastCheckpointOffset = characterOffset;
            }

            for (var index = 0; index < chunk.Text.Length; index++)
            {
                var current = chunk.Text[index];
                var absoluteOffset = characterOffset + index;
                if (current == '\r')
                {
                    CompleteLine();
                    previousWasCarriageReturn = true;
                    lineStartOffset = absoluteOffset + 1;
                    continue;
                }

                if (current == '\n')
                {
                    if (!previousWasCarriageReturn)
                    {
                        CompleteLine();
                    }

                    previousWasCarriageReturn = false;
                    lineStartOffset = absoluteOffset + 1;
                    continue;
                }

                previousWasCarriageReturn = false;
                if (!lineExceededLimit)
                {
                    line.Append(current);
                    lineExceededLimit = line.Length > chapterDetector.MaximumLineLength;
                }
            }

            characterOffset += chunk.Text.Length;
            finalByteOffset = chunk.EndByteOffset;

            progress?.Report(new TextIndexProgress(
                finalByteOffset,
                openedFileVersion.FileSize,
                characterOffset,
                progressChapters.ToArray()));
            progressChapters.Clear();

            if (finalByteOffset - lastSnapshotByteOffset >= SnapshotIntervalBytes)
            {
                snapshotUpdated?.Invoke(CreateSnapshot(
                    openedFileVersion,
                    encoding.WebName,
                    characterOffset,
                    isComplete: false,
                    checkpoints,
                    chapters));
                lastSnapshotByteOffset = finalByteOffset;
            }
        }

        if (line.Length > 0 || lineExceededLimit)
        {
            CompleteLine();
        }

        if (chapters.Count > 0)
        {
            var completed = chapters[^1] with { EndOffset = characterOffset };
            chapters[^1] = completed;
            progressChapters.Add(completed);
        }

        if (checkpoints[^1].CharacterOffset != characterOffset)
        {
            checkpoints.Add(new TextIndexCheckpoint(characterOffset, finalByteOffset));
        }

        var currentFileVersion = ReaderFileVersion.FromPath(filePath);
        if (currentFileVersion != openedFileVersion)
        {
            throw new ReaderFileChangedException(filePath);
        }

        var result = CreateSnapshot(
            openedFileVersion,
            encoding.WebName,
            characterOffset,
            isComplete: true,
            checkpoints,
            chapters);
        snapshotUpdated?.Invoke(result);
        progress?.Report(new TextIndexProgress(
            openedFileVersion.FileSize,
            openedFileVersion.FileSize,
            characterOffset,
            progressChapters.ToArray()));

        return result;
    }

    private TextIndexSnapshot CreateSnapshot(
        ReaderFileVersion fileVersion,
        string encodingName,
        long totalCharacterCount,
        bool isComplete,
        IReadOnlyList<TextIndexCheckpoint> checkpoints,
        IReadOnlyList<ChapterInfo> chapters)
    {
        return new TextIndexSnapshot(
            fileVersion,
            encodingName,
            chapterDetector.PatternVersion,
            totalCharacterCount,
            isComplete,
            checkpoints.ToArray(),
            chapters.ToArray());
    }
}
