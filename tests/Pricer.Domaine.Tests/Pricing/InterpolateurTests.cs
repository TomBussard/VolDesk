using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Tests.Outils;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class InterpolateurTests
{
    /// <summary>
    /// On prépare les points d'un smile simple dont les interpolations sont calculables à la main.
    /// </summary>
    private static SmileEcheance Smile(DateTime e, double f, params (double K, double V)[] points) =>
        new(e, Maturite.EnAnnees(e, MarcheDeTest.Maintenant), f, points.Select(p => new PointSmile(p.K, p.V)).ToList());

    /// <summary>
    /// On vérifie qu'un point déjà coté est restitué sans créer un écart d'interpolation.
    /// </summary>
    [TestMethod]
    public void Interpoler_EcheanceEtStrikeCotes_RetrouveLaVolCotee()
    {
        var etat = MarcheDeTest.Etat();
        var r = Interpolateur.Interpoler(76000, MarcheDeTest.E2, MarcheDeTest.Maintenant, etat.Smiles());

        Assert.IsTrue(r.EcheanceCotee);
        Assert.AreEqual(MarcheDeTest.Futures[MarcheDeTest.E2], r.Future);
        Assert.AreEqual(MarcheDeTest.Vol(MarcheDeTest.E2, 76000), r.Sigma, 1e-9);
    }

    /// <summary>
    /// On vérifie le poids logarithmique entre deux strikes d'une même échéance.
    /// </summary>
    [TestMethod]
    public void Interpoler_EcheanceCoteeStrikeIntermediaire_LineaireEnLogMoneyness()
    {
        var s = Smile(MarcheDeTest.E1, 74000, (70000, 0.50), (80000, 0.40));
        double k = Math.Log(75000 / 74000.0), ka = Math.Log(70000 / 74000.0), kb = Math.Log(80000 / 74000.0);

        var r = Interpolateur.Interpoler(75000, MarcheDeTest.E1, MarcheDeTest.Maintenant, [s]);

        Assert.AreEqual(0.50 + (k - ka) / (kb - ka) * (0.40 - 0.50), r.Sigma, 1e-14);
    }

    /// <summary>
    /// On vérifie séparément les conventions d'interpolation du future et de la variance totale.
    /// </summary>
    [TestMethod]
    public void Interpoler_EntreDeuxEcheances_FutureLineaireEtVarianceTotaleLineaire()
    {
        // On utilise des smiles plats pour isoler le calcul de variance totale entre les dates.
        var s1 = Smile(MarcheDeTest.E1, 74000, (50000, 0.40), (100000, 0.40));
        var s2 = Smile(MarcheDeTest.E3, 76000, (50000, 0.60), (100000, 0.60));
        var e = new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        double t = Maturite.EnAnnees(e, MarcheDeTest.Maintenant), t1 = s1.Maturite, t2 = s2.Maturite;
        double poids = (t - t1) / (t2 - t1);

        var r = Interpolateur.Interpoler(75000, e, MarcheDeTest.Maintenant, [s2, s1]);

        Assert.AreEqual(74000 + poids * 2000, r.Future, 1e-9);
        double w = 0.16 * t1 + poids * (0.36 * t2 - 0.16 * t1);
        Assert.AreEqual(Math.Sqrt(w / t), r.Sigma, 1e-12);
        Assert.AreEqual(Math.Log(75000 / r.Future), r.LogMoneyness, 1e-14);
    }

    /// <summary>
    /// On vérifie que des futures différents conduisent aux strikes équivalents appropriés sur chaque
    /// smile.
    /// </summary>
    [TestMethod]
    public void Interpoler_LogMoneynessEgale_StrikeEquivalentDifferentSurChaqueEcheance()
    {
        // On accentue le smile sur E3 pour vérifier que le strike équivalent suit son future : Ki = Fi exp(k).
        var s1 = Smile(MarcheDeTest.E1, 70000, (60000, 0.5), (80000, 0.5));
        var s3 = Smile(MarcheDeTest.E3, 80000, (60000, 0.8), (100000, 0.4));
        var r = Interpolateur.Interpoler(75000, MarcheDeTest.E2, MarcheDeTest.Maintenant, [s1, s3]);

        double kEquivalent3 = 80000 * Math.Exp(r.LogMoneyness);
        double attendu = Interpolateur.VolSurEcheance(s3, r.LogMoneyness);
        Assert.AreEqual(0.8 + (kEquivalent3 - kEquivalent3) + (Math.Log(kEquivalent3 / 80000) - Math.Log(60000 / 80000.0)) / (Math.Log(100000 / 80000.0) - Math.Log(60000 / 80000.0)) * (0.4 - 0.8), attendu, 1e-12);
        Assert.AreEqual(attendu, r.Sigma2!.Value, 1e-14);
    }

    /// <summary>
    /// On vérifie que le prix interpolé ne saute pas à l'approche d'une maturité déjà cotée.
    /// </summary>
    [TestMethod]
    public void Interpoler_ContinuiteVersLEcheanceCotee()
    {
        var etat = MarcheDeTest.Etat();
        var smiles = etat.Smiles();
        double coteE2 = Interpolateur.Interpoler(75000, MarcheDeTest.E2, MarcheDeTest.Maintenant, smiles).Sigma;
        double justeAvant = Interpolateur.Interpoler(75000, MarcheDeTest.E2.AddMinutes(-1), MarcheDeTest.Maintenant, smiles).Sigma;
        Assert.AreEqual(coteE2, justeAvant, 1e-4);
    }

    /// <summary>
    /// On vérifie que l'outil n'extrapole pas une volatilité hors des strikes disponibles.
    /// </summary>
    [TestMethod]
    public void Interpoler_StrikeEquivalentHorsDesStrikesCotes_LeveHorsPlage()
    {
        var etat = MarcheDeTest.Etat();
        Assert.ThrowsException<HorsPlageException>(() => Interpolateur.Interpoler(120000, MarcheDeTest.E2, MarcheDeTest.Maintenant, etat.Smiles()));
    }

    /// <summary>
    /// On vérifie qu'une date non encadrée par le marché est refusée.
    /// </summary>
    [TestMethod]
    public void Interpoler_EcheanceAvantLaPremiere_LeveHorsPlage()
    {
        var etat = MarcheDeTest.Etat();
        Assert.ThrowsException<HorsPlageException>(() =>
            Interpolateur.Interpoler(75000, MarcheDeTest.Maintenant.AddDays(2), MarcheDeTest.Maintenant, etat.Smiles()));
    }

    /// <summary>
    /// On vérifie qu'une demande échue n'entre pas dans les calculs de variance et de maturité.
    /// </summary>
    [TestMethod]
    public void Interpoler_EcheancePassee_LeveHorsPlage()
    {
        Assert.ThrowsException<HorsPlageException>(() =>
            Interpolateur.Interpoler(75000, MarcheDeTest.Maintenant.AddDays(-1), MarcheDeTest.Maintenant, []));
    }

    /// <summary>
    /// On vérifie l'inversion des marks sur un marché dont la volatilité de départ est connue.
    /// </summary>
    [TestMethod]
    public void Smiles_CalculesParNotreInversionDuMark_RetrouventLeSmileSynthetique()
    {
        var etat = MarcheDeTest.Etat();
        var smile = etat.Smiles().Single(s => s.Echeance == MarcheDeTest.E3);
        foreach (var p in smile.Points)
            Assert.AreEqual(MarcheDeTest.Vol(MarcheDeTest.E3, p.Strike), p.Vol, 1e-7);
    }
}
