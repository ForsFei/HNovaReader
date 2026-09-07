using System.Runtime.CompilerServices;
using System.IO;
using System.Text;

namespace HNovaReader.Services.Reader;

internal static class TextEncodingUtilities
{
    private const int ByteBufferSize = 64 * 1024;
    private delegate bool SequenceValidator(ReadOnlySpan<byte> sequence);

    static TextEncodingUtilities()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding CreateEncoding(string encodingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encodingName);
        return Encoding.GetEncoding(
            encodingName,
            EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback);
    }

    public static Encoding CreateEncodingWithoutPreamble(string encodingName)
    {
        var encoding = CreateEncoding(encodingName);
        return encoding.CodePage switch
        {
            65001 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
            1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
            12000 => new UTF32Encoding(bigEndian: false, byteOrderMark: false),
            12001 => new UTF32Encoding(bigEndian: true, byteOrderMark: false),
            _ => encoding,
        };
    }

    public static async Task<int> GetPreambleLengthAsync(
        string filePath,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var bytes = new byte[4];
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        var bytesRead = await RandomAccess.ReadAsync(
            stream.SafeFileHandle,
            bytes,
            fileOffset: 0,
            cancellationToken);
        return GetPreambleLength(bytes, bytesRead, encoding.CodePage);
    }

    private static int GetPreambleLength(byte[] bytes, int bytesRead, int codePage)
    {
        var sample = bytes.AsSpan(0, bytesRead);
        return codePage switch
        {
            12001 when sample.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }) => 4,
            12000 when sample.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }) => 4,
            65001 when sample.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) => 3,
            1201 when sample.StartsWith(new byte[] { 0xFE, 0xFF }) => 2,
            1200 when sample.StartsWith(new byte[] { 0xFF, 0xFE }) => 2,
            54936 when sample.StartsWith(new byte[] { 0x84, 0x31, 0x95, 0x33 }) => 4,
            _ => 0,
        };
    }

    public static async IAsyncEnumerable<DecodedTextChunk> ReadChunksAsync(
        string filePath,
        Encoding encoding,
        long startByteOffset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startByteOffset);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            ByteBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Position = Math.Min(startByteOffset, stream.Length);

        var buffer = new byte[ByteBufferSize + 4];
        var bufferedCount = 0;
        var decodedByteOffset = stream.Position;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(bufferedCount, ByteBufferSize),
                cancellationToken);
            var totalCount = bufferedCount + bytesRead;
            var isFinal = bytesRead == 0;
            var safeCount = GetSafePrefixLength(
                buffer.AsSpan(0, totalCount),
                encoding.CodePage,
                isFinal);

            if (safeCount > 0)
            {
                var text = encoding.GetString(buffer, 0, safeCount);
                yield return new DecodedTextChunk(
                    decodedByteOffset,
                    decodedByteOffset + safeCount,
                    text);
                decodedByteOffset += safeCount;
            }

            bufferedCount = totalCount - safeCount;
            if (bufferedCount > 0)
            {
                Buffer.BlockCopy(buffer, safeCount, buffer, 0, bufferedCount);
            }

            if (isFinal)
            {
                yield break;
            }
        }
    }

    private static int GetSafePrefixLength(
        ReadOnlySpan<byte> bytes,
        int codePage,
        bool isFinal)
    {
        if (isFinal)
        {
            return bytes.Length;
        }

        return codePage switch
        {
            65001 => GetVariableWidthSafePrefix(bytes, GetUtf8SequenceLength, IsUtf8Sequence),
            1200 or 1201 => bytes.Length - (bytes.Length % 2),
            12000 or 12001 => bytes.Length - (bytes.Length % 4),
            936 or 54936 => GetGb18030SafePrefix(bytes),
            950 => GetVariableWidthSafePrefix(bytes, GetBig5SequenceLength, IsBig5Sequence),
            _ => bytes.Length,
        };
    }

    private static int GetVariableWidthSafePrefix(
        ReadOnlySpan<byte> bytes,
        Func<byte, int> sequenceLength,
        SequenceValidator isValidSequence)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var length = sequenceLength(bytes[offset]);
            if (length == 1)
            {
                offset++;
                continue;
            }

            if (bytes.Length - offset < length)
            {
                break;
            }

            offset += isValidSequence(bytes.Slice(offset, length)) ? length : 1;
        }

        return offset;
    }

    private static int GetUtf8SequenceLength(byte firstByte)
    {
        return firstByte switch
        {
            <= 0x7F => 1,
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 1,
        };
    }

    private static bool IsUtf8Sequence(ReadOnlySpan<byte> sequence)
    {
        for (var index = 1; index < sequence.Length; index++)
        {
            if ((sequence[index] & 0xC0) != 0x80)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetGb18030SafePrefix(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var first = bytes[offset];
            if (first <= 0x7F || first is 0x80 or 0xFF)
            {
                offset++;
                continue;
            }

            if (first is < 0x81 or > 0xFE)
            {
                offset++;
                continue;
            }

            if (bytes.Length - offset < 2)
            {
                break;
            }

            var second = bytes[offset + 1];
            if (second is >= 0x30 and <= 0x39)
            {
                if (bytes.Length - offset < 4)
                {
                    break;
                }

                var third = bytes[offset + 2];
                var fourth = bytes[offset + 3];
                offset += third is >= 0x81 and <= 0xFE
                    && fourth is >= 0x30 and <= 0x39
                        ? 4
                        : 1;
                continue;
            }

            offset += second is >= 0x40 and <= 0xFE and not 0x7F ? 2 : 1;
        }

        return offset;
    }

    private static int GetBig5SequenceLength(byte firstByte)
    {
        return firstByte is >= 0x81 and <= 0xFE ? 2 : 1;
    }

    private static bool IsBig5Sequence(ReadOnlySpan<byte> sequence)
    {
        return sequence.Length == 2
            && (sequence[1] is >= 0x40 and <= 0x7E
                || sequence[1] is >= 0xA1 and <= 0xFE);
    }
}

internal sealed record DecodedTextChunk(
    long StartByteOffset,
    long EndByteOffset,
    string Text);
