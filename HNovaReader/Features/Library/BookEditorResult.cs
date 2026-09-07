using HNovaReader.Services.Library;

namespace HNovaReader.Features.Library;

public sealed record BookEditorResult(
    UpdateBookRequest Update,
    bool IsCoverChanged,
    string? SelectedCoverPath);
