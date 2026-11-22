using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SportniDogodki.Tests;

[TestClass]
public class AvtentikacijaTests
{
    private string _dbPot = null!;

    [TestInitialize]
    public void Pripravi()
    {
        _dbPot = Path.Combine(Path.GetTempPath(), $"auth_test_{Guid.NewGuid()}.db");
    }

    [TestCleanup]
    public void Pocisti()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPot))
            File.Delete(_dbPot);
    }

    private WebApplicationFactory<Program> UstvariAplikacijo()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPot}");
        });
    }

    [TestMethod]
    public async Task Test_Prijava_NeobstojecUporabnik_Vrne401()
    {
        await using var factory = UstvariAplikacijo();
        using var client = factory.CreateClient();

        var odgovor = await client.PostAsJsonAsync("/api/uporabniki/prijava", new PrijavaZahteva(
            "neobsta@primer.si",
            "geslo1234"
        ));

        Assert.AreEqual(HttpStatusCode.Unauthorized, odgovor.StatusCode);
    }

    [TestMethod]
    public async Task Test_Prijava_NapacnoGeslo_Vrne401()
    {
        await using var factory = UstvariAplikacijo();
        using var client = factory.CreateClient();

        // 1. Registriraj uporabnika
        var email = $"uporabnik_{Guid.NewGuid():N}@primer.si";
        var regOdgovor = await client.PostAsJsonAsync("/api/uporabniki/registracija", new RegistracijaZahteva(
            "Janez", "Novak", email, "pravilnoGeslo1", "pravilnoGeslo1"
        ));
        Assert.AreEqual(HttpStatusCode.Created, regOdgovor.StatusCode);

        // 2. Poizkus prijave z napačnim geslom
        var odgovor = await client.PostAsJsonAsync("/api/uporabniki/prijava", new PrijavaZahteva(
            email,
            "napacnoGeslo"
        ));

        Assert.AreEqual(HttpStatusCode.Unauthorized, odgovor.StatusCode);
    }

    [TestMethod]
    public async Task Test_Prijava_PravilniPodatki_PreusmeriNaDomacoStran()
    {
        await using var factory = UstvariAplikacijo();
        // Izklop samodejnega sledenja preusmeritvam, da lahko preverimo 302 Redirect
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var email = $"uporabnik_{Guid.NewGuid():N}@primer.si";
        var regOdgovor = await client.PostAsJsonAsync("/api/uporabniki/registracija", new RegistracijaZahteva(
            "Maja", "Kralj", email, "geslo12345", "geslo12345"
        ));
        Assert.AreEqual(HttpStatusCode.Created, regOdgovor.StatusCode);

        var odgovor = await client.PostAsJsonAsync("/api/uporabniki/prijava", new PrijavaZahteva(
            email,
            "geslo12345"
        ));

        Assert.AreEqual(HttpStatusCode.Redirect, odgovor.StatusCode);
        Assert.AreEqual("/", odgovor.Headers.Location?.OriginalString);
        Assert.IsTrue(odgovor.Headers.Contains("Set-Cookie"));
    }

    [TestMethod]
    public async Task Test_ZascitenaStran_PoOdjaviNiVecDostopna()
    {
        await using var factory = UstvariAplikacijo();
        using var client = factory.CreateClient(); // Ta client ohranja piškotke

        var email = $"uporabnik_{Guid.NewGuid():N}_{Guid.NewGuid():N}@primer.si";

        // 0. Registracija res uspe (201 Created)
        var regOdgovor = await client.PostAsJsonAsync("/api/uporabniki/registracija", new RegistracijaZahteva(
            "Luka", "Horvat", email, "geslo12345", "geslo12345"
        ));
        Assert.AreEqual(HttpStatusCode.Created, regOdgovor.StatusCode);

        // 1. Prijava
        var prijavaOdgovor = await client.PostAsJsonAsync("/api/uporabniki/prijava", new PrijavaZahteva(
            email,
            "geslo12345"
        ));
        Assert.AreEqual(HttpStatusCode.OK, prijavaOdgovor.StatusCode); // Pri clientu z avtomatskim redirectom pristane na / (200 OK)

        // 2. Dostop do zaščitene poti uspe
        var trenutniOdgovor = await client.GetAsync("/api/uporabniki/trenutni");
        Assert.AreEqual(HttpStatusCode.OK, trenutniOdgovor.StatusCode);

        // 3. Odjava
        var odjavaOdgovor = await client.PostAsync("/api/uporabniki/odjava", null);
        Assert.AreEqual(HttpStatusCode.OK, odjavaOdgovor.StatusCode);

        // 4. Zaščitena pot po odjavi ni več dostopna (vrne 401 Unauthorized)
        var znovaTrenutni = await client.GetAsync("/api/uporabniki/trenutni");
        Assert.AreEqual(HttpStatusCode.Unauthorized, znovaTrenutni.StatusCode);
    }

    [TestMethod]
    public async Task Test_Prijava_PrazniPodatki_Vrne400()
    {
        await using var factory = UstvariAplikacijo();
        using var client = factory.CreateClient();

        var odgovor = await client.PostAsJsonAsync(
            "/api/uporabniki/prijava",
            new PrijavaZahteva("", "")
        );

        Assert.AreEqual(HttpStatusCode.BadRequest, odgovor.StatusCode);
    }

    [TestMethod]
    public async Task Test_Trenutni_BrezPrijave_Vrne401()
    {
        await using var factory = UstvariAplikacijo();
        using var client = factory.CreateClient();

        var odgovor = await client.GetAsync("/api/uporabniki/trenutni");

        Assert.AreEqual(HttpStatusCode.Unauthorized, odgovor.StatusCode);
    }

    [TestMethod]
public async Task Test_Prijava_NapacnoGeslo_ZaObstojecRacun_Vrne401()
{
    await using var factory = UstvariAplikacijo();
    using var client = factory.CreateClient();

    var email = $"napacno_{Guid.NewGuid():N}_{Guid.NewGuid():N}@primer.si";
    var regOdgovor = await client.PostAsJsonAsync(
        "/api/uporabniki/registracija",
        new RegistracijaZahteva("Miha", "Kovač", email, "pravilnoGeslo123", "pravilnoGeslo123")
    );
    Assert.AreEqual(HttpStatusCode.Created, regOdgovor.StatusCode);

    var odgovor = await client.PostAsJsonAsync(
        "/api/uporabniki/prijava",
        new PrijavaZahteva(email, "drugoGeslo999")
    );

    Assert.AreEqual(HttpStatusCode.Unauthorized, odgovor.StatusCode);
}

[TestMethod]
public async Task Test_Prijava_VeljavenEmailPraznoGeslo_Vrne400()
{
    await using var factory = UstvariAplikacijo();
    using var client = factory.CreateClient();

    var odgovor = await client.PostAsJsonAsync(
        "/api/uporabniki/prijava",
        new PrijavaZahteva("test@primer.si", "")
    );

    Assert.AreEqual(HttpStatusCode.BadRequest, odgovor.StatusCode);
}

}