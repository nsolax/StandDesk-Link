using StandDesk.Core;

namespace StandDesk.Tests;

public sealed class StartupSettingsTests
{
    [Fact]
    public void UsesSharedUserProfileOutsideRedirectedAppData()
    {
        var path = SettingsStore.ResolvePath(@"C:\Users\desk-user", "");
        Assert.Equal(@"C:\Users\desk-user\.standdesk\settings.json", path);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("relative", "")]
    public void NeverUsesWorkingDirectoryAsSettingsLocation(string environment, string knownFolder) =>
        Assert.Throws<InvalidOperationException>(() => SettingsStore.ResolvePath(environment, knownFolder));

    [Fact]
    public async Task BackgroundStartupDoesNotSilentlyReplaceMissingSavedSettingsWithDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "settings.json");
        await Assert.ThrowsAsync<FileNotFoundException>(() => new SettingsStore(path).LoadAsync(requireExisting: true));
    }

    [Fact]
    public async Task NewStartupSessionRestoresValidationHotkeysAndStartupPreference()
    {
        var folder = Path.Combine(Path.GetTempPath(), "StandDeskTests", Guid.NewGuid().ToString());
        var path = Path.Combine(folder, "settings.json");
        try
        {
            await new SettingsStore(path).SaveAsync(new DeskSettings { MotionValidated = true, HotkeysEnabled = true, StartAtLogin = true });
            var loaded = await new SettingsStore(path).LoadAsync(requireExisting: true);
            Assert.True(loaded.MotionValidated);
            Assert.True(loaded.HotkeysEnabled);
            Assert.True(loaded.StartAtLogin);
            Assert.Equal(74.12m, loaded.SitCm);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
