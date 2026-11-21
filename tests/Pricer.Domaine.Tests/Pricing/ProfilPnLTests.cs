using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class ProfilPnLTests
{
    /// <summary>
    /// The number, order, and boundaries of the scenarios proposed in the first display are
    /// checked.
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
    /// The overall limits and zeros of the result are checked with the funding of crypto bonus.
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
    /// It is checked that the USD and crypto terminals follow the bonus agreement instead of
    /// being deducted from the payoff alone.
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
    /// We are checking the loss terminal of a call sold with a cryptographic premium.
    /// </summary>
    [TestMethod]
    public void Calculer_CallVenduSeul_PerteIllimitee()
    {
        var etat = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.CreerOptionSimple(TypeOption.Call, MarcheDeTest.E1, 76000, etat.Referentiel), Sens.Vente, 1, etat);
        Assert.IsNull(ProfilPnL.Calculer(c, etat).PerteMaxUsd);
    }

    /// <summary>
    /// We check that all zeros of the result are found between the payoff branches.
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
    /// It is verified that the absence of a market shock returns to the net theoretical value
    /// of the premium executed.
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
    /// The conversion of the payoff to crypto is checked before the deduction of the premium.
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
