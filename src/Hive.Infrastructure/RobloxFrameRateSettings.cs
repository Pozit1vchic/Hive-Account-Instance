using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;

namespace Hive.Infrastructure;

public sealed class RobloxFrameRateSettings(string? settingsPath = null, Func<bool>? gameRunning = null)
{
    private readonly string path = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "GlobalBasicSettings_13.xml");
    private bool IsRunning()
    {
        if (gameRunning is not null) return gameRunning();
        var processes = Process.GetProcessesByName("RobloxPlayerBeta");
        try { return processes.Length != 0; } finally { foreach (var process in processes) process.Dispose(); }
    }
    private (XDocument Document, XElement Value) Load()
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Roblox settings were not found. Open Roblox once first.");
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Roblox settings file is unexpectedly large.");
        XDocument document;
        using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var elements = document.Descendants("int").Where(x => (string?)x.Attribute("name") == "FramerateCap").ToArray();
        if (elements.Length != 1) throw new InvalidDataException("Roblox frame rate setting was not found or is ambiguous.");
        return (document, elements[0]);
    }
    public int Read()
    {
        var (_, value) = Load();
        if (!int.TryParse(value.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var fps)) throw new InvalidDataException("Roblox frame rate setting is invalid.");
        return fps;
    }
    public string Apply(int fps)
    {
        if (fps != -1 && (fps < 30 || fps > 240)) throw new ArgumentOutOfRangeException(nameof(fps), "Use -1 for Roblox default or a value from 30 to 240.");
        if (IsRunning()) throw new InvalidOperationException("Close Roblox before changing its frame rate setting.");
        var (document, value) = Load();
        value.Value = fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var temp = path + ".hive-" + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = path + ".hive-" + Guid.NewGuid().ToString("N") + ".backup";
        try
        {
            document.Save(temp, SaveOptions.DisableFormatting);
            if (IsRunning()) throw new InvalidOperationException("Close Roblox before changing its frame rate setting.");
            File.Replace(temp, path, backup);
            return backup;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
