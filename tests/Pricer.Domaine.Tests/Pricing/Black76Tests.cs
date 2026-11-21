using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class Black76Tests
{
    // The results are compared to numerical values calculated independently of the engine tested.
    /// <summary>
    /// A price and its sensitivities are checked on numerical values known to detect a formula
    /// or unit error.
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
    /// The case of the put is checked in the currency, including the sign and amplitude of its
    /// delta.
    /// </summary>
    [TestMethod]
    public void Calculer_PutDansLaMonnaie_PrixEtDeltaDeReference()
    {
        var r = Black76.Calculer(74000, 80000, 0.25, 0.5, TypeOption.Put);

        Assert.AreEqual(11026.9895832477, r.PrixUsd, 1e-6);
        Assert.AreEqual(-0.5741093754, r.Grecques.Delta, 1e-9);
    }

    /// <summary>
    /// An arbitration relationship between call and put is verified over several strikes and
    /// durations.
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
    /// Greeks are checked with price, volatility and time disruptions independent of analytical
    /// formulas.
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
        // T is reduced to measure the theta per day of elapsed calendar time.
        Assert.AreEqual(-(P(f, t + hT, s) - P(f, t - hT, s)) / (2 * hT) / 365, r.Grecques.Theta, 1e-3);
    }

    /// <summary>
    /// The economic meaning of price changes is checked for valid market parameters.
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
    /// It is verified that a failed option is evaluated without division by zero duration.
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
    /// It is verified that an invalid underlying is rejected before the logarithms of the
    /// model.
    /// </summary>
    [TestMethod]
    public void Calculer_FutureNegatif_LeveArgumentOutOfRangeException()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Black76.Calculer(-1, 70000, 0.1, 0.5, TypeOption.Call));
    }

    /// <summary>
    /// It is verified that the two entry points of the calculation share exactly the same price
    /// agreement.
    /// </summary>
    [TestMethod]
    public void Prix_SurchargeAvecParametres_EgaleCalculer()
    {
        Assert.AreEqual(Black76.Calculer(100, 100, 1, 0.2, TypeOption.Call).PrixUsd,
                        Black76.Prix(100, 100, 1, 0.2, TypeOption.Call), 1e-12);
        Assert.AreEqual(7.9655674554, Black76.Prix(100, 100, 1, 0.2, TypeOption.Put), 1e-9);
    }
}
