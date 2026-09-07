using HNovaReader.Services.Library;

namespace HNovaReader.Features.Library;

public interface ILibraryInteractionService
{
    IReadOnlyList<string> PickTextFiles();

    string? PickReplacementTextFile(BookDetails book);

    string? PickCoverImage();

    BookEditorResult? EditBook(BookDetails book);

    string? PromptForName(string title, string label, string? initialValue = null);

    bool Confirm(string title, string message, bool isDangerous = false);

    void RevealFile(string filePath);

    void MoveFileToRecycleBin(string filePath);

    void ShowMessage(string title, string message, bool isError = false);
}
