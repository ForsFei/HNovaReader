using HNovaReader.Features.Library;
using HNovaReader.Services.Library;
using HNovaReader.Services.Settings;
using HNovaReader.Tests.TestSupport;

namespace HNovaReader.Tests.Features;

public sealed class LibraryUpdateTests
{
    [Fact]
    public async Task ManualUpdateCommand_SynchronizesAndReportsCounts()
    {
        var sync = new StubSyncService();
        using var model = Create(sync, new StubAppSettingsService());
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, sync.Calls);
        Assert.Contains("新增 2 本", model.StatusMessage);
        Assert.Contains("移除 1 本", model.StatusMessage);
        Assert.False(model.IsBusy);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Initialize_OnlySynchronizesAutomaticallyWhenEnabled(bool enabled, int expected)
    {
        var sync = new StubSyncService();
        var settings = new StubAppSettingsService();
        settings.Set(new AppSettings { AutoUpdateLibrary = enabled, LibraryUpdateIntervalMinutes = 30 });
        using var model = Create(sync, settings);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, sync.Calls);
    }

    [Fact]
    public async Task ManualUpdateFailure_ReportsErrorAndReleasesBusyState()
    {
        var sync = new StubSyncService { Throw = true };
        using var model = Create(sync, new StubAppSettingsService());
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("书库更新失败", model.StatusMessage);
        Assert.False(model.IsBusy);
        sync.Throw = false;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, sync.Calls);
    }

    private static LibraryViewModel Create(ILibrarySyncService sync, StubAppSettingsService settings)
    {
        var library = new StubLibraryService();
        return new LibraryViewModel(library, new StubTextEncodingDetector(), new StubCoverFileStore(),
            new StubLibraryInteractionService(), new StubLibraryOrganizationService(library), sync, settings);
    }

    private sealed class StubSyncService : ILibrarySyncService
    {
        public int Calls { get; private set; }
        public bool Throw { get; set; }

        public Task<LibrarySyncResult> SyncAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw)
            {
                throw new IOException("测试失败");
            }

            return Task.FromResult(new LibrarySyncResult(2, 1, []));
        }
    }
}
