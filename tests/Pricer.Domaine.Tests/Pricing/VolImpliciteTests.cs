using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class VolImpliciteTests
{
    /// <summary>
    /// On vérifie l'inversion du prix sur plusieurs strikes, durées et volatilités connues.
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
                        // On exige la tolérance de prix même lorsque l'inversion de la vol est sensible.
                        Assert.AreEqual(prix, Black76.Prix(74000, k, t, r.Sigma!.Value, type), 1e-6);
                        // On compare aussi la vol lorsque le vega permet de l'identifier avec précision.
                        if (Black76.Calculer(74000, k, t, s, type).VegaBrut > 1)
                            Assert.AreEqual(s, r.Sigma!.Value, 1e-5, $"{type} K={k} T={t} σ={s}");
                    }
    }

    /// <summary>
    /// On vérifie que Newton converge rapidement lorsque le vega permet une inversion bien
    /// conditionnée.
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
    /// On vérifie qu'un prix sous sa borne d'arbitrage n'est pas transformé en volatilité.
    /// </summary>
    [TestMethod]
    public void Calculer_PrixSousLaValeurIntrinseque_NonCalculable()
    {
        var r = VolImplicite.Calculer(3999, 74000, 70000, 0.1, TypeOption.Call);
        Assert.AreEqual(StatutVol.HorsBornesArbitrage, r.Statut);
        Assert.IsNull(r.Sigma);
    }

    /// <summary>
    /// On vérifie que la borne supérieure du call est contrôlée avant de chercher une racine.
    /// </summary>
    [TestMethod]
    public void Calculer_PrixSuperieurAuFuture_NonCalculable()
    {
        Assert.AreEqual(StatutVol.HorsBornesArbitrage, VolImplicite.Calculer(74000, 74000, 70000, 0.1, TypeOption.Call).Statut);
        Assert.AreEqual(StatutVol.HorsBornesArbitrage, VolImplicite.Calculer(70000, 74000, 70000, 0.1, TypeOption.Put).Statut);
    }

    /// <summary>
    /// On vérifie qu'un prix inexploitable donne un statut explicite plutôt qu'une volatilité inventée.
    /// </summary>
    [TestMethod]
    public void Calculer_PrixNulOuNegatif_NonCalculable()
    {
        Assert.AreEqual(StatutVol.HorsBornesArbitrage, VolImplicite.Calculer(0, 74000, 90000, 0.1, TypeOption.Call).Statut);
    }

    /// <summary>
    /// On vérifie que le secours par encadrement reste utilisable lorsque Newton manque de sensibilité.
    /// </summary>
    [TestMethod]
    public void Calculer_OptionTresHorsDeLaMonnaieVegaInfime_BasculeSurDichotomie()
    {
        // On choisit un strike très éloigné et une durée courte pour rendre le vega initial presque nul.
        double prix = Black76.Prix(74000, 150000, 0.05, 1.5, TypeOption.Call);
        var r = VolImplicite.Calculer(prix, 74000, 150000, 0.05, TypeOption.Call);

        Assert.AreEqual(StatutVol.Calculee, r.Statut);
        Assert.AreEqual(MethodeVol.Dichotomie, r.Methode);
        Assert.AreEqual(1.5, r.Sigma!.Value, 1e-6);
    }

    /// <summary>
    /// On vérifie qu'une solution hors de la plage autorisée est signalée au lieu d'être acceptée
    /// silencieusement.
    /// </summary>
    [TestMethod]
    public void Calculer_VolAuDelaDe500Pourcent_HorsIntervalle()
    {
        double prix = Black76.Prix(74000, 74000, 0.5, 6.0, TypeOption.Call);
        Assert.AreEqual(StatutVol.HorsIntervalle, VolImplicite.Calculer(prix, 74000, 74000, 0.5, TypeOption.Call).Statut);
    }

    /// <summary>
    /// On vérifie qu'une option sans durée restante ne passe pas par l'inversion numérique.
    /// </summary>
    [TestMethod]
    public void Calculer_MaturiteEchue_NonCalculable()
    {
        Assert.AreEqual(StatutVol.MaturiteNulle, VolImplicite.Calculer(100, 74000, 74000, 0, TypeOption.Call).Statut);
    }

    /// <summary>
    /// On vérifie que l'inversion d'un prix crypto utilise bien le future pour retrouver le prix USD.
    /// </summary>
    [TestMethod]
    public void CalculerDepuisBtc_ConvertitEnUsdAvantInversion()
    {
        double prixBtc = Black76.Calculer(74000, 80000, 0.1, 0.45, TypeOption.Call).PrixBtc;
        var r = VolImplicite.CalculerDepuisBtc(prixBtc, 74000, 80000, 0.1, TypeOption.Call);
        Assert.AreEqual(0.45, r.Sigma!.Value, 1e-8);
    }
}
