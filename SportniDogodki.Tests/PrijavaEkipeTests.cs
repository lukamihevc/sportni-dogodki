using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SportniDogodki.Tests;

[TestClass]
public class PrijavaEkipeTests
{
    private string _pot = null!;
    private Baza _baza = null!;

    [TestInitialize]
    public async Task Pripravi()
    {
        _pot = Path.Combine(Path.GetTempPath(), $"prijava_ekipe_{Guid.NewGuid()}.db");
        _baza = new Baza($"Data Source={_pot}");
        await _baza.InicializirajAsync();
    }

    [TestCleanup]
    public void Pocisti()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_pot))
            File.Delete(_pot);
    }

    // ---------- Pomožne metode ----------

    private static async Task<long> VstaviAsync(
        Baza baza,
        string sql,
        params (string Ime, object Vrednost)[] parametri
    )
    {
        await using var conn = await baza.OdpriAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ime, vrednost) in parametri)
            cmd.Parameters.AddWithValue(ime, vrednost);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    internal static Task<long> DodajUporabnikaAsync(Baza baza, string vloga = "vodja") =>
        VstaviAsync(
            baza,
            """
            INSERT INTO uporabnik (ime, priimek, email, geslo, vloga)
            VALUES ('Test', 'Vodja', @email, 'x', @vloga) RETURNING id;
            """,
            ("@email", $"{Guid.NewGuid():N}@primer.si"),
            ("@vloga", vloga)
        );

    internal static Task<long> DodajEkipoAsync(Baza baza, long vodjaId) =>
        VstaviAsync(
            baza,
            "INSERT INTO ekipa (naziv, vodja_id) VALUES ('Ekipa', @vodja) RETURNING id;",
            ("@vodja", vodjaId)
        );

    internal static async Task<long> DodajTekmovanjeAsync(
        Baza baza,
        int maxEkip = 8,
        string status = "odprto",
        DateTimeOffset? rokPrijav = null,
        long? organizatorId = null
    ) =>
        await VstaviAsync(
            baza,
            """
            INSERT INTO tekmovanje (naziv, max_ekip, status, rok_prijav, organizator_id)
            VALUES ('Turnir', @max, @status, @rok, @organizator) RETURNING id;
            """,
            ("@max", maxEkip),
            ("@status", status),
            ("@rok", (rokPrijav ?? DateTimeOffset.UtcNow.AddDays(7)).ToString("O")),
            ("@organizator", organizatorId ?? await DodajUporabnikaAsync(baza, "organizator"))
        );

    internal static Task<long> DodajPrijavoAsync(
        Baza baza,
        long ekipaId,
        long tekmovanjeId,
        string status = PrijavaEkipe.StatusVObdelavi
    ) =>
        VstaviAsync(
            baza,
            """
            INSERT INTO prijava_ekipe (ekipa_id, tekmovanje_id, status, datum_prijave)
            VALUES (@e, @t, @s, '2026-01-01') RETURNING id;
            """,
            ("@e", ekipaId),
            ("@t", tekmovanjeId),
            ("@s", status)
        );

    private async Task<long> SteviloPrijavAsync(long tekmovanjeId)
    {
        await using var conn = await _baza.OdpriAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM prijava_ekipe WHERE tekmovanje_id = @t;";
        cmd.Parameters.AddWithValue("@t", tekmovanjeId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static ClaimsPrincipal Vodja(long id) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, "vodja")],
            "test"
        ));

    private Task<IResult> Prijavi(long tekmovanjeId, long? ekipaId, long vodjaId) =>
        PrijavaEkipe.PrijaviAsync(
            tekmovanjeId,
            new PrijavaEkipeZahteva(ekipaId),
            Vodja(vodjaId),
            _baza,
            CancellationToken.None
        );

    private static int? Status(IResult r) => ((IStatusCodeHttpResult)r).StatusCode;

    private static string? Sporocilo(IResult r) => ((ProblemHttpResult)r).ProblemDetails.Detail;

    // ---------- Pogoji zadovoljstva UZ-21 ----------

    [TestMethod]
    public async Task Test_Prijava_OdprtoTekmovanje_Uspe()
    {
        var vodja = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, vodja);
        var tekmovanje = await DodajTekmovanjeAsync(_baza);

        var rezultat = await Prijavi(tekmovanje, ekipa, vodja);

        Assert.AreEqual(201, Status(rezultat));
        var odgovor = (PrijavaEkipeOdgovor)((IValueHttpResult)rezultat).Value!;
        Assert.AreEqual(ekipa, odgovor.EkipaId);
        Assert.AreEqual(tekmovanje, odgovor.TekmovanjeId);
        Assert.AreEqual(1, await SteviloPrijavAsync(tekmovanje));
    }

    [TestMethod]
    public async Task Test_Prijava_ZasedenoTekmovanje_Vrne409()
    {
        var tekmovanje = await DodajTekmovanjeAsync(_baza, maxEkip: 1);
        var drugaEkipa = await DodajEkipoAsync(_baza, await DodajUporabnikaAsync(_baza));
        await DodajPrijavoAsync(_baza, drugaEkipa, tekmovanje, PrijavaEkipe.StatusVObdelavi);

        var vodja = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, vodja);

        var rezultat = await Prijavi(tekmovanje, ekipa, vodja);

        Assert.AreEqual(409, Status(rezultat));
        Assert.AreEqual("Tekmovanje je zasedeno.", Sporocilo(rezultat));
        Assert.AreEqual(1, await SteviloPrijavAsync(tekmovanje));
    }

    [TestMethod]
    public async Task Test_Prijava_PonovnaPrijavaIsteEkipe_Vrne409()
    {
        var vodja = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, vodja);
        var tekmovanje = await DodajTekmovanjeAsync(_baza);

        Assert.AreEqual(201, Status(await Prijavi(tekmovanje, ekipa, vodja)));
        var rezultat = await Prijavi(tekmovanje, ekipa, vodja);

        Assert.AreEqual(409, Status(rezultat));
        Assert.AreEqual("Ekipa je na to tekmovanje že prijavljena.", Sporocilo(rezultat));
        Assert.AreEqual(1, await SteviloPrijavAsync(tekmovanje));
    }

    [TestMethod]
    public async Task Test_Prijava_StatusJeVObdelavi()
    {
        var vodja = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, vodja);
        var tekmovanje = await DodajTekmovanjeAsync(_baza);

        var rezultat = await Prijavi(tekmovanje, ekipa, vodja);

        var odgovor = (PrijavaEkipeOdgovor)((IValueHttpResult)rezultat).Value!;
        Assert.AreEqual("V obdelavi", odgovor.Status);

        await using var conn = await _baza.OdpriAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM prijava_ekipe WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", odgovor.Id);
        Assert.AreEqual("V obdelavi", await cmd.ExecuteScalarAsync());
    }

    // ---------- Robni primeri ----------

    [TestMethod]
    public async Task Test_Prijava_ZavrnjenaPrijavaNeZasedaMesta()
    {
        var tekmovanje = await DodajTekmovanjeAsync(_baza, maxEkip: 1);
        var drugaEkipa = await DodajEkipoAsync(_baza, await DodajUporabnikaAsync(_baza));
        await DodajPrijavoAsync(_baza, drugaEkipa, tekmovanje, PrijavaEkipe.StatusZavrnjena);

        var vodja = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, vodja);

        Assert.AreEqual(201, Status(await Prijavi(tekmovanje, ekipa, vodja)));
    }

    [TestMethod]
    public async Task Test_Prijava_ZaprtoTekmovanje_Vrne409()
    {
        var vodja = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, vodja);
        var tekmovanje = await DodajTekmovanjeAsync(_baza, status: "zaprto");

        var rezultat = await Prijavi(tekmovanje, ekipa, vodja);

        Assert.AreEqual(409, Status(rezultat));
        Assert.AreEqual("Prijave na to tekmovanje niso odprte.", Sporocilo(rezultat));
    }

    [TestMethod]
    public async Task Test_Prijava_NeobstojeceTekmovanje_Vrne404()
    {
        var vodja = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, vodja);

        var rezultat = await Prijavi(999, ekipa, vodja);

        Assert.AreEqual(404, Status(rezultat));
        Assert.AreEqual("Tekmovanje ne obstaja.", Sporocilo(rezultat));
    }

    [TestMethod]
    public async Task Test_Prijava_NeobstojecaEkipa_Vrne404()
    {
        var vodja = await DodajUporabnikaAsync(_baza);
        var tekmovanje = await DodajTekmovanjeAsync(_baza);

        var rezultat = await Prijavi(tekmovanje, 999, vodja);

        Assert.AreEqual(404, Status(rezultat));
        Assert.AreEqual("Ekipa ne obstaja.", Sporocilo(rezultat));
    }

    [TestMethod]
    public async Task Test_Prijava_TujaEkipa_Vrne403()
    {
        var lastnik = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, lastnik);
        var drugVodja = await DodajUporabnikaAsync(_baza);
        var tekmovanje = await DodajTekmovanjeAsync(_baza);

        var rezultat = await Prijavi(tekmovanje, ekipa, drugVodja);

        Assert.AreEqual(403, Status(rezultat));
        Assert.AreEqual(0, await SteviloPrijavAsync(tekmovanje));
    }

    [TestMethod]
    public async Task Test_Prijava_ManjkaEkipa_Vrne400()
    {
        var tekmovanje = await DodajTekmovanjeAsync(_baza);

        Assert.AreEqual(400, Status(await Prijavi(tekmovanje, null, 1)));
        Assert.AreEqual(400, Status(await Prijavi(tekmovanje, 0, 1)));
    }

    [TestMethod]
    public void Test_MapPrijavaEkipe()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new Baza("Data Source=:memory:"));
        var app = builder.Build();

        var vrnjeno = app.MapPrijavaEkipe();

        Assert.AreSame(app, vrnjeno);
        var poti = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        CollectionAssert.Contains(poti, "/api/tekmovanja/{tekmovanjeId:long}/prijave");
    }

    // ---------- Celoten HTTP pipeline ----------

    internal static WebApplicationFactory<Program> UstvariAplikacijo(string pot) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={pot}");
        });

    internal static async Task<(HttpClient Client, long UporabnikId)> PrijavljenClientAsync(
        WebApplicationFactory<Program> factory,
        Baza baza,
        string vloga
    )
    {
        var client = factory.CreateClient();
        var email = $"vodja_{Guid.NewGuid():N}@primer.si";

        var reg = await client.PostAsJsonAsync(
            "/api/uporabniki/registracija",
            new RegistracijaZahteva("Ana", "Vodja", email, "geslo12345", "geslo12345")
        );
        Assert.AreEqual(HttpStatusCode.Created, reg.StatusCode);
        var id = (await reg.Content.ReadFromJsonAsync<RegistracijaOdgovor>())!.Id;

        // Vloga se nastavi neposredno v bazi (registracija vedno dodeli "tekmovalec")
        await using (var conn = await baza.OdpriAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE uporabnik SET vloga = @v WHERE id = @id;";
            cmd.Parameters.AddWithValue("@v", vloga);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        var prijava = await client.PostAsJsonAsync(
            "/api/uporabniki/prijava",
            new PrijavaZahteva(email, "geslo12345")
        );
        Assert.AreEqual(HttpStatusCode.OK, prijava.StatusCode);

        return (client, id);
    }

    [TestMethod]
    public async Task Test_Http_VodjaPrijaviEkipo_Vrne201()
    {
        await using var factory = UstvariAplikacijo(_pot);
        var (client, vodja) = await PrijavljenClientAsync(factory, _baza, "vodja");
        using var _ = client;
        var ekipa = await DodajEkipoAsync(_baza, vodja);
        var tekmovanje = await DodajTekmovanjeAsync(_baza);

        var odgovor = await client.PostAsJsonAsync(
            $"/api/tekmovanja/{tekmovanje}/prijave",
            new PrijavaEkipeZahteva(ekipa)
        );

        Assert.AreEqual(HttpStatusCode.Created, odgovor.StatusCode);
        var telo = await odgovor.Content.ReadFromJsonAsync<PrijavaEkipeOdgovor>();
        Assert.AreEqual("V obdelavi", telo!.Status);
        Assert.AreEqual(
            $"/api/tekmovanja/{tekmovanje}/prijave/{telo.Id}",
            odgovor.Headers.Location?.OriginalString
        );
    }

    [TestMethod]
    public async Task Test_Http_TekmovalecNeMorePrijaviti_Vrne403()
    {
        await using var factory = UstvariAplikacijo(_pot);
        var (client, uporabnik) = await PrijavljenClientAsync(factory, _baza, "tekmovalec");
        using var _ = client;
        var ekipa = await DodajEkipoAsync(_baza, uporabnik);
        var tekmovanje = await DodajTekmovanjeAsync(_baza);

        var odgovor = await client.PostAsJsonAsync(
            $"/api/tekmovanja/{tekmovanje}/prijave",
            new PrijavaEkipeZahteva(ekipa)
        );

        Assert.AreEqual(HttpStatusCode.Forbidden, odgovor.StatusCode);
    }

    [TestMethod]
    public async Task Test_Http_BrezPrijave_Vrne401()
    {
        await using var factory = UstvariAplikacijo(_pot);
        using var client = factory.CreateClient();

        var odgovor = await client.PostAsJsonAsync(
            "/api/tekmovanja/1/prijave",
            new PrijavaEkipeZahteva(1)
        );

        Assert.AreEqual(HttpStatusCode.Unauthorized, odgovor.StatusCode);
    }
}
