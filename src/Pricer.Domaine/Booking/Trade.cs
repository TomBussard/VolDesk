using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Booking;

/// <summary>
/// On conserve les données de chaque jambe au moment de son exécution pour pouvoir expliquer les résultats
/// du book.
/// </summary>
public sealed record JambeExecutee
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? IdJambeOrigine { get; init; }
    public bool Couverture { get; init; }
    public double PasQuantite { get; init; } = 0.1;
    public double PasNotionnelUsd { get; init; } = 10;
    /// <summary>On conserve le montant USD signé du future inverse, fixé au trade.</summary>
    public double NotionnelUsd { get; init; }
    public required string Instrument { get; init; }
    public required NatureJambe Nature { get; init; }
    public TypeOption? Type { get; init; }
    public double Strike { get; init; }
    public required DateTime Echeance { get; init; }
    /// <summary>On porte le sens du desk dans le signe de la quantité ; les quantités d'options sont en unités de crypto.</summary>
    public required double QuantiteSignee { get; init; }
    /// <summary>
    /// On enregistre le prix d'une option en crypto et celui d'un future en USD, car leurs cotations n'ont pas
    /// la même unité.
    /// </summary>
    public required double PrixExecution { get; init; }
    /// <summary>On mémorise le future utilisé au moment du trade pour rendre la cotation traçable.</summary>
    public required double Future { get; init; }
    /// <summary>
    /// On garde la volatilité en décimale ; on utilise null pour le future, qui n'a pas de volatilité d'option
    /// à enregistrer.
    /// </summary>
    public double? Vol { get; init; }

    /// <summary>On compte une prime positive quand le desk paie des cryptos ; un future n'échange pas de prime d'option.</summary>
    public double PrimeBtc => Nature == NatureJambe.Future ? 0 : QuantiteSignee * PrixExecution;
}

public enum NatureTrade
{
    Ouverture,
    Debouclage,
    Reglement
}

/// <summary>
/// On ajoute un événement pour chaque ouverture, fermeture ou règlement. Le lien avec le trade d'origine
/// permet de conserver tout l'historique.
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

    /// <summary>On additionne les primes signées des jambes ; un total positif correspond à une somme payée par le desk.</summary>
    public double PrimeBtc => Jambes.Sum(j => j.PrimeBtc);
}
