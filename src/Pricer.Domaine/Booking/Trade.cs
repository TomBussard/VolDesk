using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Booking;

/// <summary>
/// The data for each leg is kept at the time of execution to explain the results of the book.
/// </summary>
public sealed record JambeExecutee
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? IdJambeOrigine { get; init; }
    public bool Couverture { get; init; }
    public double PasQuantite { get; init; } = 0.1;
    public double PasNotionnelUsd { get; init; } = 10;
    /// <summary>
    /// The signed USD amount of the future reverse, set to the trade, is retained.
    /// </summary>
    public double NotionnelUsd { get; init; }
    public required string Instrument { get; init; }
    public required NatureJambe Nature { get; init; }
    public TypeOption? Type { get; init; }
    public double Strike { get; init; }
    public required DateTime Echeance { get; init; }
    /// <summary>
    /// We carry the direction of the desk in the quantity sign; the quantities of options are
    /// in crypto units.
    /// </summary>
    public required double QuantiteSignee { get; init; }
    /// <summary>
    /// The price of a crypto option is recorded and the price of a future in USD is recorded,
    /// as their quotes do not have the same unit.
    /// </summary>
    public required double PrixExecution { get; init; }
    /// <summary>
    /// We memorize the future used at the time of the trade to make the quote traceable.
    /// </summary>
    public required double Future { get; init; }
    /// <summary>
    /// We keep the volatility in decimal places; we use null for the future, which has no
    /// option volatility to record.
    /// </summary>
    public double? Vol { get; init; }

    /// <summary>
    /// A positive bonus is counted when the desk pays cryptos; a future does not exchange an
    /// option bonus.
    /// </summary>
    public double PrimeBtc => Nature == NatureJambe.Future ? 0 : QuantiteSignee * PrixExecution;
}

public enum NatureTrade
{
    Ouverture,
    Debouclage,
    Reglement
}

/// <summary>
/// An event is added for each opening, closing or settlement. The link with the original trade
/// keeps the entire history.
/// </summary>
public sealed record Trade
{
    public required Guid Id { get; init; }
    public required DateTime Horodatage { get; init; }
    public required NatureTrade Nature { get; init; }
    public required TypeStructure TypeStructure { get; init; }
    public required string Description { get; init; }
    public required Sens Sens { get; init; }
    public required double Quantite { get; init; }
    public required DateTime Echeance { get; init; }
    public required IReadOnlyList<JambeExecutee> Jambes { get; init; }
    public Guid? IdTradeOrigine { get; init; }
    public string Devise => Jambes.Any(j => j.Instrument.StartsWith("ETH", StringComparison.OrdinalIgnoreCase)) ? "ETH" : "BTC";

    /// <summary>
    /// Adds the signed bonuses from the legs; a positive total corresponds to a sum paid by the
    /// desk.
    /// </summary>
    public double PrimeBtc => Jambes.Sum(j => j.PrimeBtc);
}
