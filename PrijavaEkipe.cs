using System.Security.Claims;
using Microsoft.Data.Sqlite;

public record PrijavaEkipeZahteva(long? EkipaId);

public record PrijavaEkipeOdgovor(
    long Id,
    long EkipaId,
    long TekmovanjeId,
    string Status,
    string DatumPrijave
);

public static class PrijavaEkipe
{
    public const string StatusVObdelavi = "V obdelavi";
    public const string StatusZavrnjena = "Zavrnjena";
    private const string TekmovanjeOdprto = "odprto";

    public static IEndpointRouteBuilder MapPrijavaEkipe(
        this IEndpointRouteBuilder app
    )
    {
        app.MapPost("/api/tekmovanja/{tekmovanjeId:long}/prijave", PrijaviAsync)
            .RequireAuthorization(p => p.RequireRole("vodja"));
        return app;
    }

    internal static async Task<IResult> PrijaviAsync(
        long tekmovanjeId,
        PrijavaEkipeZahteva zahteva,
        ClaimsPrincipal user,
        Baza baza,
        CancellationToken ct
    )
    {
        if (zahteva.EkipaId is not > 0)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["ekipaId"] = ["Ekipa je obvezna."],
                }
            );
        }

        var ekipaId = zahteva.EkipaId.Value;
        var vodjaId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await using var connection = await baza.OdpriAsync(ct);
        // BEGIN IMMEDIATE: preverjanje zasedenosti in vnos sta atomarna,
        // zato dve socasni prijavi ne moreta zapolniti istega zadnjega mesta.
        await using var transakcija = (SqliteTransaction)
            await connection.BeginTransactionAsync(ct);

        SqliteCommand Ukaz(string sql)
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = transakcija;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@tekmovanje", tekmovanjeId);
            cmd.Parameters.AddWithValue("@ekipa", ekipaId);
            return cmd;
        }

        // 1. Tekmovanje mora obstajati
        string statusTekmovanja;
        long maxEkip;
        await using (var cmd = Ukaz(
            "SELECT status, max_ekip FROM tekmovanje WHERE id = @tekmovanje;"
        ))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return Napaka(StatusCodes.Status404NotFound, "Tekmovanje ne obstaja.");

            statusTekmovanja = reader.GetString(0);
            maxEkip = reader.GetInt64(1);
        }

        // 2. Ekipa mora obstajati in pripadati prijavljenemu vodji
        await using (var cmd = Ukaz("SELECT vodja_id FROM ekipa WHERE id = @ekipa;"))
        {
            var vodja = await cmd.ExecuteScalarAsync(ct);
            if (vodja is null)
                return Napaka(StatusCodes.Status404NotFound, "Ekipa ne obstaja.");

            if (Convert.ToInt64(vodja) != vodjaId)
                return Napaka(
                    StatusCodes.Status403Forbidden,
                    "Prijavite lahko samo ekipo, ki jo vodite."
                );
        }

        // 3. Ista ekipa se ne sme prijaviti dvakrat
        await using (var cmd = Ukaz("""
            SELECT COUNT(*) FROM prijava_ekipe
            WHERE ekipa_id = @ekipa AND tekmovanje_id = @tekmovanje;
            """))
        {
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0)
                return Napaka(
                    StatusCodes.Status409Conflict,
                    "Ekipa je na to tekmovanje že prijavljena."
                );
        }

        // 4. Prijave morajo biti odprte
        if (statusTekmovanja != TekmovanjeOdprto)
            return Napaka(
                StatusCodes.Status409Conflict,
                "Prijave na to tekmovanje niso odprte."
            );

        // 5. Tekmovanje ne sme biti zasedeno (zavrnjene prijave ne zasedajo mesta)
        await using (var cmd = Ukaz("""
            SELECT COUNT(*) FROM prijava_ekipe
            WHERE tekmovanje_id = @tekmovanje AND status <> @zavrnjena;
            """))
        {
            cmd.Parameters.AddWithValue("@zavrnjena", StatusZavrnjena);
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) >= maxEkip)
                return Napaka(StatusCodes.Status409Conflict, "Tekmovanje je zasedeno.");
        }

        // 6. Vnos prijave s statusom "V obdelavi"
        var datum = DateTime.UtcNow.ToString("O");
        long id;
        await using (var cmd = Ukaz("""
            INSERT INTO prijava_ekipe (ekipa_id, tekmovanje_id, status, datum_prijave)
            VALUES (@ekipa, @tekmovanje, @status, @datum)
            RETURNING id;
            """))
        {
            cmd.Parameters.AddWithValue("@status", StatusVObdelavi);
            cmd.Parameters.AddWithValue("@datum", datum);
            id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }

        await transakcija.CommitAsync(ct);

        return Results.Created(
            $"/api/tekmovanja/{tekmovanjeId}/prijave/{id}",
            new PrijavaEkipeOdgovor(id, ekipaId, tekmovanjeId, StatusVObdelavi, datum)
        );
    }

    private static IResult Napaka(int status, string sporocilo) =>
        Results.Problem(detail: sporocilo, statusCode: status);
}
