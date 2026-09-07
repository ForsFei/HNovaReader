namespace HNovaReader.Services.Import;

public enum TextImportStage
{
    Scanning,
    CheckingDuplicate,
    DetectingEncoding,
    Copying,
    Saving,
    Completed,
}
