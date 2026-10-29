using Dapper;
using Hive.Core;
namespace Hive.Storage;

public sealed class AccountGroupRepository(HiveDatabase database) : IAccountGroupRepository
{
    public async Task<IReadOnlyList<AccountGroup>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        return (await connection.QueryAsync<AccountGroup>(new CommandDefinition("SELECT Id,Name FROM AccountGroups ORDER BY Name COLLATE NOCASE", cancellationToken: ct))).ToList();
    }
    public async Task SaveAsync(AccountGroup group, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 64)
            throw new ArgumentException("Enter a group name up to 64 characters.");
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO AccountGroups(Id,Name) VALUES(@Id,@Name) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name", group, cancellationToken: ct));
    }
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM AccountGroups WHERE Id=@id", new
        {
            id
        }, cancellationToken: ct));
    }
}
