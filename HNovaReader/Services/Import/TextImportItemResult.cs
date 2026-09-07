using HNovaReader.Services.Library;

namespace HNovaReader.Services.Import;

public sealed record TextImportItemResult(
    string SourcePath,
    string? StoredPath,
    TextImportItemStatus Status,
    TextEncodingDetectionResult? Encoding,
    BookDetails? Book,
    string? Message);
