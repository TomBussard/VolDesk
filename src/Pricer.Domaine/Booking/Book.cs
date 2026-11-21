using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Structures;

namespace Pricer.Domaine.Booking;

/// <summary>
/// The values, the risk and the status of an opening, including still alive covers, are grouped
/// together.
/// </summary>
public sealed record PositionStructure
{
    public required Trade Origine { get; init; }
    public required double QuantiteOuverte { get; init; }
    public required double ValeurMarkBtc { get; init; }
    public required double ValeurTheoriqueBtc { get; init; }
    public required double PrimeOuverteBtc { get; init; }
    public required double PnlRealiseBtc { get; init; }
    public required Grecques Grecques { get; init; }
    public required IReadOnlyList<(JambeExecutee Jambe, double QuantiteOuverte)> JambesOuvertes { get; init; }
    public IReadOnlyDictionary<DateTime, Grecques> RisquesParEcheance { get; init; } = new Dictionary<DateTime, Grecques>();
    public bool ValorisationComplete { get; init; } = true;
    public bool RisqueComplet { get; init; } = true;
    public string Statut { get; init; } = "Ouvert";
    public string SourceMark { get; init; } = "Deribit";
    public double PnlLatentBtc => ValeurMarkBtc - PrimeOuverteBtc;
    public double PnlLatentTheoriqueBtc => ValeurTheoriqueBtc - PrimeOuverteBtc;
    public double PnlTotalBtc => PnlLatentBtc + PnlRealiseBtc;
    public bool EstOuverte => QuantiteOuverte > 0 || JambesOuvertes.Count > 0;
}

/// <summary>
/// A consistent statement of the book is presented at a moment, with the risks grouped by
/// maturity.
/// </summary>
public sealed record EtatBook
{
    public string Devise { get; init; } = "BTC";
    public required DateTime Horodatage { get; init; }
    public required double Indice { get; init; }
    public required IReadOnlyList<PositionStructure> Positions { get; init; }
    public required IReadOnlyDictionary<DateTime, Grecques> GrecquesParEcheance { get; init; }
    public IEnumerable<PositionStructure> Ouvertes => Positions.Where(p => p.EstOuverte);
    public bool ValorisationComplete => Positions.All(p => p.ValorisationComplete);
    public bool RisqueComplet => Positions.All(p => p.RisqueComplet);
    public string Statut => !ValorisationComplete ? "Valorisation incomplète - données manquantes ou périmées" : "Valorisation complète";
    public double ValeurMarkBtc => Ouvertes.Sum(p => p.ValeurMarkBtc);
    public double ValeurTheoriqueBtc => Ouvertes.Sum(p => p.ValeurTheoriqueBtc);
    public double PnlLatentBtc => Ouvertes.Sum(p => p.PnlLatentBtc);
    public double PnlRealiseBtc => Positions.Sum(p => p.PnlRealiseBtc);
    public double PnlTotalBtc => PnlLatentBtc + PnlRealiseBtc;
    public double PnlTotalUsd => Indice > 0 ? PnlTotalBtc * Indice : double.NaN;
    public Grecques GrecquesTotales => GrecquesParEcheance.Values.Aggregate(Grecques.Zero, (a, g) => a + g);
}

/// <summary>
/// The history of the float is valued without removing the settled operations, to keep their
/// realized result.
/// </summary>
/// <param name="blotter">L'historique partagé dont on lit les positions et les événements de fermeture.</param>
public sealed class Book(Blotter blotter)
{
    public Blotter Blotter { get; } = blotter;
    /// <summary>
    /// We value the book under the lock of the float to keep positions and results consistent
    /// during playback.
    /// </summary>
    public EtatBook Evaluer(IContexteMarche contexte) => Blotter.Lire(() => EvaluerCoherent(contexte));
    /// <summary>
    /// The positions assessed and their risks by maturity are combined and then converted to
    /// the total P&amp;L with current index.
    /// </summary>
    private EtatBook EvaluerCoherent(IContexteMarche contexte)
    {
        var positions = Blotter.Ouvertures.Select(o => EvaluerPosition(o, contexte)).ToList();
        var risques = positions.SelectMany(p => p.RisquesParEcheance).GroupBy(r => r.Key)
            .OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Aggregate(Grecques.Zero, (a, r) => a + r.Value));
        return new EtatBook
        {
            Devise = contexte.Referentiel.Devise,
            Horodatage = contexte.Maintenant,
            Indice = contexte.Indice ?? double.NaN,
            Positions = positions,
            GrecquesParEcheance = risques
        };
    }
    /// <summary>
    /// List the listed instruments still open to request their updates when loading the book.
    /// </summary>
    public HashSet<string> InstrumentsSuivis() => Blotter.Ouvertures.SelectMany(o => o.Jambes
        .Where(j => j.Nature != NatureJambe.OptionSurMesure && Blotter.QuantiteOuverteJambe(o.Id, j.Id) != 0))
        .Select(j => j.Instrument).ToHashSet();

    /// <summary>
    /// We look for the fallen legs under the lock of the float and we only settle them if their
    /// official price is available.
    /// </summary>
    /// <returns>Les événements de règlement créés pour les échéances disposant d'un prix valide.</returns>
    public IReadOnlyList<Trade> ReglerEcheances(DateTime maintenant, IReadOnlyDictionary<DateOnly, double> prixReglement) =>
        Blotter.Lire(() => ReglerCoherent(maintenant, prixReglement));
    /// <summary>
    /// Timed dates of each opening are reviewed and each date is set separately to keep the
    /// other legs.
    /// </summary>
    private IReadOnlyList<Trade> ReglerCoherent(DateTime maintenant, IReadOnlyDictionary<DateOnly, double> prixReglement)
    {
        var resultat = new List<Trade>();
        foreach (var o in Blotter.Ouvertures)
            foreach (var date in o.Jambes.Where(j => j.Echeance <= maintenant && Blotter.QuantiteOuverteJambe(o.Id, j.Id) != 0)
                .Select(j => j.Echeance).Distinct().OrderBy(d => d))
            {
                // Each maturity is settled separately, without closing the still alive hedges.
                if (prixReglement.TryGetValue(DateOnly.FromDateTime(date), out double prix) && double.IsFinite(prix) && prix > 0)
                    resultat.Add(Blotter.Regler(o.Id, prix, date));
            }
        return resultat;
    }

    /// <summary>
    /// We separate the realization from the closures and the value of the legs still open; a
    /// lack of data makes the valorisation incomplete.
    /// </summary>
    private PositionStructure EvaluerPosition(Trade o, IContexteMarche contexte)
    {
        var ouvertes = o.Jambes.Select(j => (Jambe: j, QuantiteOuverte: Blotter.QuantiteOuverteJambe(o.Id, j.Id)))
            .Where(j => j.QuantiteOuverte != 0).ToList();
        double realise = 0, mark = 0, theo = 0;
        // Each result is attached to its original leg, even if several trades are related to the same instrument.
        foreach (var fermeture in Blotter.TradesLies(o.Id).SelectMany(t => t.Jambes))
        {
            var j = o.Jambes.Single(j => j.Id == fermeture.IdJambeOrigine);
            if (j.Nature == NatureJambe.Future)
            {
                // The notional closed USD and the price inverses are used in accordance with the crypto settlement of the future.
                double montantFerme = fermeture.NotionnelUsd != 0 ? fermeture.NotionnelUsd : fermeture.QuantiteSignee * j.PrixExecution;
                realise += -montantFerme * (1 / j.PrixExecution - 1 / fermeture.PrixExecution);
            }
            else realise += -fermeture.QuantiteSignee * (fermeture.PrixExecution - j.PrixExecution);
        }
        var grecques = Grecques.Zero;
        var risques = new Dictionary<DateTime, Grecques>();
        var alertes = new List<string>();
        bool valeurComplete = true, risqueComplet = true;
        foreach (var (j, q) in ouvertes)
        {
            try
            {
                if (j.Echeance <= contexte.Maintenant) throw new PricerException($"{j.Instrument} : prix de règlement attendu.");
                var valeur = ValeurUnitaire(j, contexte);
                mark += q * valeur.Mark; theo += q * valeur.Theorique;
                var risque = q * valeur.Grecques;
                grecques += risque;
                risques[j.Echeance] = risques.GetValueOrDefault(j.Echeance, Grecques.Zero) + risque;
            }
            catch (Exception ex) when (ex is PricerException or ArgumentException or InvalidOperationException)
            {
                // An absent market value is never replaced by zero or the entry price.
                valeurComplete = false; risqueComplet = false; alertes.Add(ex.Message);
            }
        }
        double qStructure = Blotter.QuantiteOuverte(o.Id);
        string statut = ouvertes.Count == 0 ? "Clôturé / réglé" : qStructure == 0 ? "Couverture résiduelle à déboucler" : "Ouvert";
        if (alertes.Count > 0) statut += " - " + string.Join(" ; ", alertes.Distinct());
        return new PositionStructure
        {
            Origine = o,
            QuantiteOuverte = qStructure,
            JambesOuvertes = ouvertes,
            // On propage une valeur indisponible au total ; afficher zéro donnerait un résultat trompeur.
            ValeurMarkBtc = valeurComplete ? mark : double.NaN,
            ValeurTheoriqueBtc = valeurComplete ? theo : double.NaN,
            PrimeOuverteBtc = ouvertes.Where(x => x.Jambe.Nature != NatureJambe.Future).Sum(x => x.QuantiteOuverte * x.Jambe.PrixExecution),
            PnlRealiseBtc = realise,
            Grecques = grecques,
            RisquesParEcheance = risques,
            ValorisationComplete = valeurComplete,
            RisqueComplet = risqueComplet,
            Statut = statut,
            SourceMark = o.Jambes.Any(j => j.Nature == NatureJambe.OptionSurMesure) ? "Deribit + théorie OTC" : "Deribit"
        };
    }

    /// <summary>
    /// One unit is valued in the leg conventions: model for the CTA, mark for the side and
    /// reverse P&amp;L for the future.
    /// </summary>
    /// <returns>La valeur au mark, la valeur théorique et les grecques ; les valeurs sont en crypto, les grecques suivent les conventions USD.</returns>
    private static (double Mark, double Theorique, Grecques Grecques) ValeurUnitaire(JambeExecutee j, IContexteMarche contexte)
    {
        if (j.Nature == NatureJambe.OptionSurMesure)
        {
            // The model is explicitly used for the CTA, as there is no Deribit mark in this contract.
            var r = new JambeSurMesure(j.Type!.Value, j.Strike, j.Echeance, 1).Evaluer(contexte);
            return (r.PrixBtc, r.PrixBtc, r.Grecques);
        }
        var cot = contexte.Cotation(j.Instrument) ?? throw new PricerException($"{j.Instrument} : cotation absente.");
        cot.VerifierFraicheur(contexte.Maintenant);
        if (cot.Mark is not double m || !double.IsFinite(m) || m < 0 || (j.Nature == NatureJambe.Future && m == 0))
            throw new PricerException($"{j.Instrument} : mark absent ou invalide.");
        if (j.Nature == NatureJambe.Future)
        {
            double prixEntree = Blotter.NotionnelInitial(j) / j.QuantiteSignee;
            double pnl = prixEntree * (1 / j.PrixExecution - 1 / m);
            return (pnl, pnl, new Grecques(prixEntree / m, 0, 0, 0));
        }
        var modele = new JambeCotee(contexte.Referentiel.Obtenir(j.Instrument), 1).Evaluer(contexte);
        return (m, modele.PrixBtc, modele.Grecques);
    }
}
