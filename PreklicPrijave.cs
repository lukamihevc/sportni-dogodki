using System.Globalization;
using System.Security.Claims;
using Microsoft.Data.Sqlite;

public static class PreklicPrijave
{
    public static IEndpointRouteBuilder MapPreklicPrijave(
        this IEndpointRouteBuilder app
    )
    {
        app.MapDelete(
                "/api/tekmovanja/{tekmovanjeId:long}/prijave/{prijavaId:long}",
                PrekliciAsync
            )
            .RequireAuthorization(p => p.RequireRole("vodja"));
        return app;
    }

    internal static async Task<IResult> PrekliciAsync(
        long tekmovanjeId,
        long prijavaId,
        ClaimsPrincipal user,
        Baza baza,
        CancellationToken ct
    )
    {
        var vodjaId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await using var connection = await baza.OdpriAsync(ct);
        await using var transakcija = (SqliteTransaction)
            await connection.BeginTransactionAsync(ct);

        SqliteCommand Ukaz(string sql)
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = transakcija;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@prijava", prijavaId);
            cmd.Parameters.AddWithValue("@tekmovanje", tekmovanjeId);
            return cmd;
        }

        // 1. Prijava mora obstajati in pripadati temu tekmovanju
        long lastnikEkipe;
        string nazivEkipe;
        string nazivTekmovanja;
        DateTimeOffset rokPrijav;
        long organizatorId;
        await using (var cmd = Ukaz("""
            SELECT e.vodja_id, e.naziv, t.naziv, t.rok_prijav, t.organizator_id
            FROM prijava_ekipe p
            JOIN ekipa e ON e.id = p.ekipa_id
            JOIN tekmovanje t ON t.id = p.tekmovanje_id
            WHERE p.id = @prijava AND p.tekmovanje_id = @tekmovanje;
            """))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return Napaka(StatusCodes.Status404NotFound, "Prijava ne obstaja.");

            lastnikEkipe = reader.GetInt64(0);
            nazivEkipe = reader.GetString(1);
            nazivTekmovanja = reader.GetString(2);
            rokPrijav = DateTimeOffset.Parse(
                reader.GetString(3),
                CultureInfo.InvariantCulture
            );
            organizatorId = reader.GetInt64(4);
        }

        // 2. Preklice lahko samo vodja ekipe
        if (lastnikEkipe != vodjaId)
            return Napaka(
                StatusCodes.Status403Forbidden,
                "Prekličete lahko samo prijavo ekipe, ki jo vodite."
            );

        // 3. Preklic je mogoč le do roka prijav
        if (DateTimeOffset.UtcNow > rokPrijav)
            return Napaka(
                StatusCodes.Status409Conflict,
                "Rok prijav je potekel, preklic ni več mogoč."
            );

        // 4. Izbris prijave sprosti mesto na tekmovanju
        await using (var cmd = Ukaz("DELETE FROM prijava_ekipe WHERE id = @prijava;"))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 5. Obvestilo organizatorju
        await using (var cmd = Ukaz("""
            INSERT INTO obvestilo (uporabnik_id, vsebina, ustvarjeno)
            VALUES (@organizator, @vsebina, @ustvarjeno);
            """))
        {
            cmd.Parameters.AddWithValue("@organizator", organizatorId);
            cmd.Parameters.AddWithValue(
                "@vsebina",
                $"Ekipa {nazivEkipe} je preklicala prijavo na tekmovanje {nazivTekmovanja}."
            );
            cmd.Parameters.AddWithValue("@ustvarjeno", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await transakcija.CommitAsync(ct);

        return Results.Ok(new { sporocilo = "Prijava je bila preklicana." });
    }

    private static IResult Napaka(int status, string sporocilo) =>
        Results.Problem(detail: sporocilo, statusCode: status);
}
