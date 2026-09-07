using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HNovaReader.Services.Library;

namespace HNovaReader.Features.Library;

public sealed partial class BookEditorViewModel : ObservableObject
{
    private readonly BookDetails _book;
    private readonly Func<string?> _pickCoverImage;

    public BookEditorViewModel(BookDetails book, Func<string?> pickCoverImage)
    {
        _book = book;
        _pickCoverImage = pickCoverImage;
        title = book.Title;
        authorName = book.AuthorName ?? string.Empty;
        description = book.Description ?? string.Empty;
        seriesName = book.SeriesName ?? string.Empty;
        seriesIndexText = book.SeriesIndex?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        tagsText = string.Join(", ", book.Tags);
        isFavorite = book.IsFavorite;
        coverDisplayName = GetCoverDisplayName(book.CoverPath);
        coverPreviewPath = book.CoverPath;
    }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string authorName;

    [ObservableProperty]
    private string description;

    [ObservableProperty]
    private string seriesName;

    [ObservableProperty]
    private string seriesIndexText;

    [ObservableProperty]
    private string tagsText;

    [ObservableProperty]
    private bool isFavorite;

    [ObservableProperty]
    private string coverDisplayName;

    [ObservableProperty]
    private string? coverPreviewPath;

    public bool TryCreateResult(out BookEditorResult? result, out string? error)
    {
        var cleanedTitle = Title.Trim();
        if (string.IsNullOrWhiteSpace(cleanedTitle))
        {
            result = null;
            error = "书名不能为空。";
            return false;
        }

        if (cleanedTitle.Length > 400)
        {
            result = null;
            error = "书名不能超过 400 个字符。";
            return false;
        }

        if (!TryParseSeriesIndex(SeriesIndexText, out var seriesIndex))
        {
            result = null;
            error = "系列序号必须是数字，例如 1、2 或 2.5。";
            return false;
        }

        var cleanedSeries = CleanOptional(SeriesName);
        result = new BookEditorResult(
            new UpdateBookRequest
            {
                Title = cleanedTitle,
                AuthorName = CleanOptional(AuthorName),
                Description = CleanOptional(Description),
                SeriesName = cleanedSeries,
                SeriesIndex = cleanedSeries is null ? null : seriesIndex,
                Tags = ParseTags(TagsText),
                CoverPath = _book.CoverPath,
                IsFavorite = IsFavorite,
            },
            IsCoverChanged,
            SelectedCoverPath);
        error = null;
        return true;
    }

    public bool IsCoverChanged { get; private set; }

    public string? SelectedCoverPath { get; private set; }

    [RelayCommand]
    private void ChooseCover()
    {
        var selectedPath = _pickCoverImage();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        SelectedCoverPath = selectedPath;
        IsCoverChanged = true;
        CoverDisplayName = GetCoverDisplayName(selectedPath);
        CoverPreviewPath = selectedPath;
    }

    [RelayCommand]
    private void RemoveCover()
    {
        SelectedCoverPath = null;
        IsCoverChanged = true;
        CoverDisplayName = "使用动态默认封面";
        CoverPreviewPath = null;
    }

    private static string? CleanOptional(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyCollection<string> ParseTags(string value)
    {
        return value
            .Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryParseSeriesIndex(string value, out double? result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    private static string GetCoverDisplayName(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? "使用动态默认封面"
            : Path.GetFileName(path);
    }
}
