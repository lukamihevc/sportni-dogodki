using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SportniDogodki.Tests;

[TestClass]
public class GesloHasherTests
{
    [TestMethod]
    public void Test_Zgosti_Format()
    {
        var hash = GesloHasher.Zgosti("geslo1234");

        var deli = hash.Split('.');
        Assert.AreEqual(3, deli.Length);
        Assert.AreEqual("100000", deli[0]);
    }

    [TestMethod]
    public void Test_Zgosti_RazlicnaSol()
    {
        Assert.AreNotEqual(
            GesloHasher.Zgosti("geslo1234"),
            GesloHasher.Zgosti("geslo1234")
        );
    }

    [TestMethod]
    public void Test_Preveri_PravilnoGeslo()
    {
        var hash = GesloHasher.Zgosti("geslo1234");

        Assert.IsTrue(GesloHasher.Preveri("geslo1234", hash));
    }

    [TestMethod]
    public void Test_Preveri_NapacnoGeslo()
    {
        var hash = GesloHasher.Zgosti("geslo1234");

        Assert.IsFalse(GesloHasher.Preveri("napacno99", hash));
    }

    [TestMethod]
    public void Test_Preveri_NeveljavenFormat()
    {
        Assert.IsFalse(GesloHasher.Preveri("geslo1234", "neveljavno"));
    }
}