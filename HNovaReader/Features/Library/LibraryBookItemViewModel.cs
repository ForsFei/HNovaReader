using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HNovaReader.Services.Library;

namespace HNovaReader.Features.Library;

public sealed partial class LibraryBookItemViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;

    public LibraryBookItemViewModel(BookDetails details, LibraryViewModel owner)
    {
        Details = details;
        _owner = owner;
        OpenCommand = new AsyncRelayCommand(() => _owner.OpenBookAsync(this));
        ToggleFavoriteCommand = new AsyncRelayCommand(() => _owner.ToggleFavoriteAsync(this));
        EditCommand = new AsyncRelayCommand(() => _owner.EditBookAsync(this));
        ChangeCoverCommand = new AsyncRelayCommand(() => _owner.ChangeCoverAsync(this));
        RevealFileCommand = new RelayCommand(() => _owner.RevealFile(this));
        RefreshFileInfoCommand = new AsyncRelayCommand(() => _owner.RefreshFileInfoAsync(this));
        RelocateFileCommand = new AsyncRelayCommand(() => _owner.RelocateFileAsync(this));
        RemoveCommand = new AsyncRelayCommand(() => _owner.RemoveAsync(this));
        DeleteFileCommand = new AsyncRelayCommand(() => _owner.DeleteFileAsync(this));
    }

    public BookDetails Details { get; }

    [ObservableProperty]
    private bool isSelected;

    public Guid Id => Details.Id;

    public string Title => Details.Title;

    public string Author => Details.AuthorName ?? "未知作者";

    public string Series => Details.SeriesName is null
        ? "—"
        : Details.SeriesIndex is { } index
            ? $"{Details.SeriesName} · {index:0.##}"
            : Details.SeriesName;

    public string Tags => Details.Tags.Count == 0 ? "—" : string.Join(" · ", Details.Tags);

    public string FileSize => FormatFileSize(Details.FileSize);

    public string LastOpened => Details.LastOpenedAtUtc is { } lastOpened
        ? lastOpened.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
        : "尚未打开";

    public string PrimaryActionText => Details.LastOpenedAtUtc is null ? "开始阅读" : "继续阅读";

    public double ReadingProgress => Math.Clamp(Details.ReadingProgress ?? 0, 0, 1);

    public string ReadingProgressText => $"{ReadingProgress:P0}";

    public string ReadingChapter => string.IsNullOrWhiteSpace(Details.ReadingChapterTitle)
        ? "上次位置：正文"
        : $"上次位置：{Details.ReadingChapterTitle}";

    public bool IsFileMissing => !File.Exists(Details.FilePath);

    public string FileAvailability => IsFileMissing ? "文件丢失" : "可用";

    public string FavoriteActionText => Details.IsFavorite ? "取消收藏" : "加入收藏";

    public string FavoriteGlyph => Details.IsFavorite ? "★" : "☆";

    public bool HasCustomCover => !string.IsNullOrWhiteSpace(Details.CoverPath);

    public double AccentOpacity => 0.48 + ((uint)Details.Id.GetHashCode() % 5 * 0.09);

    public string CoverInitials
    {
        get
        {
            var text = new StringInfo(Title);
            return text.SubstringByTextElements(0, Math.Min(2, text.LengthInTextElements));
        }
    }

    public IAsyncRelayCommand OpenCommand { get; }

    public IAsyncRelayCommand ToggleFavoriteCommand { get; }

    public IAsyncRelayCommand EditCommand { get; }

    public IAsyncRelayCommand ChangeCoverCommand { get; }

    public IRelayCommand RevealFileCommand { get; }

    public IAsyncRelayCommand RefreshFileInfoCommand { get; }

    public IAsyncRelayCommand RelocateFileCommand { get; }

    public IAsyncRelayCommand RemoveCommand { get; }

    public IAsyncRelayCommand DeleteFileCommand { get; }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}
