namespace HNovaReader.Features.Settings;

public interface ISettingsInteractionService
{
    string? PickLibraryFolder(string? currentPath);

    bool ConfirmLibraryMigration(string sourcePath, string targetPath);
}
