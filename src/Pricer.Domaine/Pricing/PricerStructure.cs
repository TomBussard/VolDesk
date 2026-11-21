using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Structures;

namespace Pricer.Domaine.Pricing;

/// <summary>
/// We compare our reverse volatility to that published by Deribit to make the deviations
/// visible.
/// </summary>
public sealed record ComparaisonVol(ResultatVol Calculee, double? Deribit)
{
    /// <summary>
    /// The difference in volatility points is expressed: our decimal sigma is multiplied by 100
    /// before the comparison.
    /// </summary>
    public double? EcartPoints => Calculee.EstCalculee && Deribit is double d ? Calculee.Sigma!.Value * 100 - d : null;
    public bool Signale => EcartPoints is double e && Math.Abs(e) > PricerStructure.SeuilEcartVolPoints;
}

/// <summary>
/// The calculation and quote of a leg is grouped together to display prices, IVs and their
/// deviations on the same line.
/// </summary>
public sealed record LigneJambe
{
    public required Jambe Jambe { get; init; }
    public required double QuantiteSignee { get; init; }
    public required ParametresMarche Parametres { get; init; }
    public required ResultatBlack76 Theorique { get; init; }
    public Cotation? Cotation { get; init; }
    public ComparaisonVol? VolBid { get; init; }
    public ComparaisonVol? VolMark { get; init; }
    public ComparaisonVol? VolAsk { get; init; }

    public double? EcartMarkBtc => Cotation?.Mark is double m ? Theorique.PrixBtc - m : null;
    public bool EcartMarkSignale => EcartMarkBtc is double e && Math.Abs(e) > PricerStructure.SeuilEcartPrixBtc;
    public bool Traitable => Jambe is ICotableSurMarche c && c.Instrument.Actif && c.Instrument.Etat == "open" &&
        Cotation?.FourchetteValide == true && Parametres.Maturite > 0;
}

/// <summary>
/// The complete result of the structure is retained for a given meaning and quantity, with its
/// legs and time stamping.
/// </summary>
public sealed record CotationStructure
{
    public required Structure Structure { get; init; }
    public required Sens Sens { get; init; }
    public required double Quantite { get; init; }
    public required DateTime Horodatage { get; init; }
    public required IReadOnlyList<LigneJambe> Lignes { get; init; }
    public required double Future { get; init; }

    /// <summary>
    /// The ratio weighted crypto prices are added together to obtain the theoretical price of a
    /// unit of structure purchased.
    /// </summary>
    public double TheoriqueUnitaireBtc => Lignes.Sum(l => l.Jambe.Ratio * l.Theorique.PrixBtc);
    public double TheoriqueUnitaireUsd => Lignes.Sum(l => l.Jambe.Ratio * l.Theorique.PrixUsd);
    public double? MarkUnitaireBtc => Lignes.All(l => l.Cotation?.Mark is not null) ? Lignes.Sum(l => l.Jambe.Ratio * l.Cotation!.Mark!.Value) : null;

    /// <summary>
    /// The structure is made negotiable only if each leg has a valid notebook and a recent
    /// listing.
    /// </summary>
    public bool Traitable => Lignes.All(l => l.Traitable);

    /// <summary>
    /// You pay for the legs you bought and you get the bid from the legs you sold when you buy
    /// a structural unit.
    /// </summary>
    public double? PrixExecutableAchatBtc => Traitable
        ? Lignes.Sum(l => l.Jambe.Ratio > 0 ? l.Jambe.Ratio * l.Cotation!.Ask!.Value : l.Jambe.Ratio * l.Cotation!.Bid!.Value)
        : null;

    /// <summary>
    /// The sides of the notebook are reversed at the sale: you receive the bid of the positive
    /// legs and you pay the ask of the negative legs.
    /// </summary>
    public double? PrixExecutableVenteBtc => Traitable
        ? Lignes.Sum(l => l.Jambe.Ratio > 0 ? l.Jambe.Ratio * l.Cotation!.Bid!.Value : l.Jambe.Ratio * l.Cotation!.Ask!.Value)
        : null;

    /// <summary>
    /// The range of the structure is measured by the difference between its executable on
    /// purchase and its executable on sale.
    /// </summary>
    public double? FourchetteBtc => PrixExecutableAchatBtc - PrixExecutableVenteBtc;

    /// <summary>
    /// The executable side corresponding to the required direction is retained, without
    /// changing the calculation of the legs.
    /// </summary>
    public double? PrixExecutableBtc => Sens == Sens.Achat ? PrixExecutableAchatBtc : PrixExecutableVenteBtc;

    /// <summary>
    /// The signed premium of the entire quantity is calculated; a positive value is a premium
    /// paid by the desk.
    /// </summary>
    public double? PrimeTotaleBtc => PrixExecutableBtc * (int)Sens * Quantite;

    public Grecques Grecques => Lignes.Aggregate(Grecques.Zero, (acc, l) => acc + l.QuantiteSignee * l.Theorique.Grecques);

    public Grecques? GrecquesDeribit => Lignes.All(l => l.Cotation?.GrecquesDeribit is not null)
        ? Lignes.Aggregate(Grecques.Zero, (acc, l) => acc + l.QuantiteSignee * l.Cotation!.GrecquesDeribit!.Value)
        : null;
}

/// <summary>
/// We score the legs with Black-76, then we group their prices, their recalculated IVs and
/// their Greeks. We separate the theoretical price from the actually executable price.
/// </summary>
public static class PricerStructure
{
    public const double SeuilEcartPrixBtc = 0.0001;
    public const double SeuilEcartVolPoints = 0.1;

    /// <summary>
    /// We validate the quantities, we rate each leg with its ratio and the direction of the
    /// desk, then gather the result of the structure.
    /// </summary>
    public static CotationStructure Coter(Structure structure, Sens sens, double quantite, IContexteMarche contexte)
    {
        ValidateurStructure.ValiderQuantite(quantite, contexte.Referentiel.Devise == "ETH" ? 1 : 0.1);
        foreach (var j in structure.Jambes)
            if (j is ICotableSurMarche c)
                ValidateurStructure.ValiderQuantite(Math.Abs(j.Ratio * quantite), c.Instrument.PasQuantite);
        var lignes = structure.Jambes.Select(j => CoterJambe(j, j.Ratio * (int)sens * quantite, contexte)).ToList();
        return new CotationStructure
        {
            Structure = structure,
            Sens = sens,
            Quantite = quantite,
            Horodatage = contexte.Maintenant,
            Lignes = lignes,
            Future = lignes[0].Parametres.Future
        };
    }

    /// <summary>
    /// The theory and IV of a sided leg are calculated; for the CTA, only the interpolated
    /// parameters and the model are kept.
    /// </summary>
    public static LigneJambe CoterJambe(Jambe jambe, double quantiteSignee, IContexteMarche contexte)
    {
        var parametres = jambe.ParametresMarche(contexte);
        var theorique = Black76.Calculer(parametres.Future, jambe.Strike, parametres.Maturite, parametres.Sigma, jambe.Type);

        if (jambe is not ICotableSurMarche cotable)
            return new LigneJambe { Jambe = jambe, QuantiteSignee = quantiteSignee, Parametres = parametres, Theorique = theorique };

        var cotation = cotable.ObtenirCotation(contexte);
        ComparaisonVol Comparer(double? prixBtc, double? ivDeribit) => new(
            VolImplicite.CalculerDepuisBtc(prixBtc, parametres.Future, jambe.Strike, parametres.Maturite, jambe.Type),
            ivDeribit is > 0 ? ivDeribit : null); // On écarte l'IV nulle utilisée par Deribit quand le prix manque.

        return new LigneJambe
        {
            Jambe = jambe,
            QuantiteSignee = quantiteSignee,
            Parametres = parametres,
            Theorique = theorique,
            Cotation = cotation,
            VolBid = Comparer(cotation.Bid, cotation.BidIv),
            VolMark = Comparer(cotation.Mark, cotation.MarkIv),
            VolAsk = Comparer(cotation.Ask, cotation.AskIv)
        };
    }
}
