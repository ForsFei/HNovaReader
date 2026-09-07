using System.Buffers;
using System.IO;

namespace HNovaReader.Services.Reader;

public sealed class StreamingTextFileEditService : ITextFileEditService
{
    private const int CharacterBufferSize = 32 * 1024;

    public async Task<TextFileRangeEditResult> ReplaceRangeAsync(
        TextFileRangeEditRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EncodingName);
        ArgumentNullException.ThrowIfNull(request.ExpectedVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(request.StartCharacterOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(request.OriginalLength);
        ArgumentNullException.ThrowIfNull(request.ReplacementText);

        var filePath = Path.GetFullPath(request.FilePath);
        if (ReaderFileVersion.FromPath(filePath) != request.ExpectedVersion)
        {
            throw new ReaderFileChangedException(filePath);
        }

        var encoding = TextEncodingUtilities.CreateEncoding(request.EncodingName);
        var writerEncoding = TextEncodingUtilities.CreateEncodingWithoutPreamble(
            request.EncodingName);
        var preambleLength = await TextEncodingUtilities.GetPreambleLengthAsync(
            filePath,
            encoding,
            cancellationToken);
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException("无法确定 TXT 文件所在目录。");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.hnova.tmp");

        try
        {
            await RewriteAsync(
                filePath,
                temporaryPath,
                encoding,
                writerEncoding,
                preambleLength,
                request,
                cancellationToken);

            if (ReaderFileVersion.FromPath(filePath) != request.ExpectedVersion)
            {
                throw new ReaderFileChangedException(filePath);
            }

            File.Replace(temporaryPath, filePath, null, ignoreMetadataErrors: true);
            temporaryPath = string.Empty;
            return new TextFileRangeEditResult(ReaderFileVersion.FromPath(filePath));
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryPath) && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task RewriteAsync(
        string sourcePath,
        string temporaryPath,
        System.Text.Encoding encoding,
        System.Text.Encoding writerEncoding,
        int preambleLength,
        TextFileRangeEditRequest request,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CharacterBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CharacterBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

        if (preambleLength > 0)
        {
            var preamble = new byte[preambleLength];
            await source.ReadExactlyAsync(preamble, cancellationToken);
            await destination.WriteAsync(preamble, cancellationToken);
        }

        using var reader = new StreamReader(
            source,
            encoding,
            detectEncodingFromByteOrderMarks: false,
            CharacterBufferSize,
            leaveOpen: true);
        await using var writer = new StreamWriter(
            destination,
            writerEncoding,
            CharacterBufferSize,
            leaveOpen: true);

        var buffer = ArrayPool<char>.Shared.Rent(CharacterBufferSize);
        try
        {
            await CopyCharactersAsync(
                reader,
                writer,
                buffer,
                request.StartCharacterOffset,
                write: true,
                cancellationToken);
            await CopyCharactersAsync(
                reader,
                writer,
                buffer,
                request.OriginalLength,
                write: false,
                cancellationToken);
            await writer.WriteAsync(request.ReplacementText.AsMemory(), cancellationToken);

            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await writer.FlushAsync(cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static async Task CopyCharactersAsync(
        StreamReader reader,
        StreamWriter writer,
        char[] buffer,
        long characterCount,
        bool write,
        CancellationToken cancellationToken)
    {
        var remaining = characterCount;
        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = await reader.ReadAsync(
                buffer.AsMemory(0, requested),
                cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("编辑范围已超出当前 TXT 文件内容。");
            }

            if (write)
            {
                await writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            remaining -= read;
        }
    }
}
