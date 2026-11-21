namespace Pricer.Domaine.Utilitaires;

/// <summary>
/// Maturity is expressed in ACT/365 years, with the exact number of seconds between rating and
/// maturity.
/// </summary>
public static class Maturite
{
    public const double SecondesParAn = 365.0 * 24 * 3600;

    /// <summary>
    /// The remaining seconds are measured over a 365-day year; a negative value allows the
    /// calling calculation to identify a past expiry.
    /// </summary>
    /// <returns>La maturité ACT/365 signée, négative si l'échéance précède la cotation.</returns>
    public static double EnAnnees(DateTime echeance, DateTime horodatage) =>
        (echeance - horodatage).TotalSeconds / SecondesParAn;

    /// <summary>
    /// The Deribit standard expiries are set at 08:00 UTC to attach them to the daily delivery
    /// price.
    /// </summary>
    public static DateTime EcheanceDeribit(DateOnly jour) =>
        new(jour.Year, jour.Month, jour.Day, 8, 0, 0, DateTimeKind.Utc);
}
