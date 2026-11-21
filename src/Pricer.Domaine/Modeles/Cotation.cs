namespace Pricer.Domaine.Modeles;

/// <summary>
/// We represent a market message without changing its values after receipt. Options prices are
/// in crypto, futures and index in USD, and IVs in percentage as in API. We keep null when a
/// price is missing.
/// </summary>
public sealed record Cotation
{
    public required string NomInstrument { get; init; }
    public required DateTime Horodatage { get; init; }
    public double? Bid { get; init; }
    public double? Ask { get; init; }
    public double? Mark { get; init; }
    public double? BidIv { get; init; }
    public double? MarkIv { get; init; }
    public double? AskIv { get; init; }
    /// <summary>
    /// Deribit supplies the expiry-specific forward in underlying_price. Black-76 uses that forward as its underlying.
    /// </summary>
    public double? Future { get; init; }
    public double? Indice { get; init; }
    public Grecques? GrecquesDeribit { get; init; }
    /// <summary>
    /// A complete ticker is distinguished from a summary to find out which fields have
    /// actually been transmitted.
    /// </summary>
    public bool EstComplete { get; init; } = true;

    public string Etat { get; init; } = "open";
    public bool FourchetteValide => Bid is double b && Ask is double a &&
        double.IsFinite(b) && double.IsFinite(a) && b > 0 && a >= b && Etat == "open";
    public double? Mid => FourchetteValide ? (Ask!.Value + Bid!.Value) / 2 : null;

    /// <summary>
    /// We accept an age of less than five to thirty seconds to tolerate a slight clock shift
    /// and refuse expired prices.
    /// </summary>
    public bool EstFraiche(DateTime maintenant) => (maintenant - Horodatage).TotalSeconds is >= -5 and <= 30;

    /// <summary>
    /// The calculation is blocked with an explicit message when the rating comes out of the
    /// cool window.
    /// </summary>
    public void VerifierFraicheur(DateTime maintenant)
    {
        if (!EstFraiche(maintenant))
            throw new Pricer.Domaine.Exceptions.DonneesIndisponiblesException($"Cotation absente de la fenêtre de fraîcheur (-5 à 30 s) : {NomInstrument}.");
    }
}

/// <summary>
/// The official delivery price is linked to its date to adjust only the legs concerned.
/// </summary>
public sealed record PrixReglement(DateOnly Date, double Prix);
