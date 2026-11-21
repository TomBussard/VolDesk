using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class Black76Tests
{
    // On compare les résultats à des valeurs numériques calculées indépendamment du moteur testé.
    /// <summary>
    /// On vérifie un prix et ses sensibilités sur des valeurs numériques connues pour détecter une
    /// erreur de formule ou d'unité.
    /// </summary>
    [TestMethod]
    public void Calculer_CallHorsDeLaMonnaie_PrixEtGrecquesDeReference()
    {
        var r = Black76.Calculer(74000, 80000, 0.25, 0.5, TypeOption.Call);

        Assert.AreEqual(5026.9895832477, r.PrixUsd, 1e-6);
        Assert.AreEqual(5026.9895832477 / 74000, r.PrixBtc, 1e-10);
        Assert.AreEqual(0.4258906246, r.Grecques.Delta, 1e-9);
        Assert.AreEqual(0.0000211913, r.Grecques.Gamma, 1e-10);
        Assert.AreEqual(145.0543830442, r.Grecques.Vega, 1e-6);
        Assert.AreEqual(-39.7409268614, r.Grecques.Theta, 1e-6);
    }

    /// <summary>
    /// On vérifie le cas du put dans la monnaie, notamment le signe et l'amplitude de son delta.
    /// </summary>
    [TestMethod]
    public void Calculer_PutDansLaMonnaie_PrixEtDeltaDeReference()
    {
        var r = Black76.Calculer(74000, 80000, 0.25, 0.5, TypeOption.Put);

        Assert.AreEqual(11026.9895832477, r.PrixUsd, 1e-6);
        Assert.AreEqual(-0.5741093754, r.Grecques.Delta, 1e-9);
    }

    /// <summary>
    /// On vérifie une relation d'arbitrage entre call et put sur plusieurs strikes et durées.
    /// </summary>
    [TestMethod]
    public void Calculer_ParitéCallPutATauxNul_CMoinsPEgalFMoinsK()
    {
        foreach (double k in new[] { 50000.0, 70000, 74000, 90000 })
        {
            var c = Black76.Calculer(74000, k, 0.1, 0.6, TypeOption.Call);
            var p = Black76.Calculer(74000, k, 0.1, 0.6, TypeOption.Put);
            Assert.AreEqual(74000 - k, c.PrixUsd - p.PrixUsd, 1e-7);
            Assert.AreEqual(1.0, c.Grecques.Delta - p.Grecques.Delta, 1e-12);
            Assert.AreEqual(c.Grecques.Gamma, p.Grecques.Gamma, 1e-15);
            Assert.AreEqual(c.Grecques.Vega, p.Grecques.Vega, 1e-9);
        }
    }

    /// <summary>
    /// On vérifie les grecques avec des perturbations de prix, de vol et de temps indépendantes des
    /// formules analytiques.
    /// </summary>
    [TestMethod]
    public void Calculer_GrecquesComparéesAuxDifférencesFinies_Concordent()
    {
        double f = 74000, k = 76000, t = 0.2, s = 0.55, hF = 1.0, hS = 1e-5, hT = 1e-6;
        var r = Black76.Calculer(f, k, t, s, TypeOption.Call);
        double P(double ff, double tt, double ss) => Black76.Calculer(ff, k, tt, ss, TypeOption.Call).PrixUsd;

        Assert.AreEqual((P(f + hF, t, s) - P(f - hF, t, s)) / (2 * hF), r.Grecques.Delta, 1e-7);
        Assert.AreEqual((P(f + hF, t, s) - 2 * P(f, t, s) + P(f - hF, t, s)) / (hF * hF), r.Grecques.Gamma, 1e-8);
        Assert.AreEqual((P(f, t, s + hS) - P(f, t, s - hS)) / (2 * hS) / 100, r.Grecques.Vega, 1e-5);
        // On fait diminuer T pour mesurer le theta par jour de temps calendaire écoulé.
        Assert.AreEqual(-(P(f, t + hT, s) - P(f, t - hT, s)) / (2 * hT) / 365, r.Grecques.Theta, 1e-3);
    }

    /// <summary>
    /// On vérifie le sens économique des variations du prix pour des paramètres de marché valides.
    /// </summary>
    [TestMethod]
    public void Calculer_PrixCroissantAvecLaVolEtDecroissantAvecLeStrike()
    {
        double precedent = 0;
        for (double s = 0.1; s < 2; s += 0.1)
        {
            double prix = Black76.Calculer(74000, 74000, 0.1, s, TypeOption.Call).PrixUsd;
            Assert.IsTrue(prix > precedent);
            precedent = prix;
        }
        Assert.IsTrue(Black76.Calculer(74000, 70000, 0.1, 0.5, TypeOption.Call).PrixUsd
                    > Black76.Calculer(74000, 72000, 0.1, 0.5, TypeOption.Call).PrixUsd);
    }

    /// <summary>
    /// On vérifie qu'une option échue est évaluée sans division par une durée nulle.
    /// </summary>
    [TestMethod]
    public void Calculer_MaturiteNulle_RenvoieLaValeurIntrinseque()
    {
        var c = Black76.Calculer(74000, 70000, 0, 0.5, TypeOption.Call);
        var p = Black76.Calculer(74000, 70000, 0, 0.5, TypeOption.Put);

        Assert.AreEqual(4000, c.PrixUsd);
        Assert.AreEqual(0, p.PrixUsd);
        Assert.AreEqual(1, c.Grecques.Delta);
        Assert.AreEqual(0, c.Grecques.Gamma);
    }

    /// <summary>
    /// On vérifie qu'un sous-jacent invalide est rejeté avant les logarithmes du modèle.
    /// </summary>
    [TestMethod]
    public void Calculer_FutureNegatif_LeveArgumentOutOfRangeException()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Black76.Calculer(-1, 70000, 0.1, 0.5, TypeOption.Call));
    }

    /// <summary>
    /// On vérifie que les deux points d'entrée du calcul partagent exactement la même convention de
    /// prix.
    /// </summary>
    [TestMethod]
    public void Prix_SurchargeAvecParametres_EgaleCalculer()
    {
        Assert.AreEqual(Black76.Calculer(100, 100, 1, 0.2, TypeOption.Call).PrixUsd,
                        Black76.Prix(100, 100, 1, 0.2, TypeOption.Call), 1e-12);
        Assert.AreEqual(7.9655674554, Black76.Prix(100, 100, 1, 0.2, TypeOption.Put), 1e-9);
    }
}
