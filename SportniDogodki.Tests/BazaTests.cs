using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SportniDogodki.Tests;

[TestClass]
public class BazaTests
{
    [TestMethod]
    public async Task Test_InicializirajAsync_UstvariMapoInTabelo()
    {
        var mapa = Path.Combine(Path.GetTempPath(), $"mapa_{Guid.NewGuid()}");
        var baza = new Baza($"Data Source={Path.Combine(mapa, "t.db")}");

        await baza.InicializirajAsync();
        await baza.InicializirajAsync(); // drugi klic ne sme pasti

        Assert.IsTrue(Directory.Exists(mapa));
        await using (var conn = await baza.OdpriAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE name = 'uporabnik';";
            Assert.AreEqual("uporabnik", await cmd.ExecuteScalarAsync());
        }

        SqliteConnection.ClearAllPools();
        Directory.Delete(mapa, true);
    }
}