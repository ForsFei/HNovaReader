using System.Text;
using HNovaReader.Services.Import;

namespace HNovaReader.Tests.Services;

public sealed class TextEncodingDetectorTests
{
    [Theory]
    [InlineData("utf-8-bom", "utf-8")]
    [InlineData("utf-16-le", "utf-16")]
    [InlineData("utf-16-be", "utf-16BE")]
    public async Task DetectAsync_DetectsUnicodeByteOrderMarks(
        string sampleKind,
        string expectedEncodingName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, $"{sampleKind}.txt");
            await File.WriteAllTextAsync(
                path,
                "第一章 编码测试",
                GetEncoding(sampleKind),
                cancellationToken);

            var result = await new TextEncodingDetector().DetectAsync(path, cancellationToken);

            Assert.Equal(expectedEncodingName, result.EncodingName, ignoreCase: true);
            Assert.Equal(1, result.Confidence);
            Assert.Equal(EncodingDetectionSource.ByteOrderMark, result.Source);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task DetectAsync_UsesStrictUtf8ForBomlessUtf8()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "utf8.txt");
            await File.WriteAllTextAsync(
                path,
                "这是一本没有 BOM 的 UTF-8 小说。",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var result = await new TextEncodingDetector().DetectAsync(path, cancellationToken);

            Assert.Equal("utf-8", result.EncodingName);
            Assert.Equal(EncodingDetectionSource.StrictUtf8, result.Source);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task DetectAsync_RecognizesBomlessUtf16ByNullPattern()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "utf16-no-bom.txt");
            var bytes = new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: false,
                throwOnInvalidBytes: true)
                .GetBytes(string.Concat(Enumerable.Repeat("Chapter one. ", 20)));
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);

            var result = await new TextEncodingDetector().DetectAsync(path, cancellationToken);

            Assert.Equal("utf-16", result.EncodingName, ignoreCase: true);
            Assert.Equal(EncodingDetectionSource.UnicodeHeuristic, result.Source);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task DetectAsync_DoesNotAcceptLeadingContinuationByteAsStrictUtf8()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "invalid-utf8.txt");
            await File.WriteAllBytesAsync(
                path,
                [0x80, (byte)'A', (byte)'B', (byte)'C'],
                cancellationToken);

            var result = await new TextEncodingDetector().DetectAsync(path, cancellationToken);

            Assert.NotEqual(EncodingDetectionSource.StrictUtf8, result.Source);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Theory]
    [InlineData("GB18030", "这是用于测试简体中文小说编码识别的章节内容。")]
    [InlineData("Big5", "這是用於測試繁體中文小說編碼識別的章節內容。")]
    public async Task DetectAsync_DetectsLegacyChineseEncoding(
        string encodingName,
        string sentence)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateTempRoot();
        Directory.CreateDirectory(root);

        try
        {
            var detector = new TextEncodingDetector();
            var path = Path.Combine(root, $"{encodingName}.txt");
            var content = string.Concat(Enumerable.Repeat(sentence, 300));
            await File.WriteAllTextAsync(
                path,
                content,
                Encoding.GetEncoding(encodingName),
                cancellationToken);

            var result = await detector.DetectAsync(path, cancellationToken);

            Assert.Equal(
                Encoding.GetEncoding(encodingName).WebName,
                result.EncodingName,
                ignoreCase: true);
            Assert.Equal(EncodingDetectionSource.Statistical, result.Source);
            Assert.InRange(result.Confidence, 0.5, 1);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void GetUserOverride_NormalizesSupportedCodePage()
    {
        var result = new TextEncodingDetector().GetUserOverride("gbk");

        Assert.Equal("gb18030", result.EncodingName, ignoreCase: true);
        Assert.Equal(EncodingDetectionSource.UserOverride, result.Source);
        Assert.Equal(1, result.Confidence);
    }

    private static Encoding GetEncoding(string sampleKind)
    {
        return sampleKind switch
        {
            "utf-8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf-16-le" => Encoding.Unicode,
            "utf-16-be" => Encoding.BigEndianUnicode,
            _ => throw new ArgumentOutOfRangeException(nameof(sampleKind)),
        };
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "HNovaReader.Tests",
            Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTempRoot(string root)
    {
        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HNovaReader.Tests"));
        var resolvedRoot = Path.GetFullPath(root);
        if (resolvedRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }
}
