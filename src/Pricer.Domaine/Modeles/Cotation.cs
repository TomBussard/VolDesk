namespace Pricer.Domaine.Modeles;

/// <summary>
/// On représente un message de marché sans modifier ses valeurs après réception. Les prix d'options sont en
/// crypto, les futures et l'indice en USD, et les IV en pourcentage comme dans l'API. On garde null quand
/// un prix manque.
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
    /// On conserve le future de même échéance publié dans underlying_price, car Black-76 ne doit pas utiliser
    /// le spot à sa place.
    /// </summary>
    public double? Future { get; init; }
    public double? Indice { get; init; }
    public Grecques? GrecquesDeribit { get; init; }
    /// <summary>On distingue un ticker complet d'un résumé pour savoir quels champs ont réellement été transmis.</summary>
    public bool EstComplete { get; init; } = true;

    public string Etat { get; init; } = "open";
    public bool FourchetteValide => Bid is double b && Ask is double a &&
        double.IsFinite(b) && double.IsFinite(a) && b > 0 && a >= b && Etat == "open";
    public double? Mid => FourchetteValide ? (Ask!.Value + Bid!.Value) / 2 : null;

    /// <summary>
    /// On accepte un âge de moins cinq à trente secondes pour tolérer un léger décalage d'horloge et refuser
    /// les prix périmés.
    /// </summary>
    public bool EstFraiche(DateTime maintenant) => (maintenant - Horodatage).TotalSeconds is >= -5 and <= 30;

    /// <summary>
    /// On bloque le calcul avec un message explicite lorsque la cotation sort de la fenêtre de fraîcheur.
    /// </summary>
    public void VerifierFraicheur(DateTime maintenant)
    {
        if (!EstFraiche(maintenant))
            throw new Pricer.Domaine.Exceptions.DonneesIndisponiblesException($"Cotation absente de la fenêtre de fraîcheur (-5 à 30 s) : {NomInstrument}.");
    }
}

/// <summary>On associe le prix officiel de livraison à sa date pour régler uniquement les jambes concernées.</summary>
public sealed record PrixReglement(DateOnly Date, double Prix);
