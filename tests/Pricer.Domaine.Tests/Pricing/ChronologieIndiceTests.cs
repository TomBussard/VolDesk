using Pricer.Domaine.Modeles;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class ChronologieIndiceTests
{
    /// <summary>
    /// It is verified that a delayed REST response does not reverse the received flow index.
    /// </summary>
    [TestMethod]
    public void Indice_UneAncienneReponseRestNeRemplacePasLeFluxRecent()
    {
        var etat = MarcheDeTest.Etat();
        var date = etat.Maintenant;
        var nom = MarcheDeTest.Nom(MarcheDeTest.E1, 74000, TypeOption.Call);
        etat.MettreAJour(etat.Cotation(nom)! with { Horodatage = date.AddSeconds(3), Indice = 75000 });
        etat.DefinirIndice(73000, date);
        etat.MettreAJour(etat.Cotation(nom)! with { Horodatage = date, Indice = 72000, Future = 72000 });
        Assert.AreEqual(75000, etat.Indice);
        Assert.AreNotEqual(72000, etat.Cotation(nom)!.Future);
        Assert.AreEqual(date.AddSeconds(3), etat.Maintenant);
    }
}
