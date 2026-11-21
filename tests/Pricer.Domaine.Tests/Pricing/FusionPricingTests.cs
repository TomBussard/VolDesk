using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class FusionPricingTests
{
    /// <summary>
    /// It is verified that the shock-free scenario returns to the current rating for all
    /// proposed revenues.
    /// </summary>
    [DataTestMethod]
    [DataRow(TypeStructure.CallSpread)]
    [DataRow(TypeStructure.PutSpread)]
    [DataRow(TypeStructure.ButterflyCall)]
    [DataRow(TypeStructure.ButterflyPut)]
    [DataRow(TypeStructure.Straddle)]
    [DataRow(TypeStructure.Strangle)]
    [DataRow(TypeStructure.RiskReversal)]
    public void Profil_SeptStructures_ConservePrixEtGrecquesAuCentre(TypeStructure type)
    {
        var e = MarcheDeTest.Etat();
        double[] strikes = (type is TypeStructure.ButterflyCall or TypeStructure.ButterflyPut) ? new[] { 70000.0, 74000, 78000 } : type == TypeStructure.Straddle ? new[] { 74000.0 } : new[] { 70000.0, 78000 };
        var c = PricerStructure.Coter(FabriqueStructures.Creer(type, MarcheDeTest.E2, strikes, e.Referentiel), Sens.Achat, 2, e);
        var p = ProfilPnL.Calculer(c, e);
        var centre = p.Points.Single(x => Math.Abs(x.Variation) < 1e-12);
        Assert.AreEqual(c.TheoriqueUnitaireBtc * 2 - p.PrimeBtc, centre.PnlAujourdhuiBtc, 1e-12);
        Assert.AreEqual(c.Grecques.Delta, centre.Delta, 1e-10);
        Assert.AreEqual(c.Grecques.Vega, centre.Vega, 1e-8);
        foreach (var x in p.Points)
        {
            Assert.AreEqual(x.PnlEcheanceBtc * x.SousJacent, x.PnlEcheanceUsd, 1e-8);
            Assert.AreEqual(x.PnlAujourdhuiBtc * x.SousJacent, x.PnlAujourdhuiUsd, 1e-8);
        }
        foreach (double zero in p.PointsMorts)
            Assert.AreEqual(0, c.Lignes.Sum(l => l.QuantiteSignee * l.Jambe.PayoffUsd(zero)) / zero - p.PrimeBtc, 1e-10);
    }

    /// <summary>
    /// It is checked that changing the direction of the structure inverses its sensitivities
    /// throughout the grid.
    /// </summary>
    [TestMethod]
    public void Profil_Vente_InverseDeltaEtVega()
    {
        var e = MarcheDeTest.Etat();
        var s = FabriqueStructures.Creer(TypeStructure.Strangle, MarcheDeTest.E2, [70000, 78000], e.Referentiel);
        var a = ProfilPnL.Calculer(PricerStructure.Coter(s, Sens.Achat, 1, e), e);
        var v = ProfilPnL.Calculer(PricerStructure.Coter(s, Sens.Vente, 1, e), e);
        for (int i = 0; i < a.Points.Count; i++)
        {
            Assert.AreEqual(-a.Points[i].Delta, v.Points[i].Delta, 1e-10);
            Assert.AreEqual(-a.Points[i].Vega, v.Points[i].Vega, 1e-8);
        }
    }

    /// <summary>
    /// We check that the OTC profile shows the desk's contract and premium, without mixing the
    /// covers.
    /// </summary>
    [TestMethod]
    public void Profil_OTC_ContientOptionDeskSansCouverture()
    {
        var e = MarcheDeTest.Etat();
        var p = MoteurCouverture.CalculerMeilleure(new JambeSurMesure(TypeOption.Call, 77000, MarcheDeTest.E1.AddDays(7), 1), Sens.Achat, 10, e);
        var profil = ProfilPnL.PourOptionSurMesure(p, e);
        var centre = profil.Points.Single(x => Math.Abs(x.Variation) < 1e-12);
        Assert.AreEqual(p.GrecquesOption.Delta, centre.Delta, 1e-10);
        Assert.AreEqual(p.GrecquesOption.Vega, centre.Vega, 1e-8);
        Assert.AreEqual(p.QuantiteDesk * p.PrixClientBtc, profil.PrimeBtc, 1e-12);
        Assert.IsTrue(centre.Vega < 0);
    }

    /// <summary>
    /// It is checked that an invalid or too large grid is rejected before starting its
    /// calculations.
    /// </summary>
    [DataTestMethod]
    [DataRow(-1.0, 0.3, 0.05)]
    [DataRow(-0.3, 0.3, 0.0)]
    [DataRow(-0.3, 0.3, 0.000001)]
    public void Profil_GrilleDangereuse_Refusee(double min, double max, double pas)
    {
        var e = MarcheDeTest.Etat();
        var c = PricerStructure.Coter(FabriqueStructures.CreerOptionSimple(TypeOption.Call, MarcheDeTest.E1, 74000, e.Referentiel), Sens.Achat, 1, e);
        Assert.ThrowsException<ArgumentException>(() => ProfilPnL.Calculer(c, e, min, max, pas));
    }

    /// <summary>
    /// It is verified that market conditions and consistency bid/ask really condition the
    /// treatment.
    /// </summary>
    [TestMethod]
    public void Coter_CarnetCroiseOuFerme_NonTraitable()
    {
        var e = MarcheDeTest.Etat();
        var s = FabriqueStructures.CreerOptionSimple(TypeOption.Call, MarcheDeTest.E1, 74000, e.Referentiel);
        string nom = s.Jambes[0].Libelle;
        e.MettreAJour(e.Cotation(nom)! with { Bid = 0.03, Ask = 0.02 });
        Assert.IsFalse(PricerStructure.Coter(s, Sens.Achat, 1, e).Traitable);
        e.MettreAJour(e.Cotation(nom)! with { Bid = 0.01, Ask = 0.02, Etat = "closed" });
        Assert.IsFalse(PricerStructure.Coter(s, Sens.Achat, 1, e).Traitable);
    }

    /// <summary>
    /// It is verified that an old quota does not remain negotiable simply because it is still
    /// in memory.
    /// </summary>
    [TestMethod]
    public void Coter_CotationPerimee_Refusee()
    {
        var e = MarcheDeTest.Etat();
        var s = FabriqueStructures.CreerOptionSimple(TypeOption.Call, MarcheDeTest.E1, 74000, e.Referentiel);
        e.DefinirIndice(MarcheDeTest.Indice, e.Maintenant.AddSeconds(31));
        Assert.ThrowsException<DonneesIndisponiblesException>(() => PricerStructure.Coter(s, Sens.Achat, 1, e));
    }

    /// <summary>
    /// It is verified that a future absentee is not arbitrarily replaced by a contract of
    /// another maturity.
    /// </summary>
    [TestMethod]
    public void Coter_FutureAbsent_NeLeRemplacePasParAutreInstrument()
    {
        var e = MarcheDeTest.Etat();
        var s = FabriqueStructures.CreerOptionSimple(TypeOption.Call, MarcheDeTest.E1, 74000, e.Referentiel);
        e.MettreAJour(e.Cotation(s.Jambes[0].Libelle)! with { Future = null });
        Assert.ThrowsException<DonneesIndisponiblesException>(() => PricerStructure.Coter(s, Sens.Achat, 1, e));
    }

    /// <summary>
    /// It is verified that the neutralization of the delta also requires an exploitable market
    /// range.
    /// </summary>
    [TestMethod]
    public void Couvrir_FuturesSansBidAsk_Refuse()
    {
        var e = MarcheDeTest.Etat();
        foreach (var f in e.Referentiel.Futures) e.MettreAJour(e.Cotation(f.Nom)! with { Bid = null, Ask = null });
        Assert.ThrowsException<CouvertureImpossibleException>(() => MoteurCouverture.CalculerMeilleure(new JambeSurMesure(TypeOption.Call, 77000, MarcheDeTest.E1.AddDays(7), 1), Sens.Achat, 10, e));
    }

    /// <summary>
    /// We check the choice of a fairly long future and the calculation of the delta after
    /// rounding the options.
    /// </summary>
    [TestMethod]
    public void Couvrir_FutureApresOtcEtDeltaApresArrondiOptions()
    {
        var e = MarcheDeTest.Etat();
        var option = new JambeSurMesure(TypeOption.Call, 77000, MarcheDeTest.E1.AddDays(7), 1);
        var p = MoteurCouverture.CalculerMeilleure(option, Sens.Achat, 10, e);
        var f = p.Lignes.Single(l => l.Nature == NatureJambe.Future);
        Assert.IsTrue(f.Echeance >= option.Echeance);
        Assert.IsTrue(Math.Abs(p.GrecquesGlobales.Delta) <= 5 / f.PrixExecutable + 1e-9);
    }

    /// <summary>
    /// A decrease in total variance between maturities is verified to block inconsistent
    /// interpolation.
    /// </summary>
    [TestMethod]
    public void Interpoler_VarianceDecroissante_Refuse()
    {
        var s1 = new SmileEcheance(MarcheDeTest.E1, 0.025, 74000, [new PointSmile(60000, 2), new PointSmile(90000, 2)]);
        var s2 = new SmileEcheance(MarcheDeTest.E2, 0.12, 74500, [new PointSmile(60000, 0.1), new PointSmile(90000, 0.1)]);
        Assert.ThrowsException<HorsPlageException>(() => Interpolateur.Interpoler(77000, MarcheDeTest.E1.AddDays(7), MarcheDeTest.Maintenant, [s1, s2]));
    }

    /// <summary>
    /// It is checked that an unusable mark allows to try the call or put alternative to the
    /// same strike.
    /// </summary>
    [TestMethod]
    public void Smile_MarkOtmInutilisable_EssaieAutreOptionDuStrike()
    {
        var e = MarcheDeTest.Etat();
        string nom = MarcheDeTest.Nom(MarcheDeTest.E1, 76000, TypeOption.Call);
        e.MettreAJour(e.Cotation(nom)! with { Mark = null });
        Assert.IsTrue(e.Smiles().Single(s => s.Echeance == MarcheDeTest.E1).Points.Any(p => p.Strike == 76000));
    }
}
