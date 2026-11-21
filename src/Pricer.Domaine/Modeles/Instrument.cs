namespace Pricer.Domaine.Modeles;

/// <summary>
/// On conserve les caractéristiques de l'option dans le référentiel. On utilise required et init pour
/// exiger les champs nécessaires dès la construction et éviter de les modifier ensuite.
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

    /// <summary>On retient la quantité minimale comme pas de quantité de l'option Deribit.</summary>
    public double PasQuantite => QuantiteMinimale;
}

/// <summary>On décrit le future coté séparément des options, car sa quantité se traite en montant USD.</summary>
public sealed record InstrumentFuture
{
    public required string Nom { get; init; }
    public required string Devise { get; init; }
    /// <summary>
    /// On garde l'échéance du future ; on représente le perpétuel par DateTime.MaxValue, car il n'a pas de date
    /// de livraison.
    /// </summary>
    public required DateTime Echeance { get; init; }
    /// <summary>On conserve la taille d'un contrat en USD ; elle vaut notamment 10 USD pour les futures BTC du périmètre.</summary>
    public double TailleContratUsd { get; init; } = 10.0;
    public double QuantiteMinimaleUsd { get; init; } = 10.0;
    public bool EstPerpetuel => Echeance == DateTime.MaxValue;
}
