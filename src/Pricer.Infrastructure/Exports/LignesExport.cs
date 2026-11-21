using Pricer.Domaine.Booking;
using Pricer.Domaine.Modeles;

namespace Pricer.Infrastructure.Exports;

/// <summary>
/// Each leg of the float is presented on a line to keep its parameters in an easily rereadable
/// export.
/// </summary>
public sealed record LigneBlotter
{
    public required string IdTrade { get; init; }
    public required string Horodatage { get; init; }
    public required string Nature { get; init; }
    public string? IdTradeOrigine { get; init; }
    public required string Structure { get; init; }
    public required string Description { get; init; }
    public required string Sens { get; init; }
    public required double Quantite { get; init; }
    public required string Instrument { get; init; }
    public required string NatureJambe { get; init; }
    public required double QuantiteSignee { get; init; }
    public required double PrixExecution { get; init; }
    public required double FutureAuTrade { get; init; }
    public double? VolAuTrade { get; init; }
    public required double PrimeTradeBtc { get; init; }
    public Guid IdJambe { get; init; }
    public Guid? IdJambeOrigine { get; init; }
    public double? NotionnelUsd { get; init; }
    public string Devise { get; init; } = "BTC";
}

/// <summary>
/// The values and the result of an original structure are grouped together on a line to read
/// the status of the book in exports.
/// </summary>
public sealed record LigneBook
{
    public required string IdTrade { get; init; }
    public required string Description { get; init; }
    public required string Echeance { get; init; }
    public required double QuantiteOuverte { get; init; }
    public required double? ValeurMarkBtc { get; init; }
    public required double? ValeurTheoriqueBtc { get; init; }
    public required double PrimeOuverteBtc { get; init; }
    public required double? PnlLatentBtc { get; init; }
    public required double PnlRealiseBtc { get; init; }
    public required double? PnlTotalBtc { get; init; }
    public required double? PnlTotalUsd { get; init; }
    public required double Delta { get; init; }
    public required double Gamma { get; init; }
    public required double Vega { get; init; }
    public required double Theta { get; init; }
    public string Statut { get; init; } = "";
    public string SourceMark { get; init; } = "";
    public bool RisqueComplet { get; init; }
    public string Devise { get; init; } = "BTC";
}

/// <summary>
/// Greeks are brought together from a expiry to distinguish between the risks posed by
/// different maturities.
/// </summary>
public sealed record LigneGrecques(string Echeance, double Delta, double Gamma, double Vega, double Theta, string Statut = "Complet");
/// <summary>
/// We detail the balance of an open leg, its possible notional and its status in the book.
/// </summary>
public sealed record LignePositionJambe(string IdTrade, Guid IdJambe, string Instrument, string Nature,
    string Echeance, double QuantiteRestante, double? NotionnelRestantUsd, double PrixEntree, string Statut);
/// <summary>
/// Each global measure is associated with its unit and its availability in synthesis export.
/// </summary>
public sealed record LigneSynthese(string Mesure, string Unite, double? Valeur, string Statut);

/// <summary>
/// Export lines are prepared in extension methods to share the same presentation between
/// formats.
/// </summary>
public static class ExtensionsExport
{
    private const string FormatDate = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// One line per leg of an operation is produced by keeping the references that link the
    /// closures to their opening.
    /// </summary>
    public static IEnumerable<LigneBlotter> VersLignes(this Trade t) => t.Jambes.Select(j => new LigneBlotter
    {
        IdTrade = t.Id.ToString(),
        Horodatage = t.Horodatage.ToString(FormatDate, System.Globalization.CultureInfo.InvariantCulture),
        Nature = t.Nature.ToString(),
        IdTradeOrigine = t.IdTradeOrigine?.ToString(),
        Structure = t.TypeStructure.ToString(),
        Description = t.Description,
        Sens = t.Sens.ToString(),
        Quantite = t.Quantite,
        Instrument = j.Instrument,
        NatureJambe = j.Nature.ToString(),
        QuantiteSignee = j.QuantiteSignee,
        PrixExecution = j.PrixExecution,
        FutureAuTrade = j.Future,
        VolAuTrade = j.Vol,
        PrimeTradeBtc = t.PrimeBtc,
        IdJambe = j.Id,
        IdJambeOrigine = j.IdJambeOrigine,
        NotionnelUsd = j.Nature == NatureJambe.Future ? j.NotionnelUsd : null,
        Devise = t.Devise
    });

    /// <summary>
    /// We export one line per position and leave unavailable valuations empty instead of
    /// writing a misleading number.
    /// </summary>
    public static IEnumerable<LigneBook> VersLignes(this EtatBook e) => e.Positions.Select(p => new LigneBook
    {
        IdTrade = p.Origine.Id.ToString(),
        Description = p.Origine.Description,
        Echeance = p.Origine.Echeance.ToString(FormatDate, System.Globalization.CultureInfo.InvariantCulture),
        QuantiteOuverte = p.QuantiteOuverte,
        ValeurMarkBtc = Disponible(p.ValeurMarkBtc),
        ValeurTheoriqueBtc = Disponible(p.ValeurTheoriqueBtc),
        PrimeOuverteBtc = p.PrimeOuverteBtc,
        PnlLatentBtc = Disponible(p.PnlLatentBtc),
        PnlRealiseBtc = p.PnlRealiseBtc,
        PnlTotalBtc = Disponible(p.PnlTotalBtc),
        PnlTotalUsd = Disponible(p.PnlTotalBtc * e.Indice),
        Delta = p.Grecques.Delta,
        Gamma = p.Grecques.Gamma,
        Vega = p.Grecques.Vega,
        Theta = p.Grecques.Theta,
        Statut = p.Statut,
        SourceMark = p.SourceMark,
        RisqueComplet = p.RisqueComplet,
        Devise = e.Devise
    });

    /// <summary>
    /// Sensitivities are exported by maturity and then totals, specifying whether the
    /// calculated risk is partial.
    /// </summary>
    public static IEnumerable<LigneGrecques> VersLignesGrecques(this EtatBook e) =>
        e.GrecquesParEcheance.Select(kv => new LigneGrecques(kv.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), kv.Value.Delta, kv.Value.Gamma, kv.Value.Vega, kv.Value.Theta, e.RisqueComplet ? "Complet" : "Partiel"))
         .Append(new LigneGrecques("TOTAL", e.GrecquesTotales.Delta, e.GrecquesTotales.Gamma, e.GrecquesTotales.Vega, e.GrecquesTotales.Theta, e.RisqueComplet ? "Complet" : "Partiel"));

    /// <summary>
    /// The remaining balances and the remaining notional of the futures are detailed to track
    /// residual hedges.
    /// </summary>
    public static IEnumerable<LignePositionJambe> VersJambesOuvertes(this EtatBook e) => e.Positions.SelectMany(p =>
        p.JambesOuvertes.Select(x => new LignePositionJambe(p.Origine.Id.ToString(), x.Jambe.Id, x.Jambe.Instrument,
            x.Jambe.Nature.ToString(), x.Jambe.Echeance.ToString(FormatDate), x.QuantiteOuverte,
            x.Jambe.Nature == NatureJambe.Future ? x.Jambe.NotionnelUsd * x.QuantiteOuverte / x.Jambe.QuantiteSignee : null,
            x.Jambe.PrixExecution, p.Statut)));

    /// <summary>
    /// Global measurements are produced with their unit and status to keep an export legible
    /// even if data is missing.
    /// </summary>
    public static IEnumerable<LigneSynthese> VersSynthese(this EtatBook e)
    {
        yield return new("Horodatage", "UTC", null, e.Horodatage.ToString(FormatDate));
        yield return new("Indice", "USD", Disponible(e.Indice), "");
        yield return new("Valeur mark", e.Devise, Disponible(e.ValeurMarkBtc), e.Statut);
        yield return new("Valeur théorique", e.Devise, Disponible(e.ValeurTheoriqueBtc), e.Statut);
        yield return new("P&L latent", e.Devise, Disponible(e.PnlLatentBtc), e.Statut);
        yield return new("P&L réalisé", e.Devise, e.PnlRealiseBtc, "");
        yield return new("P&L total", e.Devise, Disponible(e.PnlTotalBtc), e.Statut);
        yield return new("P&L total", "USD", Disponible(e.PnlTotalUsd), e.Statut);
        string risque = e.RisqueComplet ? "Complet" : "Partiel";
        yield return new("Delta", "sous-jacent", e.GrecquesTotales.Delta, risque);
        yield return new("Gamma", "par USD", e.GrecquesTotales.Gamma, risque);
        yield return new("Vega", "USD / point", e.GrecquesTotales.Vega, risque);
        yield return new("Theta", "USD / jour", e.GrecquesTotales.Theta, risque);
    }
    /// <summary>
    /// An unfinished value is replaced by an absent value, so that exports do not show invalid
    /// valuation as a result.
    /// </summary>
    private static double? Disponible(double x) => double.IsFinite(x) ? x : null;
}
