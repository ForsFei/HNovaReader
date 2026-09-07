namespace HNovaReader.Services.Import;

public enum EncodingDetectionSource
{
    ByteOrderMark,
    UnicodeHeuristic,
    StrictUtf8,
    Statistical,
    Fallback,
    UserOverride,
}
