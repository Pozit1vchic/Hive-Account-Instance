using System.IO;
using System.Text.Json;
namespace Hive.App;

public sealed class AppPreferences
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? StoragePath
    {
        get; set;
    }
    public bool DarkTheme
    {
        get; set;
    }
    public string Accent { get; set; } = "Honey";
    public bool AnimationsEnabled { get; set; } = true;
    public bool CompactRows
    {
        get; set;
    }
    public string BrowserPath { get; set; } = "";
    public bool ShowPasswords
    {
        get; set;
    }
    public bool AutoRenewSessions { get; set; } = true;
    public bool AutoCheckSessions { get; set; } = true;
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive", "preferences.json");
    public static AppPreferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch (IOException) { return new(); }
        catch (JsonException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        var path = StoragePath ?? SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}
