using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;

public record PrijavaZahteva(string? Email, string? Geslo);

public record TrenutniUporabnikOdgovor(
    long Id,
    string Ime,
    string Priimek,
    string Email,
    string Vloga
);

public static class AvtentikacijaUporabnika
{
    public static IEndpointRouteBuilder MapAvtentikacijaUporabnika(
        this IEndpointRouteBuilder app
    )
    {
        var skupina = app.MapGroup("/api/uporabniki");

        skupina.MapPost("/prijava", PrijavaAsync);
        skupina.MapPost("/odjava", (Delegate)OdjavaAsync).RequireAuthorization();
        skupina.MapGet("/trenutni", TrenutniAsync).RequireAuthorization();

        return app;
    }

    internal static async Task<IResult> PrijavaAsync(
        PrijavaZahteva zahteva,
        HttpContext httpContext,
        Baza baza,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(zahteva.Email) || string.IsNullOrWhiteSpace(zahteva.Geslo))
        {
            return Results.BadRequest(new { sporocilo = "E-pošta in geslo sta obvezna." });
        }

        var email = zahteva.Email.Trim().ToLowerInvariant();

        long id;
        string ime;
        string priimek;
        string gesloHash;
        string vloga;

        await using (var connection = await baza.OdpriAsync(ct))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, ime, priimek, geslo, vloga
                FROM uporabnik
                WHERE email = @email;
                """;
            command.Parameters.AddWithValue("@email", email);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Uporabnik ne obstaja
                return Results.Problem(
                    detail: "Napačna e-pošta ali geslo.",
                    statusCode: StatusCodes.Status401Unauthorized
                );
            }

            id = reader.GetInt64(0);
            ime = reader.GetString(1);
            priimek = reader.GetString(2);
            gesloHash = reader.GetString(3);
            vloga = reader.GetString(4);
        }

        if (!GesloHasher.Preveri(zahteva.Geslo, gesloHash))
        {
            // Napačno geslo
            return Results.Problem(
                detail: "Napačna e-pošta ali geslo.",
                statusCode: StatusCodes.Status401Unauthorized
            );
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.GivenName, ime),
            new(ClaimTypes.Surname, priimek),
            new(ClaimTypes.Role, vloga)
        };

        var claimsIdentity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme
        );

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties
        );

        // Preusmeritev na domačo stran po uspešni prijavi
        return Results.Redirect("/");
    }

    internal static async Task<IResult> OdjavaAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(new { sporocilo = "Uspešno ste se odjavili." });
    }

    internal static IResult TrenutniAsync(ClaimsPrincipal user)
    {
        var id = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var email = user.FindFirstValue(ClaimTypes.Email)!;
        var ime = user.FindFirstValue(ClaimTypes.GivenName)!;
        var priimek = user.FindFirstValue(ClaimTypes.Surname)!;
        var vloga = user.FindFirstValue(ClaimTypes.Role)!;

        return Results.Ok(new TrenutniUporabnikOdgovor(id, ime, priimek, email, vloga));
    }
}