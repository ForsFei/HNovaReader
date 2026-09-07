namespace HNovaReader.Services.Import;

public interface ITextEncodingDetector
{
    Task<TextEncodingDetectionResult> DetectAsync(
        string filePath,
        CancellationToken cancellationToken);

    TextEncodingDetectionResult GetUserOverride(string encodingName);
}
