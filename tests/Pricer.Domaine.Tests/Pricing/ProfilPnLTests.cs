using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class ProfilPnLTests
{
    /// <summary>
    /// On vérifie le nombre, l'ordre et les bornes des scénarios proposés au premier affichage.
    /// </summary>
    [TestMethod]
    public void Calculer_GrilleParDefaut_13PointsDeMoins30APlus30()
    {
        var etat = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel), Sens.Achat, 1, etat);

        var p = ProfilPnL.Calculer(c, etat);

        Assert.AreEqual(13, p.Points.Count);
        Assert.AreEqual(-0.30, p.Points[0].Variation, 1e-12);
        Assert.AreEqual(0.30, p.Points[^1].Variation, 1e-12);
        Assert.AreEqual(13, p.EnMatrice().GetLength(0));
    }

    /// <summary>
    /// On vérifie les limites globales et les zéros du résultat avec le financement de prime en crypto.
    /// </summary>
    [TestMethod]
    public void Calculer_CallSpreadAchete_ExtremesEtPointMortAnalytiques()
    {
        var etat = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel), Sens.Achat, 2, etat);

        var p = ProfilPnL.Calculer(c, etat);

        Assert.AreEqual(2 * 8000 - p.PrimeBtc * 80000, p.GainMaxUsd!.Value, 1e-6);
        Assert.IsNull(p.PerteMaxUsd, "En USD, la prime conservée en crypto peut coûter sans borne si le sous-jacent monte.");
        Assert.AreEqual(-p.PrimeBtc, p.PerteMaxBtc!.Value, 1e-12);
        Assert.AreEqual(2, p.PointsMorts.Count);
        Assert.AreEqual(72000 / (1 - p.PrimeBtc / 2), p.PointsMorts[0], 1e-6);
        Assert.AreEqual(16000 / p.PrimeBtc, p.PointsMorts[1], 1e-6);
    }

    /// <summary>
    /// On vérifie que les bornes USD et crypto suivent la convention de prime au lieu d'être déduites
    /// du seul payoff.
    /// </summary>
    [TestMethod]
    public void Calculer_CallSimpleAchete_GainIllimite()
    {
        var etat = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.CreerOptionSimple(TypeOption.Call, MarcheDeTest.E1, 76000, etat.Referentiel), Sens.Achat, 1, etat);

        var p = ProfilPnL.Calculer(c, etat);

        Assert.IsNull(p.GainMaxUsd);
        Assert.AreEqual(-p.PrimeBtc * 76000, p.PerteMaxUsd!.Value, 1e-6);
    }

    /// <summary>
    /// On vérifie la borne de perte d'un call vendu avec une prime conservée en crypto.
    /// </summary>
    [TestMethod]
    public void Calculer_CallVenduSeul_PerteIllimitee()
    {
        var etat = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.CreerOptionSimple(TypeOption.Call, MarcheDeTest.E1, 76000, etat.Referentiel), Sens.Vente, 1, etat);
        Assert.IsNull(ProfilPnL.Calculer(c, etat).PerteMaxUsd);
    }

    /// <summary>
    /// On vérifie que tous les zéros du résultat sont trouvés entre les branches du payoff.
    /// </summary>
    [TestMethod]
    public void Calculer_Butterfly_DeuxPointsMortsFinancementCrypto()
    {
        var etat = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.ButterflyCall, MarcheDeTest.E1, [70000, 74000, 78000], etat.Referentiel), Sens.Achat, 1, etat);

        var p = ProfilPnL.Calculer(c, etat);

        Assert.AreEqual(2, p.PointsMorts.Count);
        Assert.AreEqual(70000 / (1 - p.PrimeBtc), p.PointsMorts[0], 1e-6);
        Assert.AreEqual(78000 / (1 + p.PrimeBtc), p.PointsMorts[1], 1e-6);
        Assert.AreEqual(4000 - p.PrimeBtc * 74000, p.GainMaxUsd!.Value, 1e-6);
    }

    /// <summary>
    /// On vérifie que l'absence de choc de marché retrouve la valeur théorique nette de la prime
    /// exécutée.
    /// </summary>
    [TestMethod]
    public void Calculer_AujourdhuiAuFutureCourant_EgalValeurTheoriqueMoinsPrime()
    {
        var etat = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.PutSpread, MarcheDeTest.E2, [66000, 72000], etat.Referentiel), Sens.Achat, 1, etat);

        var p = ProfilPnL.Calculer(c, etat);
        var centre = p.Points.Single(x => Math.Abs(x.Variation) < 1e-12);

        Assert.AreEqual((c.TheoriqueUnitaireBtc - p.PrimeBtc) * MarcheDeTest.Indice, centre.PnlAujourdhuiUsd, 1e-6);
        Assert.IsTrue(centre.PnlAujourdhuiUsd < 0, "on paie la fourchette en traitant à l'ask");
    }

    /// <summary>
    /// On vérifie la conversion du payoff en crypto avant la déduction de la prime.
    /// </summary>
    [TestMethod]
    public void Calculer_PnlBtcEcheance_PayoffInverseMoinsPrimeBtc()
    {
        var etat = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.CreerOptionSimple(TypeOption.Put, MarcheDeTest.E1, 70000, etat.Referentiel), Sens.Achat, 1, etat);

        var p = ProfilPnL.Calculer(c, etat);
        var bas = p.Points[0];

        Assert.AreEqual(Math.Max(70000 - bas.SousJacent, 0) / bas.SousJacent - p.PrimeBtc, bas.PnlEcheanceBtc, 1e-12);
    }
}
