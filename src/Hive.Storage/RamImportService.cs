using Dapper;
using System.Text;
using System.Text.Json;
using Hive.Core;
namespace Hive.Storage;

public sealed record RamImportItem(string Username, string Alias, string Description, string? Password, string? Cookie);
public sealed record RamImportSummary(int Added, int Skipped);
public sealed class RamImportService(HiveDatabase database, ISecretProtector protector)
{
    public static IReadOnlyList<RamImportItem> Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("Choose a decrypted RAM account array.");
        if (document.RootElement.GetArrayLength() > 5000)
            throw new FormatException("Import up to 5,000 accounts at a time.");
        var result = new List<RamImportItem>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                throw new FormatException("Every account entry must be an object.");
            string Read(string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            var username = Read("Username").Trim();
            if (username.Length is < 1 or > 64 || !names.Add(username))
                continue;
            var alias = Read("Alias");
            var description = Read("Description");
            var password = Read("Password");
            var cookie = Read("SecurityToken");
            if (alias.Length > 128 || description.Length > 4000 || password.Length > 5000 || cookie.Length > 16384)
                throw new FormatException("An imported field exceeds the supported length.");
            result.Add(new(username, alias, description, password.Length == 0 ? null : password, cookie.Length == 0 ? null : cookie));
        }
        return result;
    }
    public async Task<RamImportSummary> ImportAsync(IReadOnlyList<RamImportItem> items, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var existing = (await connection.QueryAsync<string>(new CommandDefinition("SELECT Username FROM Accounts", transaction: transaction, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var skipped = 0;
        byte[] Encrypt(byte[] clear)
        {
            try
            {
                return protector.Protect(clear);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(clear); }
        }
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (!existing.Add(item.Username))
            {
                skipped++;
                continue;
            }
            var id = Guid.NewGuid().ToString("N");
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO Accounts(Id,Username,DisplayName,Notes,SessionState) VALUES(@id,@Username,@Alias,@Description,0)", new
            {
                id,
                item.Username,
                item.Alias,
                item.Description
            }, transaction: transaction, cancellationToken: ct));
            if (item.Password is not null)
            {
                var payload = Encrypt(Encoding.UTF8.GetBytes(item.Password));
                await connection.ExecuteAsync(new CommandDefinition("INSERT INTO Credentials(AccountId,Payload) VALUES(@id,@payload)", new
                {
                    id,
                    payload
                }, transaction: transaction, cancellationToken: ct));
            }
            if (item.Cookie is not null)
            {
                var session = new AccountSession([new(".ROBLOSECURITY", item.Cookie, ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow);
                var payload = Encrypt(JsonSerializer.SerializeToUtf8Bytes(session));
                await connection.ExecuteAsync(new CommandDefinition("INSERT INTO Sessions(AccountId,Payload) VALUES(@id,@payload)", new
                {
                    id,
                    payload
                }, transaction: transaction, cancellationToken: ct));
            }
            added++;
        }
        await transaction.CommitAsync(ct);
        return new(added, skipped);
    }
}
