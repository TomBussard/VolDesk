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
    /// On vérifie que le scénario sans choc retrouve la cote courante pour toutes les recettes
    /// proposées.
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
    /// On vérifie que changer le sens de la structure inverse ses sensibilités sur toute la grille.
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
    /// On vérifie que le profil OTC montre le contrat du desk et sa prime, sans y mélanger les
    /// couvertures.
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
    /// On vérifie qu'une grille invalide ou trop grande est rejetée avant de lancer ses calculs.
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
    /// On vérifie que l'état du marché et la cohérence bid/ask conditionnent réellement le traitement.
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
    /// On vérifie qu'une ancienne quote ne reste pas négociable simplement parce qu'elle est encore en
    /// mémoire.
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
    /// On vérifie qu'un future absent n'est pas remplacé arbitrairement par un contrat d'une autre
    /// échéance.
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
    /// On vérifie que la neutralisation du delta exige elle aussi une fourchette de marché exploitable.
    /// </summary>
    [TestMethod]
    public void Couvrir_FuturesSansBidAsk_Refuse()
    {
        var e = MarcheDeTest.Etat();
        foreach (var f in e.Referentiel.Futures) e.MettreAJour(e.Cotation(f.Nom)! with { Bid = null, Ask = null });
        Assert.ThrowsException<CouvertureImpossibleException>(() => MoteurCouverture.CalculerMeilleure(new JambeSurMesure(TypeOption.Call, 77000, MarcheDeTest.E1.AddDays(7), 1), Sens.Achat, 10, e));
    }

    /// <summary>
    /// On vérifie le choix d'un future assez long et le calcul du delta après l'arrondi des options.
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
    /// On vérifie qu'une baisse de variance totale entre les échéances bloque une interpolation
    /// incohérente.
    /// </summary>
    [TestMethod]
    public void Interpoler_VarianceDecroissante_Refuse()
    {
        var s1 = new SmileEcheance(MarcheDeTest.E1, 0.025, 74000, [new PointSmile(60000, 2), new PointSmile(90000, 2)]);
        var s2 = new SmileEcheance(MarcheDeTest.E2, 0.12, 74500, [new PointSmile(60000, 0.1), new PointSmile(90000, 0.1)]);
        Assert.ThrowsException<HorsPlageException>(() => Interpolateur.Interpoler(77000, MarcheDeTest.E1.AddDays(7), MarcheDeTest.Maintenant, [s1, s2]));
    }

    /// <summary>
    /// On vérifie qu'un mark inutilisable permet d'essayer le call ou put alternatif au même strike.
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

