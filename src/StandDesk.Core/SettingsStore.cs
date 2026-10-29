using System.Text.Json;

namespace StandDesk.Core;

public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string FilePath { get; } = filePath;
    public static string ResolvePath(string? userProfileEnvironment, string? userProfileKnownFolder)
    {
        var root = new[] { userProfileEnvironment, userProfileKnownFolder }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value));
        if (root is null) throw new InvalidOperationException("The Windows profile settings folder is not available. StandDesk will not use a different settings location.");
        // AppData can be virtualized when launched by a packaged host such as Codex.
        // A profile-root folder is shared by that process and ordinary Windows login launches.
        return Path.Combine(root, ".standdesk", "settings.json");
    }
    public async Task<DeskSettings> LoadAsync(bool requireExisting = false)
    {
        if (!File.Exists(FilePath))
        {
            if (requireExisting) throw new FileNotFoundException("Saved StandDesk settings are not available yet. Movement remains disabled; no settings were reset.", FilePath);
            return new DeskSettings();
        }
        var settings = JsonSerializer.Deserialize<DeskSettings>(await File.ReadAllTextAsync(FilePath), JsonOptions)
            ?? throw new InvalidDataException("Empty settings file.");
        settings.Validate();
        return settings;
    }
    public async Task SaveAsync(DeskSettings settings)
    {
        settings.Validate();
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
