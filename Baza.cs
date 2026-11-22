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

            CREATE TABLE IF NOT EXISTS ekipa (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              naziv TEXT NOT NULL,
              vodja_id INTEGER NOT NULL REFERENCES uporabnik(id)
            );

            CREATE TABLE IF NOT EXISTS tekmovanje (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              naziv TEXT NOT NULL,
              max_ekip INTEGER NOT NULL CHECK (max_ekip > 0),
              status TEXT NOT NULL DEFAULT 'odprto'
                CHECK (status IN ('odprto', 'zaprto')),
              rok_prijav TEXT NOT NULL,
              organizator_id INTEGER NOT NULL REFERENCES uporabnik(id)
            );

            CREATE TABLE IF NOT EXISTS prijava_ekipe (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              ekipa_id INTEGER NOT NULL REFERENCES ekipa(id),
              tekmovanje_id INTEGER NOT NULL REFERENCES tekmovanje(id),
              status TEXT NOT NULL
                CHECK (status IN ('V obdelavi', 'Potrjena', 'Zavrnjena')),
              datum_prijave TEXT NOT NULL,
              UNIQUE (ekipa_id, tekmovanje_id)
            );

            CREATE TABLE IF NOT EXISTS obvestilo (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              uporabnik_id INTEGER NOT NULL REFERENCES uporabnik(id),
              vsebina TEXT NOT NULL,
              ustvarjeno TEXT NOT NULL,
              prebrano INTEGER NOT NULL DEFAULT 0
            );
            """;
        await command.ExecuteNonQueryAsync();
    }
}