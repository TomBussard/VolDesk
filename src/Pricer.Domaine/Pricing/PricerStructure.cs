using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Structures;

namespace Pricer.Domaine.Pricing;

/// <summary>On compare notre volatilité inversée à celle publiée par Deribit pour rendre les écarts visibles.</summary>
public sealed record ComparaisonVol(ResultatVol Calculee, double? Deribit)
{
    /// <summary>
    /// On exprime la différence en points de volatilité : notre sigma décimal est multiplié par 100 avant la
    /// comparaison.
    /// </summary>
    public double? EcartPoints => Calculee.EstCalculee && Deribit is double d ? Calculee.Sigma!.Value * 100 - d : null;
    public bool Signale => EcartPoints is double e && Math.Abs(e) > PricerStructure.SeuilEcartVolPoints;
}

/// <summary>
/// On regroupe le calcul et la cotation d'une jambe afin d'afficher les prix, les IV et leurs écarts sur la
/// même ligne.
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
/// On conserve le résultat complet de la structure pour un sens et une quantité donnés, avec ses jambes et
/// son horodatage.
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
    /// On additionne les prix crypto pondérés par les ratios pour obtenir le prix théorique d'une unité de
    /// structure achetée.
    /// </summary>
    public double TheoriqueUnitaireBtc => Lignes.Sum(l => l.Jambe.Ratio * l.Theorique.PrixBtc);
    public double TheoriqueUnitaireUsd => Lignes.Sum(l => l.Jambe.Ratio * l.Theorique.PrixUsd);
    public double? MarkUnitaireBtc => Lignes.All(l => l.Cotation?.Mark is not null) ? Lignes.Sum(l => l.Jambe.Ratio * l.Cotation!.Mark!.Value) : null;

    /// <summary>
    /// On rend la structure négociable uniquement si chaque jambe possède un carnet valide et une cotation
    /// récente.
    /// </summary>
    public bool Traitable => Lignes.All(l => l.Traitable);

    /// <summary>
    /// On paie l'ask des jambes achetées et on reçoit le bid des jambes vendues lorsque l'on achète une unité
    /// de structure.
    /// </summary>
    public double? PrixExecutableAchatBtc => Traitable
        ? Lignes.Sum(l => l.Jambe.Ratio > 0 ? l.Jambe.Ratio * l.Cotation!.Ask!.Value : l.Jambe.Ratio * l.Cotation!.Bid!.Value)
        : null;

    /// <summary>
    /// On inverse les côtés du carnet à la vente : on reçoit le bid des jambes positives et on paie l'ask des
    /// jambes négatives.
    /// </summary>
    public double? PrixExecutableVenteBtc => Traitable
        ? Lignes.Sum(l => l.Jambe.Ratio > 0 ? l.Jambe.Ratio * l.Cotation!.Bid!.Value : l.Jambe.Ratio * l.Cotation!.Ask!.Value)
        : null;

    /// <summary>
    /// On mesure la fourchette de la structure par la différence entre son exécutable à l'achat et son
    /// exécutable à la vente.
    /// </summary>
    public double? FourchetteBtc => PrixExecutableAchatBtc - PrixExecutableVenteBtc;

    /// <summary>On retient le côté exécutable correspondant au sens demandé, sans changer le calcul des jambes.</summary>
    public double? PrixExecutableBtc => Sens == Sens.Achat ? PrixExecutableAchatBtc : PrixExecutableVenteBtc;

    /// <summary>
    /// On calcule la prime signée de toute la quantité ; une valeur positive correspond à une prime payée par
    /// le desk.
    /// </summary>
    public double? PrimeTotaleBtc => PrixExecutableBtc * (int)Sens * Quantite;

    public Grecques Grecques => Lignes.Aggregate(Grecques.Zero, (acc, l) => acc + l.QuantiteSignee * l.Theorique.Grecques);

    public Grecques? GrecquesDeribit => Lignes.All(l => l.Cotation?.GrecquesDeribit is not null)
        ? Lignes.Aggregate(Grecques.Zero, (acc, l) => acc + l.QuantiteSignee * l.Cotation!.GrecquesDeribit!.Value)
        : null;
}

/// <summary>
/// On cote les jambes avec Black-76, puis on regroupe leurs prix, leurs IV recalculées et leurs grecques.
/// On sépare le prix théorique du prix réellement exécutable.
/// </summary>
public static class PricerStructure
{
    public const double SeuilEcartPrixBtc = 0.0001;
    public const double SeuilEcartVolPoints = 0.1;

    /// <summary>
    /// On valide les quantités, on cote chaque jambe avec son ratio et le sens du desk, puis on rassemble le
    /// résultat de la structure.
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
    /// On calcule le théorique et les IV d'une jambe cotée ; pour l'OTC, on garde seulement les paramètres
    /// interpolés et le modèle.
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
