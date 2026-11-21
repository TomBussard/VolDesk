using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Tests.Outils;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class DiversTests
{
    /// <summary>
    /// It is checked that the rounding follows the decimal steps without adding a binary
    /// precision deviation.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.30000000000000004, 0.1, 0.3)]
    [DataRow(1.26, 0.1, 1.3)]
    [DataRow(-0.04, 0.1, 0.0)]
    [DataRow(123.0, 10.0, 120.0)]
    public void ArrondirAuPas_ArrondiDecimalExact(double valeur, double pas, double attendu)
    {
        Assert.AreEqual(attendu, valeur.ArrondirAuPas(pas), 1e-15);
    }

    /// <summary>
    /// The ACT/365 convention is checked with a known calendar interval.
    /// </summary>
    [TestMethod]
    public void Maturite_UnJourExact_Vaut1Sur365()
    {
        var t0 = new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(1 / 365.0, Maturite.EnAnnees(t0.AddDays(1), t0), 1e-15);
    }

    /// <summary>
    /// We check that all calculations share the same clock and that an old message doesn't push
    /// it back.
    /// </summary>
    [TestMethod]
    public void HorlogeMarche_Singleton_MemeInstanceEtNeReculeJamais()
    {
        var h = HorlogeMarche.Instance;
        h.Reinitialiser();
        var t = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        h.Synchroniser(t);
        h.Synchroniser(t.AddMinutes(-5));

        Assert.AreSame(h, HorlogeMarche.Instance);
        Assert.AreEqual(t, h.Maintenant);
        h.Reinitialiser();
    }

    private sealed class Espion : IObservateur<Cotation>
    {
        public List<string> Recues { get; } = new();
        /// <summary>
        /// The notification received is stored in order to be able to verify subscriptions to
        /// the market.
        /// </summary>
        public void Actualiser(Cotation donnee) => Recues.Add(donnee.NomInstrument);
    }

    /// <summary>
    /// It is verified that observers receive the quotes only during their subscription.
    /// </summary>
    [TestMethod]
    public void EtatMarche_Observer_NotifieLesAbonnesPuisPlusApresDesabonnement()
    {
        var etat = MarcheDeTest.Etat();
        var espion = new Espion();
        string nom = MarcheDeTest.Nom(MarcheDeTest.E1, 72000, TypeOption.Call);

        etat.AjouterObservateur(espion);
        etat.MettreAJour(etat.Cotation(nom)!);
        etat.SupprimerObservateur(espion);
        etat.MettreAJour(etat.Cotation(nom)!);

        CollectionAssert.AreEqual(new[] { nom }, espion.Recues);
    }

    /// <summary>
    /// It is verified that an incomplete update removes the old negotiable price instead of
    /// leaving it available.
    /// </summary>
    [TestMethod]
    public void EtatMarche_ResumeSansBid_EffaceAncienPrixExecutable()
    {
        var etat = MarcheDeTest.Etat();
        string nom = MarcheDeTest.Nom(MarcheDeTest.E1, 72000, TypeOption.Call);
        var complet = etat.Cotation(nom)!;

        etat.MettreAJour(new Cotation { NomInstrument = nom, Horodatage = complet.Horodatage.AddSeconds(1), Mark = 0.05, EstComplete = false });

        var fusion = etat.Cotation(nom)!;
        Assert.AreEqual(0.05, fusion.Mark);
        Assert.IsNull(fusion.Bid);
        Assert.IsFalse(fusion.FourchetteValide);
        Assert.IsNull(fusion.GrecquesDeribit);
        Assert.IsNull(fusion.Future);
    }

    /// <summary>
    /// It is checked that the instruments are stored in a stable order for menus and tables.
    /// </summary>
    [TestMethod]
    public void Referentiel_ParcoursParEcheancePuisParStrike_Trie()
    {
        var r = MarcheDeTest.Referentiel();
        CollectionAssert.AreEqual(new[] { MarcheDeTest.E1, MarcheDeTest.E2, MarcheDeTest.E3 }, r.Echeances.ToArray());
        var strikes = r.Strikes(MarcheDeTest.E2).Select(p => p.Strike).ToList();
        CollectionAssert.AreEqual(strikes.OrderBy(k => k).ToList(), strikes);
        Assert.AreEqual(16, strikes.Count);
        Assert.AreEqual("BTC-25DEC26", r.FuturePourEcheance(MarcheDeTest.E2).Nom);
    }

    /// <summary>
    /// Check the signed aggregation used to move from legs to the structure and then to the
    /// book.
    /// </summary>
    [TestMethod]
    public void Grecques_Operateurs_SommePondereeParLesQuantites()
    {
        var g = new Grecques(0.5, 1e-5, 20, -10);
        var s = 2 * g + (-1.0) * g;
        Assert.AreEqual(g, s);
        Assert.AreEqual(Grecques.Zero, g - g);
    }
}
