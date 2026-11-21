using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class CouvertureTests
{
    private static readonly DateTime EcheanceOtc = new(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// On vérifie la résolution du système sur une solution connue avant de l'utiliser pour les
    /// quantités de couverture.
    /// </summary>
    [TestMethod]
    public void ResoudreSysteme2x2_SystemeConnu_SolutionExacte()
    {
        double[,] a = { { 2, 1 }, { 1, 3 } };
        var q = MoteurCouverture.ResoudreSysteme2x2(a, [5, 10], out double det, out _);
        Assert.AreEqual(5, det);
        Assert.AreEqual(1, q[0], 1e-14);
        Assert.AreEqual(3, q[1], 1e-14);
    }

    /// <summary>
    /// On vérifie qu'un système sans solution unique est refusé au lieu de produire des quantités
    /// instables.
    /// </summary>
    [TestMethod]
    public void ResoudreSysteme2x2_MatriceSinguliere_LeveCouvertureImpossible()
    {
        double[,] a = { { 1e-5, 2e-5 }, { 50, 100 } };
        Assert.ThrowsException<CouvertureImpossibleException>(() => MoteurCouverture.ResoudreSysteme2x2(a, [1, 1], out _, out _));
    }

    /// <summary>
    /// On vérifie qu'une paire aux risques redondants ne peut pas être présentée comme une couverture
    /// gamma/vega.
    /// </summary>
    [TestMethod]
    public void Calculer_PaireCallPutMemeStrike_RefuseeCarGammaEtVegaIdentiques()
    {
        var etat = MarcheDeTest.Etat();
        var option = new JambeSurMesure(TypeOption.Call, 77000, EcheanceOtc, 1);
        var call = etat.Referentiel.Trouver(MarcheDeTest.E2, 76000, TypeOption.Call)!;
        var put = etat.Referentiel.Trouver(MarcheDeTest.E2, 76000, TypeOption.Put)!;

        var ex = Assert.ThrowsException<CouvertureImpossibleException>(() =>
            MoteurCouverture.Calculer(option, Sens.Achat, 10, call, put, etat));
        StringAssert.Contains(ex.Message, "singulier");
    }

    /// <summary>
    /// On vérifie que les quantités brutes résolvent bien les trois contraintes de neutralisation
    /// demandées.
    /// </summary>
    [TestMethod]
    public void Calculer_PaireValide_PositionGlobaleNeutreAvantArrondi()
    {
        var etat = MarcheDeTest.Etat();
        var option = new JambeSurMesure(TypeOption.Call, 77000, EcheanceOtc, 1);
        var h1 = etat.Referentiel.Trouver(MarcheDeTest.E2, 76000, TypeOption.Call)!;
        var h2 = etat.Referentiel.Trouver(MarcheDeTest.E3, 78000, TypeOption.Call)!;

        var p = MoteurCouverture.Calculer(option, Sens.Achat, 25, h1, h2, etat);

        Assert.AreEqual(-25, p.QuantiteDesk);
        Assert.AreEqual(0, p.GrecquesGlobalesAvantArrondi.Delta, 1e-9);
        Assert.AreEqual(0, p.GrecquesGlobalesAvantArrondi.Gamma, 1e-15);
        Assert.AreEqual(0, p.GrecquesGlobalesAvantArrondi.Vega, 1e-9);
    }

    /// <summary>
    /// On vérifie les pas de négociation et les sensibilités qui restent après les arrondis.
    /// </summary>
    [TestMethod]
    public void Calculer_QuantitesArrondiesAuPas_ResidusBornes()
    {
        var etat = MarcheDeTest.Etat();
        var option = new JambeSurMesure(TypeOption.Put, 70000, EcheanceOtc, 1);
        var h1 = etat.Referentiel.Trouver(MarcheDeTest.E2, 70000, TypeOption.Put)!;
        var h2 = etat.Referentiel.Trouver(MarcheDeTest.E3, 70000, TypeOption.Put)!;

        var p = MoteurCouverture.Calculer(option, Sens.Vente, 13.7, h1, h2, etat);

        foreach (var l in p.Lignes.Where(l => l.Nature == NatureJambe.OptionCotee))
            Assert.AreEqual(Math.Round(l.Quantite * 10), l.Quantite * 10, 1e-9, "multiple de 0,1");
        var future = p.Lignes.Single(l => l.Nature == NatureJambe.Future);
        Assert.AreEqual(Math.Round(future.Quantite * future.PrixExecutable / 10), future.Quantite * future.PrixExecutable / 10, 1e-6, "multiple de 10 USD");

        // On borne l'écart d'arrondi à un demi-pas pour chaque option de couverture.
        double vegaMax = 0.05 * p.Lignes.Where(l => l.Nature == NatureJambe.OptionCotee).Sum(l => Math.Abs(l.GrecquesUnitaires.Vega));
        Assert.IsTrue(Math.Abs(p.GrecquesGlobales.Vega) <= vegaMax + 1e-9);
        Assert.IsTrue(Math.Abs(p.GrecquesGlobales.Delta) <= 0.06 + 5.0 / future.PrixExecutable);
    }

    /// <summary>
    /// On vérifie que le sens client incorpore le coût de couverture et la marge du bon côté du prix.
    /// </summary>
    [TestMethod]
    public void Calculer_ClientAcheteur_PrixClientSuperieurAuTheorique()
    {
        var etat = MarcheDeTest.Etat();
        var option = new JambeSurMesure(TypeOption.Call, 77000, EcheanceOtc, 1);
        var h1 = etat.Referentiel.Trouver(MarcheDeTest.E2, 76000, TypeOption.Call)!;
        var h2 = etat.Referentiel.Trouver(MarcheDeTest.E3, 78000, TypeOption.Call)!;

        var achat = MoteurCouverture.Calculer(option, Sens.Achat, 10, h1, h2, etat);
        var vente = MoteurCouverture.Calculer(option, Sens.Vente, 10, h1, h2, etat);

        Assert.IsTrue(achat.CoutCouvertureBtc > 0);
        Assert.IsTrue(achat.PrixClientBtc > achat.ValeurOption.PrixBtc);
        Assert.IsTrue(vente.PrixClientBtc < vente.ValeurOption.PrixBtc);
    }

    /// <summary>
    /// On vérifie que le choix automatique évite une paire presque redondante en diversifiant les
    /// maturités.
    /// </summary>
    [TestMethod]
    public void CalculerMeilleure_ChoisitDeuxEcheancesDifferentes()
    {
        var etat = MarcheDeTest.Etat();
        var option = new JambeSurMesure(TypeOption.Call, 77000, EcheanceOtc, 1);

        var p = MoteurCouverture.CalculerMeilleure(option, Sens.Achat, 10, etat);
        var options = p.Lignes.Where(l => l.Nature == NatureJambe.OptionCotee).ToList();

        Assert.AreEqual(2, options.Count);
        Assert.AreNotEqual(options[0].Echeance, options[1].Echeance);
        Assert.IsTrue(p.DeterminantNormalise >= MoteurCouverture.DeterminantNormaliseConfortable);
    }

    /// <summary>
    /// On vérifie qu'une couverture ne peut pas acheter un instrument dépourvu de prix vendeur.
    /// </summary>
    [TestMethod]
    public void Calculer_OptionCouvranteSansAsk_Refusee()
    {
        var etat = MarcheDeTest.Etat();
        var h1 = etat.Referentiel.Trouver(MarcheDeTest.E2, 76000, TypeOption.Call)!;
        etat.MettreAJour(etat.Cotation(h1.Nom)! with { Ask = null });
        var h2 = etat.Referentiel.Trouver(MarcheDeTest.E3, 78000, TypeOption.Call)!;

        Assert.ThrowsException<CouvertureImpossibleException>(() =>
            MoteurCouverture.Calculer(new JambeSurMesure(TypeOption.Call, 77000, EcheanceOtc, 1), Sens.Achat, 10, h1, h2, etat));
    }
}
