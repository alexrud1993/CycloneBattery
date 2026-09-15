using CycloneBattery.Core.Settings;
using CycloneBattery.Tests.Testing;
using Xunit;

namespace CycloneBattery.Tests.Settings;

/// <summary>Covers defaults, persistence round trips and the corrupt-file fallback.</summary>
public class AppSettingsTests
{
    [Fact]
    public void DefaultsMatchTheSpecification()
    {
        AppSettings settings = AppSettings.CreateDefault();

        Assert.False(settings.StartWithWindows);
        Assert.True(settings.ShowWidgetOnStartup);
        Assert.False(settings.WidgetAlwaysOnTop);
        Assert.True(settings.HideWidgetWhenDisconnected);
        Assert.False(settings.WidgetVisible);
        Assert.True(settings.LowBatteryAlertEnabled);
        Assert.Equal(20, settings.LowBatteryThresholdPercent);
        Assert.False(settings.HasSavedWidgetPosition);
    }

    [Fact]
    public void AllowedThresholdsAreTheDocumentedSet()
    {
        Assert.Equal([10, 15, 20, 25, 30], AppSettings.AllowedLowBatteryThresholds);
    }

    [Fact]
    public void RoundTripPreservesValues()
    {
        var store = new InMemorySettingsStore();
        var service = new JsonAppSettingsService(store);

        service.Save(new AppSettings
        {
            StartWithWindows = true,
            ShowWidgetOnStartup = false,
            WidgetAlwaysOnTop = true,
            HideWidgetWhenDisconnected = false,
            WidgetVisible = true,
            WidgetLeft = 120.5,
            WidgetTop = 340.25,
            LowBatteryAlertEnabled = false,
            LowBatteryThresholdPercent = 30,
        });

        var reloaded = new JsonAppSettingsService(store).Load();

        Assert.True(reloaded.StartWithWindows);
        Assert.False(reloaded.ShowWidgetOnStartup);
        Assert.True(reloaded.WidgetAlwaysOnTop);
        Assert.False(reloaded.HideWidgetWhenDisconnected);
        Assert.True(reloaded.WidgetVisible);
        Assert.Equal(120.5, reloaded.WidgetLeft);
        Assert.Equal(340.25, reloaded.WidgetTop);
        Assert.False(reloaded.LowBatteryAlertEnabled);
        Assert.Equal(30, reloaded.LowBatteryThresholdPercent);
        Assert.True(reloaded.HasSavedWidgetPosition);
    }

    [Fact]
    public void SavedJsonIsIndentedAndUsesTheStoreLocation()
    {
        var store = new InMemorySettingsStore();
        var service = new JsonAppSettingsService(store);

        service.Save(AppSettings.CreateDefault());

        Assert.NotNull(store.Content);
        Assert.Contains('\n', store.Content);
        Assert.Equal("memory://settings.json", service.Location);
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var service = new JsonAppSettingsService(new InMemorySettingsStore { Content = null });

        AppSettings settings = service.Load();

        Assert.Equal(AppSettings.DefaultLowBatteryThresholdPercent, settings.LowBatteryThresholdPercent);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void EmptyFileYieldsDefaults()
    {
        var service = new JsonAppSettingsService(new InMemorySettingsStore { Content = "   " });

        Assert.Equal(AppSettings.DefaultLowBatteryThresholdPercent, service.Load().LowBatteryThresholdPercent);
    }

    [Fact]
    public void CorruptFileFallsBackSafely()
    {
        var store = new InMemorySettingsStore { Content = "{ this is not json ]" };
        var service = new JsonAppSettingsService(store);

        AppSettings settings = service.Load();

        Assert.Equal(AppSettings.CreateDefault().LowBatteryThresholdPercent, settings.LowBatteryThresholdPercent);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void UnknownThresholdIsNormalizedToDefault()
    {
        var store = new InMemorySettingsStore { Content = """{ "lowBatteryThresholdPercent": 47 }""" };
        var service = new JsonAppSettingsService(store);

        Assert.Equal(AppSettings.DefaultLowBatteryThresholdPercent, service.Load().LowBatteryThresholdPercent);
    }

    [Fact]
    public void SaveNormalizesBeforePersisting()
    {
        var store = new InMemorySettingsStore();
        var service = new JsonAppSettingsService(store);

        service.Save(new AppSettings { LowBatteryThresholdPercent = 999, WidgetLeft = double.PositiveInfinity });

        Assert.Equal(AppSettings.DefaultLowBatteryThresholdPercent, service.Current.LowBatteryThresholdPercent);
        Assert.True(double.IsNaN(service.Current.WidgetLeft));
    }

    [Fact]
    public void CurrentReflectsSavedSettings()
    {
        var store = new InMemorySettingsStore();
        var service = new JsonAppSettingsService(store);

        service.Save(new AppSettings { StartWithWindows = true });

        Assert.True(service.Current.StartWithWindows);
    }

    [Fact]
    public void SettingsChangedIsRaisedAfterSave()
    {
        var service = new JsonAppSettingsService(new InMemorySettingsStore());
        AppSettings? observed = null;
        service.SettingsChanged += (_, settings) => observed = settings;

        service.Save(new AppSettings { WidgetAlwaysOnTop = true });

        Assert.NotNull(observed);
        Assert.True(observed!.WidgetAlwaysOnTop);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        AppSettings original = AppSettings.CreateDefault();
        AppSettings clone = original.Clone();

        clone.StartWithWindows = true;
        clone.LowBatteryThresholdPercent = 30;

        Assert.False(original.StartWithWindows);
        Assert.Equal(20, original.LowBatteryThresholdPercent);
    }

    [Fact]
    public void UnreadableStoreFallsBackToDefaults()
    {
        var service = new JsonAppSettingsService(new ThrowingSettingsStore());

        AppSettings settings = service.Load();

        Assert.Equal(AppSettings.DefaultLowBatteryThresholdPercent, settings.LowBatteryThresholdPercent);
    }

    private sealed class ThrowingSettingsStore : ISettingsStore
    {
        public string Location => "memory://throwing";

        public string? ReadAllText() => throw new IOException("disk unavailable");

        public void WriteAllText(string content) => throw new IOException("disk unavailable");
    }
}
