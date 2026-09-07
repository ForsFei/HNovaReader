using System.Buffers;
using System.IO;
using System.Text;
using UtfUnknown;

namespace HNovaReader.Services.Import;

public sealed class TextEncodingDetector : ITextEncodingDetector
{
    private const int SampleSize = 64 * 1024;
    private const int MaximumSampleCount = 3;

    static TextEncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<TextEncodingDetectionResult> DetectAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var samples = await ReadSamplesAsync(filePath, cancellationToken);
        if (samples.Count == 0 || samples[0].Length == 0)
        {
            return new TextEncodingDetectionResult(
                Encoding.UTF8.WebName,
                0.5,
                EncodingDetectionSource.Fallback);
        }

        if (TryDetectByteOrderMark(samples[0], out var bomEncoding))
        {
            return new TextEncodingDetectionResult(
                bomEncoding.WebName,
                1,
                EncodingDetectionSource.ByteOrderMark);
        }

        if (TryDetectUtf16ByNullPattern(samples[0], out var utf16Encoding))
        {
            return new TextEncodingDetectionResult(
                utf16Encoding.WebName,
                0.8,
                EncodingDetectionSource.UnicodeHeuristic);
        }

        if (samples
            .Select((sample, index) => IsStrictUtf8Sample(
                sample,
                allowLeadingContinuationBytes: index > 0))
            .All(isValid => isValid))
        {
            return new TextEncodingDetectionResult(
                Encoding.UTF8.WebName,
                0.99,
                EncodingDetectionSource.StrictUtf8);
        }

        var combinedSamples = CombineSamples(samples);
        var detection = CharsetDetector.DetectFromBytes(combinedSamples).Detected;
        if (detection?.Encoding is { } detectedEncoding)
        {
            return new TextEncodingDetectionResult(
                GetCanonicalEncodingName(detectedEncoding),
                Math.Clamp(detection.Confidence, 0, 1),
                EncodingDetectionSource.Statistical);
        }

        return new TextEncodingDetectionResult(
            Encoding.GetEncoding("GB18030").WebName,
            0,
            EncodingDetectionSource.Fallback);
    }

    public TextEncodingDetectionResult GetUserOverride(string encodingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encodingName);
        var encoding = Encoding.GetEncoding(
            encodingName.Trim(),
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        return new TextEncodingDetectionResult(
            GetCanonicalEncodingName(encoding),
            1,
            EncodingDetectionSource.UserOverride);
    }

    private static async Task<IReadOnlyList<byte[]>> ReadSamplesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            SampleSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        if (stream.Length == 0)
        {
            return Array.Empty<byte[]>();
        }

        var positions = GetSamplePositions(stream.Length);
        var samples = new List<byte[]>(positions.Count);
        foreach (var position in positions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesToRead = (int)Math.Min(SampleSize, stream.Length - position);
            var sample = new byte[bytesToRead];
            var totalRead = 0;

            while (totalRead < sample.Length)
            {
                var read = await RandomAccess.ReadAsync(
                    stream.SafeFileHandle,
                    sample.AsMemory(totalRead),
                    position + totalRead,
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead != sample.Length)
            {
                Array.Resize(ref sample, totalRead);
            }

            samples.Add(sample);
        }

        return samples;
    }

    private static IReadOnlyList<long> GetSamplePositions(long fileLength)
    {
        if (fileLength <= SampleSize)
        {
            return [0];
        }

        var positions = new[]
        {
            0L,
            Math.Max(0, (fileLength / 2) - (SampleSize / 2)),
            Math.Max(0, fileLength - SampleSize),
        };

        return positions
            .Distinct()
            .Take(MaximumSampleCount)
            .ToArray();
    }

    private static bool TryDetectByteOrderMark(byte[] sample, out Encoding encoding)
    {
        ReadOnlySpan<byte> bytes = sample;
        if (bytes.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            return true;
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
            return true;
        }

        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            return true;
        }

        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            encoding = Encoding.BigEndianUnicode;
            return true;
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            encoding = Encoding.Unicode;
            return true;
        }

        if (bytes.StartsWith(new byte[] { 0x84, 0x31, 0x95, 0x33 }))
        {
            encoding = Encoding.GetEncoding("GB18030");
            return true;
        }

        encoding = null!;
        return false;
    }

    private static bool TryDetectUtf16ByNullPattern(byte[] sample, out Encoding encoding)
    {
        if (sample.Length < 16)
        {
            encoding = null!;
            return false;
        }

        var pairCount = Math.Min(sample.Length / 2, 2048);
        var evenNulls = 0;
        var oddNulls = 0;
        for (var pair = 0; pair < pairCount; pair++)
        {
            if (sample[pair * 2] == 0)
            {
                evenNulls++;
            }

            if (sample[(pair * 2) + 1] == 0)
            {
                oddNulls++;
            }
        }

        var evenRatio = (double)evenNulls / pairCount;
        var oddRatio = (double)oddNulls / pairCount;
        if (oddRatio >= 0.3 && evenRatio <= 0.05)
        {
            encoding = Encoding.Unicode;
            return true;
        }

        if (evenRatio >= 0.3 && oddRatio <= 0.05)
        {
            encoding = Encoding.BigEndianUnicode;
            return true;
        }

        encoding = null!;
        return false;
    }

    private static bool IsStrictUtf8Sample(
        byte[] sample,
        bool allowLeadingContinuationBytes)
    {
        ReadOnlySpan<byte> remaining = sample;
        while (allowLeadingContinuationBytes
            && !remaining.IsEmpty
            && IsUtf8ContinuationByte(remaining[0]))
        {
            remaining = remaining[1..];
        }

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(remaining, out _, out var consumed);
            if (status == OperationStatus.Done)
            {
                remaining = remaining[consumed..];
                continue;
            }

            return status == OperationStatus.NeedMoreData;
        }

        return true;
    }

    private static bool IsUtf8ContinuationByte(byte value)
    {
        return (value & 0xC0) == 0x80;
    }

    private static byte[] CombineSamples(IReadOnlyList<byte[]> samples)
    {
        var totalLength = samples.Sum(sample => sample.Length) + ((samples.Count - 1) * 2);
        var combined = new byte[totalLength];
        var destinationOffset = 0;

        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            sample.CopyTo(combined, destinationOffset);
            destinationOffset += sample.Length;

            if (index < samples.Count - 1)
            {
                combined[destinationOffset++] = (byte)'\r';
                combined[destinationOffset++] = (byte)'\n';
            }
        }

        return combined;
    }

    private static string GetCanonicalEncodingName(Encoding encoding)
    {
        return encoding.CodePage switch
        {
            936 or 54936 => Encoding.GetEncoding("GB18030").WebName,
            950 => Encoding.GetEncoding("Big5").WebName,
            _ => encoding.WebName,
        };
    }
}
