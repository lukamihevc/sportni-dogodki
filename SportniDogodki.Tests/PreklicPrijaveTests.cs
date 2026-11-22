using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static SportniDogodki.Tests.PrijavaEkipeTests;

namespace SportniDogodki.Tests;

[TestClass]
public class PreklicPrijaveTests
{
    private string _pot = null!;
    private Baza _baza = null!;

    [TestInitialize]
    public async Task Pripravi()
    {
        _pot = Path.Combine(Path.GetTempPath(), $"preklic_{Guid.NewGuid()}.db");
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

    private static ClaimsPrincipal Uporabnik(long id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));

    private Task<IResult> Preklici(long tekmovanjeId, long prijavaId, long vodjaId) =>
        PreklicPrijave.PrekliciAsync(
            tekmovanjeId,
            prijavaId,
            Uporabnik(vodjaId),
            _baza,
            CancellationToken.None
        );

    private static int? Status(IResult r) => ((IStatusCodeHttpResult)r).StatusCode;

    private static string? Sporocilo(IResult r) => ((ProblemHttpResult)r).ProblemDetails.Detail;

    private async Task<long> SteviloAsync(string sql, long id)
    {
        await using var conn = await _baza.OdpriAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private Task<long> SteviloPrijavAsync(long prijavaId) =>
        SteviloAsync("SELECT COUNT(*) FROM prijava_ekipe WHERE id = @id;", prijavaId);

    private Task<long> SteviloObvestilAsync(long uporabnikId) =>
        SteviloAsync("SELECT COUNT(*) FROM obvestilo WHERE uporabnik_id = @id;", uporabnikId);

    private record Scenarij(long Vodja, long Ekipa, long Organizator, long Tekmovanje, long Prijava);

    private async Task<Scenarij> PripraviPrijavoAsync(DateTimeOffset rokPrijav, int maxEkip = 8)
    {
        var vodja = await DodajUporabnikaAsync(_baza);
        var ekipa = await DodajEkipoAsync(_baza, vodja);
        var organizator = await DodajUporabnikaAsync(_baza, "organizator");
        var tekmovanje = await DodajTekmovanjeAsync(
            _baza,
            maxEkip: maxEkip,
            rokPrijav: rokPrijav,
            organizatorId: organizator
        );
        var prijava = await DodajPrijavoAsync(_baza, ekipa, tekmovanje);
        return new Scenarij(vodja, ekipa, organizator, tekmovanje, prijava);
    }

    // ---------- Pogoji zadovoljstva UZ-22 ----------

    [TestMethod]
    public async Task Test_Preklic_PredRokomPrijav_Uspe()
    {
        var s = await PripraviPrijavoAsync(DateTimeOffset.UtcNow.AddDays(3));

        var rezultat = await Preklici(s.Tekmovanje, s.Prijava, s.Vodja);

        Assert.AreEqual(200, Status(rezultat));
        Assert.AreEqual(0, await SteviloPrijavAsync(s.Prijava));
    }

    [TestMethod]
    public async Task Test_Preklic_PoRokuPrijav_Zavrnjeno()
    {
        var s = await PripraviPrijavoAsync(DateTimeOffset.UtcNow.AddDays(-1));

        var rezultat = await Preklici(s.Tekmovanje, s.Prijava, s.Vodja);

        Assert.AreEqual(409, Status(rezultat));
        Assert.AreEqual("Rok prijav je potekel, preklic ni več mogoč.", Sporocilo(rezultat));
        Assert.AreEqual(1, await SteviloPrijavAsync(s.Prijava));
        Assert.AreEqual(0, await SteviloObvestilAsync(s.Organizator));
    }

    [TestMethod]
    public async Task Test_Preklic_OrganizatorPrejmeObvestilo()
    {
        var s = await PripraviPrijavoAsync(DateTimeOffset.UtcNow.AddDays(3));

        await Preklici(s.Tekmovanje, s.Prijava, s.Vodja);

        var rezultat = await Obvestila.SeznamAsync(
            Uporabnik(s.Organizator),
            _baza,
            CancellationToken.None
        );
        var obvestila = (List<ObvestiloOdgovor>)((IValueHttpResult)rezultat).Value!;
        Assert.AreEqual(1, obvestila.Count);
        Assert.AreEqual(
            "Ekipa Ekipa je preklicala prijavo na tekmovanje Turnir.",
            obvestila[0].Vsebina
        );
        Assert.IsFalse(obvestila[0].Prebrano);

        // Vodja ekipe obvestila ne dobi
        Assert.AreEqual(0, await SteviloObvestilAsync(s.Vodja));
    }

    // ---------- Robni primeri ----------

    [TestMethod]
    public async Task Test_Preklic_SprostiMesto()
    {
        var s = await PripraviPrijavoAsync(DateTimeOffset.UtcNow.AddDays(3), maxEkip: 1);
        var drugVodja = await DodajUporabnikaAsync(_baza);
        var drugaEkipa = await DodajEkipoAsync(_baza, drugVodja);

        await Preklici(s.Tekmovanje, s.Prijava, s.Vodja);
        var prijava = await PrijavaEkipe.PrijaviAsync(
            s.Tekmovanje,
            new PrijavaEkipeZahteva(drugaEkipa),
            Uporabnik(drugVodja),
            _baza,
            CancellationToken.None
        );

        Assert.AreEqual(201, Status(prijava));
    }

    [TestMethod]
    public async Task Test_Preklic_NeobstojecaPrijava_Vrne404()
    {
        var s = await PripraviPrijavoAsync(DateTimeOffset.UtcNow.AddDays(3));

        var rezultat = await Preklici(s.Tekmovanje, 999, s.Vodja);

        Assert.AreEqual(404, Status(rezultat));
        Assert.AreEqual("Prijava ne obstaja.", Sporocilo(rezultat));
    }

    [TestMethod]
    public async Task Test_Preklic_PrijavaDrugegaTekmovanja_Vrne404()
    {
        var s = await PripraviPrijavoAsync(DateTimeOffset.UtcNow.AddDays(3));
        var drugoTekmovanje = await DodajTekmovanjeAsync(_baza);

        var rezultat = await Preklici(drugoTekmovanje, s.Prijava, s.Vodja);

        Assert.AreEqual(404, Status(rezultat));
        Assert.AreEqual(1, await SteviloPrijavAsync(s.Prijava));
    }

    [TestMethod]
    public async Task Test_Preklic_TujaPrijava_Vrne403()
    {
        var s = await PripraviPrijavoAsync(DateTimeOffset.UtcNow.AddDays(3));
        var drugVodja = await DodajUporabnikaAsync(_baza);

        var rezultat = await Preklici(s.Tekmovanje, s.Prijava, drugVodja);

        Assert.AreEqual(403, Status(rezultat));
        Assert.AreEqual(1, await SteviloPrijavAsync(s.Prijava));
        Assert.AreEqual(0, await SteviloObvestilAsync(s.Organizator));
    }

    [TestMethod]
    public void Test_MapPreklicPrijaveInObvestila()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new Baza("Data Source=:memory:"));
        var app = builder.Build();

        Assert.AreSame(app, app.MapPreklicPrijave());
        Assert.AreSame(app, app.MapObvestila());

        var poti = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        CollectionAssert.Contains(
            poti,
            "/api/tekmovanja/{tekmovanjeId:long}/prijave/{prijavaId:long}"
        );
        CollectionAssert.Contains(poti, "/api/obvestila");
    }

    // ---------- Celoten HTTP pipeline ----------

    [TestMethod]
    public async Task Test_Http_PreklicInObvestiloOrganizatorju()
    {
        await using var factory = UstvariAplikacijo(_pot);
        var (vodjaClient, vodja) = await PrijavljenClientAsync(factory, _baza, "vodja");
        var (orgClient, organizator) = await PrijavljenClientAsync(factory, _baza, "organizator");
        using var _v = vodjaClient;
        using var _o = orgClient;

        var ekipa = await DodajEkipoAsync(_baza, vodja);
        var tekmovanje = await DodajTekmovanjeAsync(_baza, organizatorId: organizator);
        var prijava = await DodajPrijavoAsync(_baza, ekipa, tekmovanje);

        var odgovor = await vodjaClient.DeleteAsync(
            $"/api/tekmovanja/{tekmovanje}/prijave/{prijava}"
        );
        Assert.AreEqual(HttpStatusCode.OK, odgovor.StatusCode);

        var obvestila = await orgClient.GetFromJsonAsync<List<ObvestiloOdgovor>>(
            "/api/obvestila"
        );
        Assert.AreEqual(1, obvestila!.Count);
        StringAssert.Contains(obvestila[0].Vsebina, "preklicala prijavo");
    }

    [TestMethod]
    public async Task Test_Http_TekmovalecNeMorePreklicati_Vrne403()
    {
        await using var factory = UstvariAplikacijo(_pot);
        var (client, _) = await PrijavljenClientAsync(factory, _baza, "tekmovalec");
        using var _c = client;

        var odgovor = await client.DeleteAsync("/api/tekmovanja/1/prijave/1");

        Assert.AreEqual(HttpStatusCode.Forbidden, odgovor.StatusCode);
    }

    [TestMethod]
    public async Task Test_Http_ObvestilaBrezPrijave_Vrne401()
    {
        await using var factory = UstvariAplikacijo(_pot);
        using var client = factory.CreateClient();

        var odgovor = await client.GetAsync("/api/obvestila");

        Assert.AreEqual(HttpStatusCode.Unauthorized, odgovor.StatusCode);
    }
}
