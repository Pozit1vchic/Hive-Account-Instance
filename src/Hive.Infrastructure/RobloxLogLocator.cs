using System.Text.RegularExpressions;
namespace Hive.Infrastructure;

public sealed class RobloxLogLocator(string? directory = null, Func<string, IReadOnlyList<FileProcessOwner>>? owners = null)
{
    private readonly string logs = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
    private readonly Func<string, IReadOnlyList<FileProcessOwner>> getOwners = owners ?? RestartManagerFileOwners.GetOwners;
    public string? Find(int processId, DateTime startedUtc)
    {
        if (processId <= 0 || !Directory.Exists(logs)) return null;
        try
        {
            var candidates = new DirectoryInfo(logs).EnumerateFiles("*_Player_*_last.log")
                .Where(f => Regex.IsMatch(f.Name, @"_Player_[0-9A-Fa-f]+_last\.log$", RegexOptions.CultureInvariant) && f.LastWriteTimeUtc >= startedUtc.AddMinutes(-2))
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(16).ToArray();
            string? match = null;
            foreach (var file in candidates)
            {
                if (!getOwners(file.FullName).Any(owner => owner.ProcessId == processId && Math.Abs((owner.StartedUtc - startedUtc).TotalSeconds) < 1)) continue;
                if (match is not null) return null;
                match = file.FullName;
            }
            return match;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
