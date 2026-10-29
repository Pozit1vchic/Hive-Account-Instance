using System.IO;
using System.Text.Json;
namespace Hive.App;

public sealed class AppPreferences : Hive.Core.IGameWindowPreferences, Hive.Core.ILaunchPolicy
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? StoragePath
    {
        get; set;
    }
    public List<string> FavoriteAccounts { get; set; } = [];
    public List<SavedPlace> SavedPlaces { get; set; } = [];
    public string PlaceId { get; set; } = "";
    public string JobId { get; set; } = "";
    public Hive.Core.LaunchKind LaunchKind { get; set; }
    public string FollowUserId { get; set; } = "";
    public string ProtectedPrivateInvite { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string PrivateInvite { get => Hive.Infrastructure.ProtectedText.Read(ProtectedPrivateInvite); set => ProtectedPrivateInvite = Hive.Infrastructure.ProtectedText.Protect(value); }
    public int MaxParallelLaunches { get; set; } = 1;
    public int ParallelSignIns { get; set; } = 2;
    public double LaunchDelaySeconds { get; set; } = 3;
    public bool AutoRejoin { get; set; }
    public int MaxRejoinAttempts { get; set; } = 3;
    public double RejoinDelaySeconds { get; set; } = 5;
    public bool MultiInstance { get; set; }
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
    public bool CompactGameWindows { get; set; } = true;
    public int GameWindowWidth { get; set; } = 800;
    public int GameWindowHeight { get; set; } = 500;
    public bool AutoRenewSessions { get; set; } = true;
    public bool AutoCheckSessions { get; set; } = true;
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive", "preferences.json");
    public static AppPreferences Load(string? storagePath = null)
    {
        try
        {
            var preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(storagePath ?? SettingsPath)) ?? new();
            preferences.StoragePath = storagePath;
            return preferences;
        }
        catch (IOException) { return new() { StoragePath = storagePath }; }
        catch (JsonException) { return new() { StoragePath = storagePath }; }
        catch (UnauthorizedAccessException) { return new() { StoragePath = storagePath }; }
    }
    public void Save()
    {
        var path = StoragePath ?? SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}

public sealed record SavedPlace(string PlaceId, string JobId, Hive.Core.LaunchKind Kind = Hive.Core.LaunchKind.Place, string? ProtectedArgument = null);
