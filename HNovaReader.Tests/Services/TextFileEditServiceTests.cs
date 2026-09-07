using System.Text;
using HNovaReader.Services.Reader;

namespace HNovaReader.Tests.Services;

public sealed class TextFileEditServiceTests
{
    [Fact]
    public async Task ReplaceRangeAsync_StreamsLargeUtf8FileWithoutAddingBom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "large-utf8.txt");
        var prefix = new string('甲', 40_000);
        const string original = "需要替换的旧内容";
        var suffix = new string('乙', 40_000);
        try
        {
            await File.WriteAllTextAsync(
                filePath,
                prefix + original + suffix,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            var expectedVersion = ReaderFileVersion.FromPath(filePath);

            var result = await new StreamingTextFileEditService().ReplaceRangeAsync(
                new TextFileRangeEditRequest(
                    filePath,
                    "utf-8",
                    expectedVersion,
                    prefix.Length,
                    original.Length,
                    "更新后的正文"),
                cancellationToken);

            Assert.Equal(prefix + "更新后的正文" + suffix, await File.ReadAllTextAsync(filePath, cancellationToken));
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.Equal(ReaderFileVersion.FromPath(filePath), result.FileVersion);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.hnova.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceRangeAsync_PreservesUtf16Bom()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "utf16.txt");
        const string source = "开头旧段落结尾";
        try
        {
            await File.WriteAllTextAsync(
                filePath,
                source,
                new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
                cancellationToken);
            var expectedVersion = ReaderFileVersion.FromPath(filePath);

            await new StreamingTextFileEditService().ReplaceRangeAsync(
                new TextFileRangeEditRequest(
                    filePath,
                    "utf-16",
                    expectedVersion,
                    2,
                    3,
                    "新内容"),
                cancellationToken);

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            Assert.True(bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble));
            Assert.Equal("开头新内容结尾", await File.ReadAllTextAsync(filePath, Encoding.Unicode, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceRangeAsync_WhenVersionChanged_DoesNotOverwriteFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "changed.txt");
        try
        {
            await File.WriteAllTextAsync(filePath, "原内容", cancellationToken);
            var expectedVersion = ReaderFileVersion.FromPath(filePath);
            await File.WriteAllTextAsync(filePath, "外部程序已经修改了内容", cancellationToken);

            await Assert.ThrowsAsync<ReaderFileChangedException>(() =>
                new StreamingTextFileEditService().ReplaceRangeAsync(
                    new TextFileRangeEditRequest(
                        filePath,
                        "utf-8",
                        expectedVersion,
                        0,
                        3,
                        "编辑内容"),
                    cancellationToken));

            Assert.Equal("外部程序已经修改了内容", await File.ReadAllTextAsync(filePath, cancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.hnova.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"HNovaReader-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
