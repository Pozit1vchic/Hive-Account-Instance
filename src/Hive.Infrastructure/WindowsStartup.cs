using Microsoft.Win32;

namespace Hive.Infrastructure;

public sealed class WindowsStartup(string? registryPath = null, string? executablePath = null)
{
    private const string ValueName = "Hive";
    private readonly string keyPath = registryPath ?? @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string executable = executablePath ?? Path.Combine(AppContext.BaseDirectory, "Hive.App.exe");
    private string Command => $"\"{executable}\"";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true) ?? throw new InvalidOperationException("Could not open Windows startup settings.");
        var current = key.GetValue(ValueName) as string;
        if (enabled)
        {
            if (!File.Exists(executable)) throw new InvalidOperationException("Hive executable was not found.");
            if (current is not null && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Another Hive installation is already registered for startup.");
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else if (string.Equals(current, Command, StringComparison.OrdinalIgnoreCase)) key.DeleteValue(ValueName, false);
    }
}
