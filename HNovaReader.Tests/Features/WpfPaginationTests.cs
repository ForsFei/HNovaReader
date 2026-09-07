using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;

namespace HNovaReader.Tests.Features;

public sealed class WpfPaginationTests
{
    [Fact]
    public void DynamicPaginator_CreatesMultiplePagesWithTextPointerBoundaries()
    {
        RunInSta(() =>
        {
            var text = string.Join(
                "\n\n",
                Enumerable.Range(1, 80).Select(index =>
                    $"第 {index} 段。分页必须记录当前页首的字符位置，并在窗口或字号变化后从同一锚点重新排版。"));
            var document = new FlowDocument(new Paragraph(new Run(text)))
            {
                PageWidth = 640,
                PageHeight = 480,
                PagePadding = new Thickness(48),
                FontSize = 18,
                ColumnWidth = double.PositiveInfinity,
            };
            var paginator = Assert.IsAssignableFrom<DynamicDocumentPaginator>(
                ((IDocumentPaginatorSource)document).DocumentPaginator);
            paginator.IsBackgroundPaginationEnabled = false;
            paginator.PageSize = new Size(640, 480);

            var firstPage = paginator.GetPage(0);
            var secondPage = paginator.GetPage(1);
            Assert.NotSame(DocumentPage.Missing, firstPage);
            Assert.NotSame(DocumentPage.Missing, secondPage);

            var firstPosition = Assert.IsType<TextPointer>(paginator.GetPagePosition(firstPage));
            var secondPosition = Assert.IsType<TextPointer>(paginator.GetPagePosition(secondPage));
            Assert.True(firstPosition.CompareTo(secondPosition) < 0);
            Assert.Equal(1, paginator.GetPageNumber(secondPosition));
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF pagination test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
