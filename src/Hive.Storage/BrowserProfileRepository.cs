using Dapper;
using Hive.Core;
using System.Globalization;
namespace Hive.Storage;

public sealed class BrowserProfileRepository(HiveDatabase database) : IBrowserProfileRepository
{
    public async Task<IReadOnlyList<BrowserProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("SELECT Id,Name,DirectoryPath,AccountId,LastUsedAt FROM BrowserProfiles ORDER BY LastUsedAt DESC,Name", cancellationToken: ct));
        return rows.Select(r => new BrowserProfile(r.Id, r.Name, r.DirectoryPath, r.AccountId, DateTimeOffset.TryParse(r.LastUsedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null)).ToArray();
    }
    public async Task SaveAsync(BrowserProfile profile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 64) throw new ArgumentException("Enter a profile name up to 64 characters.");
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO BrowserProfiles(Id,Name,DirectoryPath,AccountId,LastUsedAt) VALUES(@Id,@Name,@DirectoryPath,@AccountId,@At) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name,DirectoryPath=excluded.DirectoryPath,AccountId=excluded.AccountId,LastUsedAt=excluded.LastUsedAt", new { profile.Id, profile.Name, profile.DirectoryPath, profile.AccountId, At = profile.LastUsedAt?.ToString("O") }, cancellationToken: ct));
    }
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM BrowserProfiles WHERE Id=@id", new { id }, cancellationToken: ct));
    }
    private sealed class Row
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string DirectoryPath { get; set; } = "";
        public string? AccountId { get; set; }
        public string? LastUsedAt { get; set; }
    }
}
