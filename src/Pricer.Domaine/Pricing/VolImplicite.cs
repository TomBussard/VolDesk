using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Pricing;

public enum StatutVol
{
    Calculee,
    HorsBornesArbitrage,
    HorsIntervalle,
    MaturiteNulle,
    PrixAbsent,
    NonConvergee
}

public enum MethodeVol
{
    Aucune,
    Newton,
    Dichotomie
}

/// <summary>
/// On renvoie un statut quand une IV ne peut pas être calculée, car un bid absent ou un prix hors bornes
/// est une situation de marché attendue. On réserve les exceptions aux paramètres incohérents.
/// </summary>
public sealed record ResultatVol(double? Sigma, StatutVol Statut, MethodeVol Methode, int Iterations)
{
    public bool EstCalculee => Statut == StatutVol.Calculee;

    /// <summary>
    /// On garde la cause de l'échec sans attribuer une volatilité arbitraire au prix reçu.
    /// </summary>
    public static ResultatVol Echec(StatutVol statut) => new(null, statut, MethodeVol.Aucune, 0);

    public string Libelle => Statut switch
    {
        StatutVol.Calculee => $"{Sigma!.Value * 100:0.00} %",
        StatutVol.PrixAbsent => "pas de prix",
        StatutVol.HorsBornesArbitrage => "n/c (bornes)",
        StatutVol.HorsIntervalle => "n/c (>500 %)",
        StatutVol.MaturiteNulle => "n/c (échue)",
        _ => "n/c"
    };
}

/// <summary>
/// On inverse Black-76 : Newton démarre à 50 % et s'arrête à 10^-6 USD d'erreur. On
/// utilise la dichotomie dans [0,01 % ; 500 %] si le vega devient trop faible ou si Newton sort de
/// l'intervalle.
/// </summary>
public static class VolImplicite
{
    public const double VolMin = 0.0001;
    public const double VolMax = 5.0;
    public const double Tolerance = 1e-6;
    public const double VegaMinimal = 1e-8;
    private const int IterationsMaxNewton = 50;
    private const int IterationsMaxDichotomie = 200;

    /// <summary>
    /// On convertit d'abord le prix crypto en USD en le multipliant par le future F, puis on cherche sa
    /// volatilité.
    /// </summary>
    public static ResultatVol CalculerDepuisBtc(double? prixBtc, double future, double strike, double maturite, TypeOption type) =>
        prixBtc is double p ? Calculer(p * future, future, strike, maturite, type) : ResultatVol.Echec(StatutVol.PrixAbsent);

    /// <summary>
    /// On cherche la volatilité d'un prix USD admissible avec Newton, puis on utilise la dichotomie si
    /// l'itération devient inutilisable.
    /// </summary>
    /// <param name="prixUsd">Le prix de marché de l'option, déjà converti en USD.</param>
    /// <param name="future">Le future de l'échéance en USD.</param>
    /// <param name="strike">Le strike de l'option en USD.</param>
    /// <param name="maturite">La durée restante en années ACT/365.</param>
    /// <param name="type">Le type call ou put de l'option.</param>
    /// <param name="sigma0">La volatilité décimale de départ de Newton, égale à 0,5 par défaut.</param>
    /// <returns>Une volatilité décimale avec sa méthode et ses itérations, ou un statut précisant pourquoi elle n'est pas calculable.</returns>
    public static ResultatVol Calculer(double prixUsd, double future, double strike, double maturite, TypeOption type, double sigma0 = 0.5)
    {
        if (!double.IsFinite(prixUsd) || !double.IsFinite(future) || future <= 0 || !double.IsFinite(strike) || strike <= 0 || !double.IsFinite(maturite))
            return ResultatVol.Echec(StatutVol.HorsBornesArbitrage);
        if (maturite <= 0) return ResultatVol.Echec(StatutVol.MaturiteNulle);
        if (!RespecteBornes(prixUsd, future, strike, type)) return ResultatVol.Echec(StatutVol.HorsBornesArbitrage);

        // On essaie Newton en premier, car le vega permet de corriger rapidement la volatilité.
        double sigma = double.IsFinite(sigma0) ? Math.Clamp(sigma0, VolMin, VolMax) : 0.5;
        for (int i = 1; i <= IterationsMaxNewton; i++)
        {
            var r = Black76.Calculer(future, strike, maturite, sigma, type);
            double ecart = r.PrixUsd - prixUsd;
            if (Math.Abs(ecart) < Tolerance) return new ResultatVol(sigma, StatutVol.Calculee, MethodeVol.Newton, i);
            if (r.VegaBrut < VegaMinimal) break;
            sigma -= ecart / r.VegaBrut;
            if (sigma < VolMin || sigma > VolMax) break;
        }

        // On garde la dichotomie en secours : le prix croît avec la volatilité sur l'intervalle retenu.
        return Dichotomie(prixUsd, future, strike, maturite, type);
    }

    /// <summary>
    /// On exige un prix strictement entre les bornes de non-arbitrage pour qu'une volatilité implicite soit
    /// recherchée.
    /// </summary>
    public static bool RespecteBornes(double prixUsd, double future, double strike, TypeOption type) => type == TypeOption.Call
        ? prixUsd > Math.Max(future - strike, 0) && prixUsd < future
        : prixUsd > Math.Max(strike - future, 0) && prixUsd < strike;

    /// <summary>
    /// On encadre la volatilité entre les bornes autorisées et on divise cet intervalle jusqu'à atteindre la
    /// tolérance de prix.
    /// </summary>
    private static ResultatVol Dichotomie(double prixUsd, double future, double strike, double maturite, TypeOption type)
    {
        double bas = VolMin, haut = VolMax;
        if (prixUsd < Black76.Prix(future, strike, maturite, bas, type) || prixUsd > Black76.Prix(future, strike, maturite, haut, type))
            return ResultatVol.Echec(StatutVol.HorsIntervalle);

        for (int i = 1; i <= IterationsMaxDichotomie; i++)
        {
            double milieu = 0.5 * (bas + haut);
            double ecart = Black76.Prix(future, strike, maturite, milieu, type) - prixUsd;
            if (Math.Abs(ecart) < Tolerance) return new ResultatVol(milieu, StatutVol.Calculee, MethodeVol.Dichotomie, i);
            if (ecart > 0) haut = milieu; else bas = milieu;
        }
        return ResultatVol.Echec(StatutVol.NonConvergee);
    }
}
