using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HNovaReader.Features.Reader;
using HNovaReader.Features.Library;
using HNovaReader.Features.Settings;
using HNovaReader.Services.Reader;
using HNovaReader.Services.Settings;
using HNovaReader.Services.Theming;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Features;

public sealed class ReaderVisualTests
{
    private sealed class StubThemeService : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;
        public AppTheme EffectiveTheme => CurrentTheme;
        public void Apply(AppTheme theme) => CurrentTheme = theme;
    }

    [Fact]
    public void Views_LoadTemplatesAndReaderThemeCoversWholeSurface()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? host = null;
            try
            {
                var application = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/HNovaReader;component/Resources/Themes/LightTheme.xaml", UriKind.Relative),
                });
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/HNovaReader;component/Resources/Styles/Controls.xaml", UriKind.Relative),
                });
                var settings = new StubAppSettingsService();
                var model = new ReaderViewModel(
                    new StubReaderEngineFactory(),
                    new StubReadingPositionService(),
                    new StubReaderAnnotationService(),
                    settings,
                    new StubTextFileEditService(),
                    new StubLibraryService())
                {
                    BookTitle = "山海之间 · 阅读样章",
                    Author = "HNOVA",
                    CurrentWindow = new TextReadWindow(0,
                        string.Join("\n\n", Enumerable.Repeat(
                            "午后的光落在书页上，窗外树影缓缓移动。阅读是一段安静的旅程，在文字之间留一点空白，让目光与思绪都能停留。", 40)),
                        true, true),
                };
                var reader = new ReaderView { DataContext = model };
                host = new Window
                {
                    Width = 1200,
                    Height = 800,
                    Left = -20000,
                    Top = -20000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = reader,
                };
                host.Show();
                foreach (var theme in Enum.GetValues<ReaderTheme>())
                {
                    model.ReaderTheme = theme;
                    FlushLayout(host);
                    var canvas = Assert.IsType<Grid>(reader.Content);
                    var expected = theme == ReaderTheme.Dark ? "#FF111419"
                        : theme == ReaderTheme.Light ? "#FFFAFAF8" : "#FFF2E8D5";
                    Assert.Equal(expected, Assert.IsType<SolidColorBrush>(canvas.Background).Color.ToString());
                    SaveSnapshot(reader, $"reader-{theme}");
                }

                model.ToggleCatalogCommand.Execute(null);
                model.ToggleAnnotationsCommand.Execute(null);
                FlushLayout(host);
                SaveSnapshot(reader, "reader-panels");

                model.ToggleCatalogCommand.Execute(null);
                model.ToggleAnnotationsCommand.Execute(null);
                model.ReaderTheme = ReaderTheme.Dark;
                model.ReadingLayoutMode = ReaderLayoutMode.Paged;
                FlushLayout(host);
                Assert.Single(Assert.IsType<FlowDocumentPageViewer>(reader.FindName("PagedReader")).PageViews);
                SaveSnapshot(reader, "reader-paged");
                model.IsEditMode = true;
                model.EditText = model.CurrentWindow.Text;
                FlushLayout(host);
                SaveSnapshot(reader, "reader-editor");

                var settingsView = new SettingsView
                {
                    DataContext = new SettingsViewModel(
                        new StubThemeService(),
                        settings,
                        new StubSettingsInteractionService(),
                        new StubLibraryOrganizationService(new StubLibraryService()),
                        new StubAppPaths()),
                };
                host.Width = 840;
                host.Content = settingsView;
                FlushLayout(host);
                var combo = Descendants(settingsView).OfType<ComboBox>().First();
                combo.ApplyTemplate();
                Assert.NotNull(combo.Template.FindName("PART_Popup", combo));
                combo.IsDropDownOpen = true;
                FlushLayout(host);
                var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
                Assert.True(popup.IsOpen);
                SaveSnapshot(settingsView, "settings");
                combo.IsDropDownOpen = false;
                SaveSnapshot(Assert.IsAssignableFrom<FrameworkElement>(popup.Child), "settings-dropdown");
                var treeStyle = Assert.IsType<Style>(application.FindResource("HNova.LibraryTreeItem"));
                var tree = new TreeView
                {
                    Width = 220,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 16, 12, 16),
                    Background = (Brush)application.FindResource("SidebarBackgroundBrush"),
                    BorderThickness = new Thickness(0),
                    ItemContainerStyle = treeStyle,
                };
                var folder = new TreeViewItem { Header = "扫描式光刻", IsExpanded = true };
                var series = new TreeViewItem { Header = "扫描", Style = treeStyle };
                folder.Items.Add(series);
                var child = new TreeViewItem { Header = "参考资料", Style = treeStyle, IsExpanded = true };
                var grandchild = new TreeViewItem { Header = "2026 年", Style = treeStyle, IsExpanded = true };
                var leaf = new TreeViewItem { Header = "设计文档", Style = treeStyle };
                series.IsExpanded = true;
                series.Items.Add(child);
                child.Items.Add(grandchild);
                grandchild.Items.Add(leaf);
                foreach (var item in new[] { folder, series, child, grandchild, leaf })
                {
                    item.DataContext = new LibraryTreeNodeViewModel
                    {
                        Kind = LibraryTreeNodeKind.Folder,
                        FolderId = Guid.NewGuid(),
                        Name = item.Header.ToString()!,
                        Count = 0,
                        IsExpanded = true,
                    };
                }
                tree.Items.Add(folder);
                host.Width = 440;
                host.Height = 360;
                host.Content = tree;
                FlushLayout(host);
                series.IsSelected = true;
                series.IsExpanded = true;
                child.IsExpanded = true;
                grandchild.IsExpanded = true;
                FlushLayout(host);
                Assert.Equal(Visibility.Visible, Assert.IsType<Border>(
                    series.Template.FindName("SelectionMark", series)).Visibility);
                SaveSnapshot(tree, "library-tree-selection");
                Assert.True(leaf.IsVisible);
                SaveSnapshot(tree, "library-nested-folders");

                var menu = new ContextMenu
                {
                    Style = (Style)application.FindResource("HNova.ContextMenu"),
                    PlacementTarget = series,
                    Placement = PlacementMode.Right,
                };
                menu.Resources.Add(typeof(MenuItem), application.FindResource("HNova.MenuItem"));
                menu.Resources.Add(typeof(Separator), application.FindResource("HNova.MenuSeparator"));
                menu.Items.Add(new MenuItem { Header = "新建子文件夹" });
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = "重命名" });
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = "删除文件夹", Foreground = (Brush)application.FindResource("DangerBrush") });
                menu.IsOpen = true;
                FlushLayout(host);
                SaveSnapshot(menu, "library-tree-menu");
                menu.IsOpen = false;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                host?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI rendering timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void FlushLayout(Window host)
    {
        host.UpdateLayout();
        host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        host.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void SaveSnapshot(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("HNOVAREADER_UI_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)view.ActualWidth), Math.Max(1, (int)view.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(stream);
    }
}
