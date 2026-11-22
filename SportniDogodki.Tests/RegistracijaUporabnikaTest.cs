using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace SportniDogodki.Tests;

[TestClass]
public class RegistracijaUporabnikaTests
{
    private string _pot = null!;
    private Baza _baza = null!;

    [TestInitialize]
    public async Task Pripravi()
    {
        _pot = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _baza = new Baza($"Data Source={_pot}");
        await _baza.InicializirajAsync();
    }

    [TestCleanup]
    public void Pocisti()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_pot);
    }

    private static RegistracijaZahteva Veljavna(
        string email = "janez@primer.si"
    ) => new("Janez", "Novak", email, "geslo1234", "geslo1234");

    private static Task<IResult> Registriraj(
        RegistracijaZahteva z,
        Baza baza
    ) =>
        RegistracijaUporabnika.RegistrirajAsync(
            z,
            baza,
            NullLogger<Program>.Instance,
            CancellationToken.None
        );

    private static int? Status(IResult r) =>
        ((IStatusCodeHttpResult)r).StatusCode;

    // ---------- Pogoji zadovoljstva UZ-1 ----------

    [TestMethod]
    public async Task Test_RegistrirajAsync_VeljavniPodatki()
    {
        var rezultat = await Registriraj(Veljavna(), _baza);

        Assert.AreEqual(201, Status(rezultat));
        var odgovor = (RegistracijaOdgovor)((IValueHttpResult)rezultat).Value!;
        Assert.AreEqual(1, odgovor.Id);
        Assert.AreEqual("Janez", odgovor.Ime);
        Assert.AreEqual("Novak", odgovor.Priimek);
        Assert.AreEqual("janez@primer.si", odgovor.Email);
        Assert.AreEqual("tekmovalec", odgovor.Vloga);
    }

    [TestMethod]
    public async Task Test_RegistrirajAsync_GesloShranjenoZgosceno()
    {
        await Registriraj(Veljavna(), _baza);

        await using var conn = await _baza.OdpriAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT geslo FROM uporabnik WHERE id = 1;";
        var shranjeno = (string)(await cmd.ExecuteScalarAsync())!;

        Assert.AreNotEqual("geslo1234", shranjeno);
        Assert.IsTrue(GesloHasher.Preveri("geslo1234", shranjeno));
    }

    [TestMethod]
    public async Task Test_RegistrirajAsync_EmailZeRegistriran()
    {
        await Registriraj(Veljavna(), _baza);

        var rezultat = await Registriraj(Veljavna(), _baza);

        Assert.AreEqual(409, Status(rezultat));
    }

    [TestMethod]
    public async Task Test_RegistrirajAsync_EmailZeRegistriranVelikeCrke()
    {
        await Registriraj(Veljavna(), _baza);

        var rezultat = await Registriraj(Veljavna("JANEZ@Primer.si"), _baza);

        Assert.AreEqual(409, Status(rezultat));
    }

    [TestMethod]
    public async Task Test_RegistrirajAsync_NeveljavenFormatEmaila()
    {
        var rezultat = await Registriraj(Veljavna("janez.primer.si"), _baza);

        Assert.AreEqual(400, Status(rezultat));
    }

    [TestMethod]
    public async Task Test_RegistrirajAsync_GesloKrajseOd8Znakov()
    {
        var z = Veljavna() with { Geslo = "kratko", PotrditevGesla = "kratko" };

        var rezultat = await Registriraj(z, _baza);

        Assert.AreEqual(400, Status(rezultat));
    }

    [TestMethod]
    public async Task Test_RegistrirajAsync_GesliSeNeUjemata()
    {
        var z = Veljavna() with { PotrditevGesla = "geslo9999" };

        var rezultat = await Registriraj(z, _baza);

        Assert.AreEqual(400, Status(rezultat));
    }

    [TestMethod]
    public async Task Test_RegistrirajAsync_NapakaBaze()
    {
        // Baza brez tabele uporabnik -> napaka SQLite
        var pot = Path.Combine(Path.GetTempPath(), $"prazna_{Guid.NewGuid()}.db");
        var prazna = new Baza($"Data Source={pot}");

        var rezultat = await Registriraj(Veljavna(), prazna);

        Assert.AreEqual(503, Status(rezultat));
        SqliteConnection.ClearAllPools();
        File.Delete(pot);
    }

    // ---------- Validacija ----------

    [TestMethod]
    public void Test_Validiraj_VeljavniPodatki()
    {
        var napake = RegistracijaUporabnika.Validiraj(Veljavna());

        Assert.AreEqual(0, napake.Count);
    }

    [TestMethod]
    public void Test_Validiraj_PraznaPolja()
    {
        var napake = RegistracijaUporabnika.Validiraj(
            new RegistracijaZahteva(null, " ", "", null, null)
        );

        Assert.IsTrue(napake.ContainsKey("ime"));
        Assert.IsTrue(napake.ContainsKey("priimek"));
        Assert.IsTrue(napake.ContainsKey("email"));
        Assert.IsTrue(napake.ContainsKey("geslo"));
        Assert.IsFalse(napake.ContainsKey("potrditevGesla"));
    }

    [TestMethod]
    public void Test_Validiraj_PredolgaPolja()
    {
        var dolgo = new string('a', 101);
        var dolgEmail = new string('a', 251) + "@a.si";

        var napake = RegistracijaUporabnika.Validiraj(
            new RegistracijaZahteva(
                dolgo,
                dolgo,
                dolgEmail,
                "geslo1234",
                "geslo1234"
            )
        );

        Assert.AreEqual(3, napake.Count);
        Assert.IsTrue(napake.ContainsKey("ime"));
        Assert.IsTrue(napake.ContainsKey("priimek"));
        Assert.AreEqual("Neveljaven format e-pošte.", napake["email"][0]);
    }

    [TestMethod]
    public void Test_Validiraj_NeveljavenEmail()
    {
        var napake = RegistracijaUporabnika.Validiraj(Veljavna("ana@primer"));

        Assert.AreEqual("Neveljaven format e-pošte.", napake["email"][0]);
    }

    [TestMethod]
    public void Test_Validiraj_KratkoGeslo()
    {
        var napake = RegistracijaUporabnika.Validiraj(
            Veljavna() with { Geslo = "1234567", PotrditevGesla = "1234567" }
        );

        Assert.AreEqual(
            "Geslo mora imeti vsaj 8 znakov.",
            napake["geslo"][0]
        );
    }

    [TestMethod]
    public void Test_Validiraj_GesliSeNeUjemata()
    {
        var napake = RegistracijaUporabnika.Validiraj(
            Veljavna() with { PotrditevGesla = "drugo123" }
        );

        Assert.AreEqual("Gesli se ne ujemata.", napake["potrditevGesla"][0]);
    }

    // ---------- Registracija endpointa ----------

    [TestMethod]
    public void Test_MapRegistracijaUporabnika()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(new Baza("Data Source=:memory:"));
        var app = builder.Build();

        var vrnjeno = app.MapRegistracijaUporabnika();

        Assert.AreSame(app, vrnjeno);
        var poti = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText);
        CollectionAssert.Contains(
            poti.ToList(),
            "/api/uporabniki/registracija"
        );
    }

    [TestMethod]
public async Task Test_RegistrirajAsync_DrugaNapakaSqlite()
{
    var pot = Path.Combine(Path.GetTempPath(), $"napaka_sqlite_{Guid.NewGuid()}.db");
    var baza = new Baza($"Data Source={pot}");

    await using (var conn = await baza.OdpriAsync())
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = """
            CREATE TABLE uporabnik (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ime TEXT NOT NULL,
                priimek TEXT NOT NULL,
                email TEXT NOT NULL,
                geslo TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    try
    {
        var rezultat = await Registriraj(Veljavna(), baza);
        Assert.AreEqual(503, Status(rezultat));
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(pot))
            File.Delete(pot);
    }
}

[TestMethod]
public async Task Test_RegistrirajAsync_PreklicanToken()
{
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();

    await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () =>
    {
        await RegistracijaUporabnika.RegistrirajAsync(
            Veljavna(),
            _baza,
            NullLogger<Program>.Instance,
            cts.Token
        );
    });
}

}