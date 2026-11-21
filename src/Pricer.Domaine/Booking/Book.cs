using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Structures;

namespace Pricer.Domaine.Booking;

/// <summary>On rassemble les valeurs, le risque et le statut d’une ouverture, y compris les couvertures encore vivantes.</summary>
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

/// <summary>On présente un état cohérent du book à un instant, avec les risques regroupés par échéance.</summary>
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

/// <summary>On valorise l’historique du blotter sans supprimer les opérations réglées, pour conserver leur résultat réalisé.</summary>
/// <param name="blotter">L'historique partagé dont on lit les positions et les événements de fermeture.</param>
public sealed class Book(Blotter blotter)
{
    public Blotter Blotter { get; } = blotter;
    /// <summary>
    /// On valorise le book sous le verrou du blotter pour garder les positions et les résultats cohérents
    /// pendant la lecture.
    /// </summary>
    public EtatBook Evaluer(IContexteMarche contexte) => Blotter.Lire(() => EvaluerCoherent(contexte));
    /// <summary>
    /// On réunit les positions évaluées et leurs risques par échéance, puis on convertit le P&amp;L total
    /// avec l'indice courant.
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
    /// On liste les instruments cotés encore ouverts pour demander leurs mises à jour lors du chargement du
    /// book.
    /// </summary>
    public HashSet<string> InstrumentsSuivis() => Blotter.Ouvertures.SelectMany(o => o.Jambes
        .Where(j => j.Nature != NatureJambe.OptionSurMesure && Blotter.QuantiteOuverteJambe(o.Id, j.Id) != 0))
        .Select(j => j.Instrument).ToHashSet();

    /// <summary>
    /// On recherche les jambes échues sous le verrou du blotter et on ne les règle que si leur prix officiel
    /// est disponible.
    /// </summary>
    /// <returns>Les événements de règlement créés pour les échéances disposant d'un prix valide.</returns>
    public IReadOnlyList<Trade> ReglerEcheances(DateTime maintenant, IReadOnlyDictionary<DateOnly, double> prixReglement) =>
        Blotter.Lire(() => ReglerCoherent(maintenant, prixReglement));
    /// <summary>
    /// On parcourt les dates échues de chaque ouverture et on règle chaque date séparément pour conserver
    /// les autres jambes.
    /// </summary>
    private IReadOnlyList<Trade> ReglerCoherent(DateTime maintenant, IReadOnlyDictionary<DateOnly, double> prixReglement)
    {
        var resultat = new List<Trade>();
        foreach (var o in Blotter.Ouvertures)
            foreach (var date in o.Jambes.Where(j => j.Echeance <= maintenant && Blotter.QuantiteOuverteJambe(o.Id, j.Id) != 0)
                .Select(j => j.Echeance).Distinct().OrderBy(d => d))
            {
                // On règle chaque échéance séparément, sans fermer les couvertures encore vivantes.
                if (prixReglement.TryGetValue(DateOnly.FromDateTime(date), out double prix) && double.IsFinite(prix) && prix > 0)
                    resultat.Add(Blotter.Regler(o.Id, prix, date));
            }
        return resultat;
    }

    /// <summary>
    /// On sépare le réalisé des fermetures et la valeur des jambes encore ouvertes ; une donnée absente rend
    /// la valorisation incomplète.
    /// </summary>
    private PositionStructure EvaluerPosition(Trade o, IContexteMarche contexte)
    {
        var ouvertes = o.Jambes.Select(j => (Jambe: j, QuantiteOuverte: Blotter.QuantiteOuverteJambe(o.Id, j.Id)))
            .Where(j => j.QuantiteOuverte != 0).ToList();
        double realise = 0, mark = 0, theo = 0;
        // On rattache chaque résultat réalisé à sa jambe d’origine, même si plusieurs trades portent sur le même instrument.
        foreach (var fermeture in Blotter.TradesLies(o.Id).SelectMany(t => t.Jambes))
        {
            var j = o.Jambes.Single(j => j.Id == fermeture.IdJambeOrigine);
            if (j.Nature == NatureJambe.Future)
            {
                // On utilise le notionnel USD fermé et les inverses de prix, conformément au règlement crypto du future.
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
                // On ne remplace jamais une valeur de marché absente par zéro ou par le prix d'entrée.
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
    /// On valorise une unité dans les conventions de la jambe : modèle pour l'OTC, mark pour le coté et
    /// P&amp;L inverse pour le future.
    /// </summary>
    /// <returns>La valeur au mark, la valeur théorique et les grecques ; les valeurs sont en crypto, les grecques suivent les conventions USD.</returns>
    private static (double Mark, double Theorique, Grecques Grecques) ValeurUnitaire(JambeExecutee j, IContexteMarche contexte)
    {
        if (j.Nature == NatureJambe.OptionSurMesure)
        {
            // On utilise explicitement le modèle pour l’OTC, puisqu’aucun mark Deribit de ce contrat n’existe.
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
