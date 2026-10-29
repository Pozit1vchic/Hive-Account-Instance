using Dapper;
using Microsoft.Data.Sqlite;
namespace Hive.Storage;

public sealed class HiveDatabase(string path)
{
    public string DatabasePath { get; } = Path.GetFullPath(path);
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, DefaultTimeout = 15 }.ToString());
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async Task BackupAsync(string destination, CancellationToken ct = default)
    {
        if (Path.GetFullPath(destination).Equals(DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a different backup file.");
        await using var source = await OpenAsync(ct);
        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(destination) }.ToString());
        await target.OpenAsync(ct);
        source.BackupDatabase(target);
    }
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("PRAGMA journal_mode=WAL;", cancellationToken: ct));
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY);", transaction: transaction, cancellationToken: ct));
        var version = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COALESCE(MAX(Version),0) FROM SchemaMigrations", transaction: transaction, cancellationToken: ct));
        if (version < 1)
            await connection.ExecuteAsync(new CommandDefinition("""
   CREATE TABLE AccountGroups (Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE);
   CREATE TABLE Accounts (Id TEXT PRIMARY KEY, Username TEXT NOT NULL COLLATE NOCASE UNIQUE, DisplayName TEXT NOT NULL, GroupId TEXT REFERENCES AccountGroups(Id) ON DELETE SET NULL, Notes TEXT NOT NULL, SessionState INTEGER NOT NULL DEFAULT 0);
   CREATE TABLE Sessions (AccountId TEXT PRIMARY KEY REFERENCES Accounts(Id) ON DELETE CASCADE, Payload BLOB NOT NULL);
   CREATE TABLE BrowserProfiles (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, DirectoryPath TEXT NOT NULL);
   INSERT INTO SchemaMigrations(Version) VALUES(1);
   """, transaction: transaction, cancellationToken: ct));
        if (version < 2)
            await connection.ExecuteAsync(new CommandDefinition("""
   CREATE TABLE Credentials (AccountId TEXT PRIMARY KEY REFERENCES Accounts(Id) ON DELETE CASCADE, Payload BLOB NOT NULL);
   INSERT INTO SchemaMigrations(Version) VALUES(2);
   """, transaction: transaction, cancellationToken: ct));
        if (version < 3)
            await connection.ExecuteAsync(new CommandDefinition("""
   ALTER TABLE Accounts ADD COLUMN RobloxUserId INTEGER NULL;
   CREATE UNIQUE INDEX IX_Accounts_RobloxUserId ON Accounts(RobloxUserId) WHERE RobloxUserId IS NOT NULL;
   INSERT INTO SchemaMigrations(Version) VALUES(3);
   """, transaction: transaction, cancellationToken: ct));
        if (version < 4)
            await connection.ExecuteAsync(new CommandDefinition("""
   ALTER TABLE BrowserProfiles ADD COLUMN AccountId TEXT NULL REFERENCES Accounts(Id) ON DELETE CASCADE;
   ALTER TABLE BrowserProfiles ADD COLUMN LastUsedAt TEXT NULL;
   INSERT INTO SchemaMigrations(Version) VALUES(4);
   """, transaction: transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
    }
}
