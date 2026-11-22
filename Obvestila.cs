using System.Security.Claims;

public record ObvestiloOdgovor(long Id, string Vsebina, string Ustvarjeno, bool Prebrano);

public static class Obvestila
{
    public static IEndpointRouteBuilder MapObvestila(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/obvestila", SeznamAsync).RequireAuthorization();
        return app;
    }

    internal static async Task<IResult> SeznamAsync(
        ClaimsPrincipal user,
        Baza baza,
        CancellationToken ct
    )
    {
        var uporabnikId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await using var connection = await baza.OdpriAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, vsebina, ustvarjeno, prebrano
            FROM obvestilo
            WHERE uporabnik_id = @uporabnik
            ORDER BY id DESC;
            """;
        cmd.Parameters.AddWithValue("@uporabnik", uporabnikId);

        var seznam = new List<ObvestiloOdgovor>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            seznam.Add(new ObvestiloOdgovor(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3) != 0
            ));
        }

        return Results.Ok(seznam);
    }
}
