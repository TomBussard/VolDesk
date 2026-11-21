namespace Pricer.Domaine.Utilitaires;

/// <summary>
/// On exprime la maturité en années ACT/365, avec le nombre exact de secondes entre la cotation et
/// l'échéance.
/// </summary>
public static class Maturite
{
    public const double SecondesParAn = 365.0 * 24 * 3600;

    /// <summary>
    /// On mesure les secondes restantes sur une année de 365 jours ; une valeur négative permet au calcul
    /// appelant de repérer une échéance passée.
    /// </summary>
    /// <returns>La maturité ACT/365 signée, négative si l'échéance précède la cotation.</returns>
    public static double EnAnnees(DateTime echeance, DateTime horodatage) =>
        (echeance - horodatage).TotalSeconds / SecondesParAn;

    /// <summary>On place les échéances standards Deribit à 08:00 UTC pour les rattacher au prix de livraison quotidien.</summary>
    public static DateTime EcheanceDeribit(DateOnly jour) =>
        new(jour.Year, jour.Month, jour.Day, 8, 0, 0, DateTimeKind.Utc);
}
