namespace HNovaReader.Services.Import;

public sealed record TextEncodingDetectionResult(
    string EncodingName,
    double Confidence,
    EncodingDetectionSource Source);
