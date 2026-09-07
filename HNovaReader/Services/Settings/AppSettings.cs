using HNovaReader.Data.Entities;

namespace HNovaReader.Services.Settings;

public enum ReaderTheme
{
    Light,
    Dark,
    Sepia,
}

public enum ReadingModePreference
{
    Scroll,
    Paged,
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Services.Theming.AppTheme Theme { get; init; } = Services.Theming.AppTheme.System;

    public ReaderTheme ReaderTheme { get; init; } = ReaderTheme.Sepia;

    public string FontFamily { get; init; } = "Microsoft YaHei UI";

    public double FontSize { get; init; } = 18;

    public double LineHeight { get; init; } = 1.75;

    public double ParagraphSpacing { get; init; }

    public double MaximumContentWidth { get; init; } = 1040;

    public double HorizontalMargin { get; init; } = 64;

    public ReadingModePreference ReadingMode { get; init; } = ReadingModePreference.Scroll;

    public string? LibraryFolder { get; init; }

    public BookStorageMode ImportBehavior { get; init; } = BookStorageMode.ManagedCopy;

    public bool RememberWindowSize { get; init; } = true;

    public string DefaultTextEncoding { get; init; } = "utf-8";

    public double? WindowWidth { get; init; }

    public double? WindowHeight { get; init; }
}
