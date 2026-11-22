using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace SportniDogodki.Tests;

[TestClass]
public class ProgramTests
{
    private string _dbPot = null!;

    [TestInitialize]
    public void Pripravi()
    {
        _dbPot = Path.Combine(Path.GetTempPath(), $"prog_test_{Guid.NewGuid()}.db");
    }

    [TestCleanup]
    public void Pocisti()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPot))
            File.Delete(_dbPot);
    }

    private WebApplicationFactory<Program> UstvariAplikacijo(
        string okolje = "Development",
        bool dodajNapakoEndpoint = false
    )
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(okolje);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPot}",
                    }
                );
            });

            if (dodajNapakoEndpoint)
            {
                builder.Configure(app =>
                {
                    // Ta konfiguracija se izvede pred MapGet v Program.cs le, če uporabiš IStartupFilter,
                    // zato spodaj uporabimo IStartupFilter za simulacijo izjeme na strežniku.
                });
            }
        });
    }

    [TestMethod]
public async Task Test_Program_OsnovnePotiInRegistracija()
{
    await using var factory = UstvariAplikacijo("Development");
    using var client = factory.CreateClient();

    // 1. Preveri GET /
    var rootOdgovor = await client.GetAsync("/");
    Assert.AreEqual(HttpStatusCode.OK, rootOdgovor.StatusCode);
    var rootBesedilo = await rootOdgovor.Content.ReadAsStringAsync();
    StringAssert.Contains(rootBesedilo, "Sportni dogodki API");

    // 2. Preveri GET /health
    var healthOdgovor = await client.GetAsync("/health");
    Assert.AreEqual(HttpStatusCode.OK, healthOdgovor.StatusCode);
    Assert.AreEqual("\"OK\"", await healthOdgovor.Content.ReadAsStringAsync());

    // 3. Preveri POST /api/uporabniki/registracija skozi celoten HTTP pipeline
    var email = $"ana_{Guid.NewGuid():N}@primer.si";
    var zahteva = new RegistracijaZahteva(
        "Ana",
        "Kovač",
        email,
        "geslo1234",
        "geslo1234"
    );
    var regOdgovor = await client.PostAsJsonAsync(
        "/api/uporabniki/registracija",
        zahteva
    );
    Assert.AreEqual(HttpStatusCode.Created, regOdgovor.StatusCode);
}

    [TestMethod]
    public async Task Test_Program_ProdukcijaInGlobalniExceptionHandler()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(
            builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:DefaultConnection"] =
                                $"Data Source={_dbPot}",
                        }
                    );
                });
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupFilter, IzjemaStartupFilter>();
                });
            }
        );

        using var client = factory.CreateClient();

        // Sproži izjemo, da se izvede blok znotraj app.UseExceptionHandler(...)
        var odgovor = await client.GetAsync("/sprozi-napako");
        Assert.AreEqual(HttpStatusCode.InternalServerError, odgovor.StatusCode);

        var vsebina = await odgovor.Content.ReadAsStringAsync();
        StringAssert.Contains(vsebina, "Prislo je do napake na strezniku.");
    }

    [TestMethod]
public void Test_Program_ManjkaDefaultConnection()
{
    var praznaMapa = Path.Combine(Path.GetTempPath(), $"prazna_konf_{Guid.NewGuid()}");
    Directory.CreateDirectory(praznaMapa);

    try
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(
                builder =>
                {
                    builder.UseContentRoot(praznaMapa);
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.Sources.Clear();
                    });
                }
            );
            _ = factory.CreateClient();
        });

        StringAssert.Contains(ex.Message, "Manjka povezovalni niz DefaultConnection.");
    }
    finally
    {
        Directory.Delete(praznaMapa, true);
    }
}
    private sealed class IzjemaStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);
                app.Map(
                    "/sprozi-napako",
                    branch =>
                        branch.Run(_ => throw new InvalidOperationException("Testna napaka"))
                );
            };
        }
    }
}