using Microsoft.Data.Sqlite;

public sealed class Baza(string connectionString)
{
    public async Task<SqliteConnection> OdpriAsync(
        CancellationToken ct = default
    )
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task InicializirajAsync()
    {
        // SQLite sam ustvari datoteko, mape pa ne
        var pot = new SqliteConnectionStringBuilder(connectionString)
            .DataSource;
        Directory.CreateDirectory(
            Path.GetDirectoryName(Path.GetFullPath(pot))!
        );

        await using var connection = await OdpriAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS uporabnik (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              ime TEXT NOT NULL,
              priimek TEXT NOT NULL,
              email TEXT NOT NULL UNIQUE COLLATE NOCASE,
              geslo TEXT NOT NULL,
              slika TEXT,
              vloga TEXT NOT NULL
                CHECK (vloga IN ('organizator', 'vodja', 'tekmovalec')),
              zeton_gesla TEXT,
              zeton_velja_do TEXT
            );
            """;
        await command.ExecuteNonQueryAsync();
    }
}