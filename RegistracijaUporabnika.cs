using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

public record RegistracijaZahteva(
    string? Ime,
    string? Priimek,
    string? Email,
    string? Geslo,
    string? PotrditevGesla
);

public record RegistracijaOdgovor(
    long Id,
    string Ime,
    string Priimek,
    string Email,
    string Vloga
);

public static partial class RegistracijaUporabnika
{
    private const int MinDolzinaGesla = 8;
    private const string PrivzetaVloga = "tekmovalec";

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    public static IEndpointRouteBuilder MapRegistracijaUporabnika(
        this IEndpointRouteBuilder app
    )
    {
        app.MapPost("/api/uporabniki/registracija", RegistrirajAsync);
        return app;
    }

    internal static async Task<IResult> RegistrirajAsync(
        RegistracijaZahteva zahteva,
        Baza baza,
        ILogger<Program> logger,
        CancellationToken ct
    )
    {
        var napake = Validiraj(zahteva);
        if (napake.Count > 0)
        {
            return Results.ValidationProblem(napake);
        }

        var ime = zahteva.Ime!.Trim();
        var priimek = zahteva.Priimek!.Trim();
        var email = zahteva.Email!.Trim().ToLowerInvariant();

        try
        {
            await using var connection = await baza.OdpriAsync(ct);
            await using var vnos = connection.CreateCommand();
            vnos.CommandText = """
                INSERT INTO uporabnik (ime, priimek, email, geslo, vloga)
                VALUES (@ime, @priimek, @email, @geslo, @vloga)
                RETURNING id;
                """;
            vnos.Parameters.AddWithValue("@ime", ime);
            vnos.Parameters.AddWithValue("@priimek", priimek);
            vnos.Parameters.AddWithValue("@email", email);
            vnos.Parameters.AddWithValue(
                "@geslo",
                GesloHasher.Zgosti(zahteva.Geslo!)
            );
            vnos.Parameters.AddWithValue("@vloga", PrivzetaVloga);

            var id = Convert.ToInt64(await vnos.ExecuteScalarAsync(ct));

            return Results.Created(
                $"/api/uporabniki/{id}",
                new RegistracijaOdgovor(id, ime, priimek, email, PrivzetaVloga)
            );
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            // 19 = SQLITE_CONSTRAINT (UNIQUE na email)
            return Results.Problem(
                detail: "Uporabnik s to e-pošto je že registriran.",
                statusCode: StatusCodes.Status409Conflict
            );
        }
        catch (SqliteException ex)
        {
            logger.LogError(ex, "Registracija uporabnika ni uspela.");
            return Results.Problem(
                detail: "Registracija trenutno ni mogoča.",
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }
    }

    internal static Dictionary<string, string[]> Validiraj(
        RegistracijaZahteva z
    )
    {
        var napake = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(z.Ime))
            napake["ime"] = ["Ime je obvezno."];
        else if (z.Ime.Trim().Length > 100)
            napake["ime"] = ["Ime je lahko dolgo največ 100 znakov."];

        if (string.IsNullOrWhiteSpace(z.Priimek))
            napake["priimek"] = ["Priimek je obvezen."];
        else if (z.Priimek.Trim().Length > 100)
            napake["priimek"] = ["Priimek je lahko dolg največ 100 znakov."];

        if (string.IsNullOrWhiteSpace(z.Email))
            napake["email"] = ["E-pošta je obvezna."];
        else if (
            z.Email.Trim().Length > 255 || !EmailRegex().IsMatch(z.Email.Trim())
        )
            napake["email"] = ["Neveljaven format e-pošte."];

        if (string.IsNullOrEmpty(z.Geslo))
            napake["geslo"] = ["Geslo je obvezno."];
        else if (z.Geslo.Length < MinDolzinaGesla)
            napake["geslo"] =
            [
                $"Geslo mora imeti vsaj {MinDolzinaGesla} znakov.",
            ];

        if (z.Geslo != z.PotrditevGesla)
            napake["potrditevGesla"] = ["Gesli se ne ujemata."];

        return napake;
    }
}

public static class GesloHasher
{
    private const int SaltVelikost = 16;
    private const int HashVelikost = 32;
    private const int Iteracije = 100_000;

    public static string Zgosti(string geslo)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltVelikost);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            geslo,
            salt,
            Iteracije,
            HashAlgorithmName.SHA256,
            HashVelikost
        );
        return $"{Iteracije}.{Convert.ToBase64String(salt)}."
            + Convert.ToBase64String(hash);
    }

    public static bool Preveri(string geslo, string shranjeno)
    {
        var deli = shranjeno.Split('.');
        if (deli.Length != 3)
            return false;

        var iteracije = int.Parse(deli[0]);
        var salt = Convert.FromBase64String(deli[1]);
        var hash = Convert.FromBase64String(deli[2]);

        var izracunan = Rfc2898DeriveBytes.Pbkdf2(
            geslo,
            salt,
            iteracije,
            HashAlgorithmName.SHA256,
            hash.Length
        );
        return CryptographicOperations.FixedTimeEquals(izracunan, hash);
    }
}