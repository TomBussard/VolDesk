namespace Pricer.Domaine.Modeles;

/// <summary>
/// The features of the option are retained in the repository. Required and init are used to
/// require the necessary fields from the moment of construction and avoid subsequent
/// modification.
/// </summary>
public sealed record Instrument
{
    public required string Nom { get; init; }
    public required string Devise { get; init; }
    public required double Strike { get; init; }
    public required DateTime Echeance { get; init; }
    public required TypeOption Type { get; init; }
    public bool Actif { get; init; } = true;
    public string Etat { get; init; } = "open";
    public double TailleContrat { get; init; } = 1.0;
    public double QuantiteMinimale { get; init; } = 0.1;
    public double PasCotation { get; init; } = 0.0001;

    /// <summary>
    /// The minimum quantity is retained as not the quantity of the Deribit option.
    /// </summary>
    public double PasQuantite => QuantiteMinimale;
}

/// <summary>
/// The future rating is described separately from the options, as its quantity is treated as
/// USD.
/// </summary>
public sealed record InstrumentFuture
{
    public required string Nom { get; init; }
    public required string Devise { get; init; }
    /// <summary>
    /// We keep the expiry of the future; we represent the perpetual by DateTime.MaxValue,
    /// because it has no delivery date.
    /// </summary>
    public required DateTime Echeance { get; init; }
    /// <summary>
    /// The size of a contract is kept in USD, which is worth 10 USD for future BTCs on the
    /// perimeter.
    /// </summary>
    public double TailleContratUsd { get; init; } = 10.0;
    public double QuantiteMinimaleUsd { get; init; } = 10.0;
    public bool EstPerpetuel => Echeance == DateTime.MaxValue;
}
