using System.Globalization;

namespace Pricer.Domaine.Utilitaires;

/// <summary>
/// On regroupe les arrondis et les formats réutilisés par les calculs pour conserver les mêmes conventions
/// partout.
/// </summary>
public static class ExtensionsNumeriques
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// On arrondit au multiple le plus proche du pas. On utilise decimal pour éviter qu'une imprécision binaire
    /// transforme 0,1 fois 3 en une quantité hors pas.
    /// </summary>
    public static double ArrondirAuPas(this double valeur, double pas)
    {
        if (pas <= 0) throw new ArgumentOutOfRangeException(nameof(pas), "Le pas doit être strictement positif.");
        decimal p = (decimal)pas;
        decimal n = Math.Round((decimal)valeur / p, MidpointRounding.AwayFromZero);
        return (double)(n * p);
    }

    /// <summary>On vérifie que la quantité est un multiple du pas en tenant compte de la petite tolérance numérique.</summary>
    public static bool EstMultipleDe(this double valeur, double pas)
    {
        decimal ratio = (decimal)valeur / (decimal)pas;
        return Math.Abs(ratio - Math.Round(ratio)) < 1e-9m;
    }

    /// <summary>
    /// On formate un montant BTC avec six décimales et son unité pour les sorties qui utilisent
    /// explicitement cette devise.
    /// </summary>
    public static string EnBtc(this double valeur) => valeur.ToString("0.000000", Fr) + " BTC";
    /// <summary>
    /// On formate un montant USD avec deux décimales et les séparateurs français pour les sorties
    /// textuelles.
    /// </summary>
    public static string EnUsd(this double valeur) => valeur.ToString("#,##0.00", Fr) + " $";
    /// <summary>
    /// On passe d'une volatilité décimale à un affichage en pourcentage avec deux décimales.
    /// </summary>
    public static string EnPointsDeVol(this double vol) => (vol * 100).ToString("0.00", Fr) + " %";
    /// <summary>
    /// On affiche une variation décimale en pourcentage entier en conservant son signe.
    /// </summary>
    public static string EnPourcentage(this double x) => (x * 100).ToString("+0;-0;0", Fr) + " %";
}
