using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class VolImpliciteTests
{
    /// <summary>
    /// Price reversal is verified on several known strikes, durations and volatilitys.
    /// </summary>
    [TestMethod]
    public void Calculer_AllerRetourSurUneGrille_RetrouveLaVol()
    {
        foreach (var type in new[] { TypeOption.Call, TypeOption.Put })
            foreach (double k in new[] { 50000.0, 65000, 74000, 85000, 110000 })
                foreach (double t in new[] { 1 / 365.0, 0.1, 1.0 })
                    foreach (double s in new[] { 0.2, 0.5, 1.2 })
                    {
                        double prix = Black76.Prix(74000, k, t, s, type);
                        var r = VolImplicite.Calculer(prix, 74000, k, t, type);
                        if (r.Statut != StatutVol.Calculee) continue; // valeur temps sous la précision machine
                        // Price tolerance is required even when the inversion of the volatility is sensitive.
                        Assert.AreEqual(prix, Black76.Prix(74000, k, t, r.Sigma!.Value, type), 1e-6);
                        // Volatility is also compared when the vega accurately identifies it.
                        if (Black76.Calculer(74000, k, t, s, type).VegaBrut > 1)
                            Assert.AreEqual(s, r.Sigma!.Value, 1e-5, $"{type} K={k} T={t} σ={s}");
                    }
    }

    /// <summary>
    /// We check that Newton converges quickly when the vega allows a well conditioned
    /// inversion.
    /// </summary>
    [TestMethod]
    public void Calculer_ATM_ConvergeParNewtonEnPeuDIterations()
    {
        double prix = Black76.Prix(74000, 74000, 0.25, 0.6, TypeOption.Call);
        var r = VolImplicite.Calculer(prix, 74000, 74000, 0.25, TypeOption.Call);

        Assert.AreEqual(StatutVol.Calculee, r.Statut);
        Assert.AreEqual(MethodeVol.Newton, r.Methode);
        Assert.IsTrue(r.Iterations <= 6);
        Assert.AreEqual(0.6, r.Sigma!.Value, 1e-9);
    }

    /// <summary>
    /// It is verified that a price below its arbitration point is not transformed into
    /// volatility.
    /// </summary>
    [TestMethod]
    public void Calculer_PrixSousLaValeurIntrinseque_NonCalculable()
    {
        var r = VolImplicite.Calculer(3999, 74000, 70000, 0.1, TypeOption.Call);
        Assert.AreEqual(StatutVol.HorsBornesArbitrage, r.Statut);
        Assert.IsNull(r.Sigma);
    }

    /// <summary>
    /// It is checked that the upper terminal of the call is checked before looking for a root.
    /// </summary>
    [TestMethod]
    public void Calculer_PrixSuperieurAuFuture_NonCalculable()
    {
        Assert.AreEqual(StatutVol.HorsBornesArbitrage, VolImplicite.Calculer(74000, 74000, 70000, 0.1, TypeOption.Call).Statut);
        Assert.AreEqual(StatutVol.HorsBornesArbitrage, VolImplicite.Calculer(70000, 74000, 70000, 0.1, TypeOption.Put).Statut);
    }

    /// <summary>
    /// It is verified that an untapped price gives an explicit status rather than invented
    /// volatility.
    /// </summary>
    [TestMethod]
    public void Calculer_PrixNulOuNegatif_NonCalculable()
    {
        Assert.AreEqual(StatutVol.HorsBornesArbitrage, VolImplicite.Calculer(0, 74000, 90000, 0.1, TypeOption.Call).Statut);
    }

    /// <summary>
    /// It is checked that frame rescue remains usable when Newton lacks sensitivity.
    /// </summary>
    [TestMethod]
    public void Calculer_OptionTresHorsDeLaMonnaieVegaInfime_BasculeSurDichotomie()
    {
        // We choose a very distant strike and a short duration to make the initial vega almost void.
        double prix = Black76.Prix(74000, 150000, 0.05, 1.5, TypeOption.Call);
        var r = VolImplicite.Calculer(prix, 74000, 150000, 0.05, TypeOption.Call);

        Assert.AreEqual(StatutVol.Calculee, r.Statut);
        Assert.AreEqual(MethodeVol.Dichotomie, r.Methode);
        Assert.AreEqual(1.5, r.Sigma!.Value, 1e-6);
    }

    /// <summary>
    /// It is verified that a solution outside the authorized range is reported instead of being
    /// accepted silently.
    /// </summary>
    [TestMethod]
    public void Calculer_VolAuDelaDe500Pourcent_HorsIntervalle()
    {
        double prix = Black76.Prix(74000, 74000, 0.5, 6.0, TypeOption.Call);
        Assert.AreEqual(StatutVol.HorsIntervalle, VolImplicite.Calculer(prix, 74000, 74000, 0.5, TypeOption.Call).Statut);
    }

    /// <summary>
    /// It is checked that an option with no remaining duration does not involve digital
    /// inversion.
    /// </summary>
    [TestMethod]
    public void Calculer_MaturiteEchue_NonCalculable()
    {
        Assert.AreEqual(StatutVol.MaturiteNulle, VolImplicite.Calculer(100, 74000, 74000, 0, TypeOption.Call).Statut);
    }

    /// <summary>
    /// We are checking that the reversal of a crypto price is using the future to find the USD
    /// price.
    /// </summary>
    [TestMethod]
    public void CalculerDepuisBtc_ConvertitEnUsdAvantInversion()
    {
        double prixBtc = Black76.Calculer(74000, 80000, 0.1, 0.45, TypeOption.Call).PrixBtc;
        var r = VolImplicite.CalculerDepuisBtc(prixBtc, 74000, 80000, 0.1, TypeOption.Call);
        Assert.AreEqual(0.45, r.Sigma!.Value, 1e-8);
    }
}
