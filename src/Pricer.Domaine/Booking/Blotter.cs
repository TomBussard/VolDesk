using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Booking;

/// <summary>
/// The openings and their fastenings are kept; the amounts per leg are monitored to manage the
/// covers separately.
/// </summary>
public sealed class Blotter
{
    public const double PasQuantite = 0.1;
    private readonly List<Trade> _trades = new();
    private readonly Dictionary<Guid, Trade> _parId = new();
    private readonly object _verrou = new();
    public event EventHandler<Trade>? TradeAjoute;
    public IReadOnlyList<Trade> Trades { get { lock (_verrou) return _trades.ToList(); } }
    public IReadOnlyList<Trade> Ouvertures => Trades.Where(t => t.Nature == NatureTrade.Ouverture).ToList();
    /// <summary>
    /// A trade is found by its identifier and an unknown reference is explicitly reported.
    /// </summary>
    public Trade Obtenir(Guid id)
    {
        lock (_verrou) return _parId.TryGetValue(id, out var t) ? t
            : throw new SaisieInvalideException(CodeErreurSaisie.TradeInconnu, "Trade introuvable.");
    }
    /// <summary>
    /// We find the debunkings and regulations associated with an opening to reconstitute its
    /// position.
    /// </summary>
    public IReadOnlyList<Trade> TradesLies(Guid id)
    {
        lock (_verrou) return _trades.Where(t => t.IdTradeOrigine == id).ToList();
    }
    /// <summary>
    /// The initial amount and the closures linked to this leg are added together to return its
    /// signed balance.
    /// </summary>
    /// <returns>Le solde dans les unités de la jambe d'origine ; les très petits résidus numériques sont ramenés à zéro.</returns>
    public double QuantiteOuverteJambe(Guid id, Guid idJambe)
    {
        var j = Obtenir(id).Jambes.First(j => j.Id == idJambe);
        double q = j.QuantiteSignee + TradesLies(id).SelectMany(t => t.Jambes)
            .Where(x => x.IdJambeOrigine == idJambe).Sum(x => x.QuantiteSignee);
        return Math.Abs(q) < 1e-9 ? 0 : q;
    }
    /// <summary>
    /// The remaining amount of structure is deducted from the balance of his main leg; the
    /// covers are followed separately.
    /// </summary>
    public double QuantiteOuverte(Guid id)
    {
        var o = Obtenir(id);
        var j = o.Jambes.FirstOrDefault(j => !j.Couverture) ?? o.Jambes[0];
        return o.Quantite * Math.Abs(QuantiteOuverteJambe(id, j.Id) / j.QuantiteSignee);
    }
    /// <summary>
    /// Check to see if a leg remains alive, including a blanket after CTA regulation.
    /// </summary>
    public bool ADesJambesOuvertes(Guid id) => Obtenir(id).Jambes.Any(j => QuantiteOuverteJambe(id, j.Id) != 0);
    /// <summary>
    /// A reading under the lock of the float is performed to prevent a booking from changing
    /// the quantities during calculation.
    /// </summary>
    /// <returns>Le résultat de la lecture, obtenu sur un historique stable.</returns>
    internal T Lire<T>(Func<T> lecture) { lock (_verrou) return lecture(); }

    /// <summary>
    /// The legs are recorded at the executable prices of the quote, after quantity and
    /// chronology checks.
    /// </summary>
    public Trade Booker(CotationStructure c)
    {
        lock (_verrou)
        {
            VerifierChronologie(c.Horodatage);
            if (!double.IsFinite(c.Quantite) || c.Quantite <= 0) throw new PricerException("La quantité doit être positive et finie.");
            if (!c.Traitable) throw new PricerException("Structure non traitable : bid/ask absent ou invalide.");
            var jambes = c.Lignes.Select(l =>
            {
                var i = ((ICotableSurMarche)l.Jambe).Instrument;
                ValidateurStructure.ValiderQuantite(Math.Abs(l.QuantiteSignee), i.PasQuantite);
                return new JambeExecutee
                {
                    Instrument = l.Jambe.Libelle,
                    Nature = NatureJambe.OptionCotee,
                    Type = l.Jambe.Type,
                    Strike = l.Jambe.Strike,
                    Echeance = l.Jambe.Echeance,
                    QuantiteSignee = l.QuantiteSignee,
                    PasQuantite = i.PasQuantite,
                    PrixExecution = l.QuantiteSignee >= 0 ? l.Cotation!.Ask!.Value : l.Cotation!.Bid!.Value,
                    Future = l.Parametres.Future,
                    Vol = l.Parametres.Sigma
                };
            }).ToList();
            return Ajouter(new Trade
            {
                Id = Guid.NewGuid(),
                Horodatage = c.Horodatage,
                Nature = NatureTrade.Ouverture,
                TypeStructure = c.Structure.Type,
                Description = c.Structure.Description,
                Sens = c.Sens,
                Quantite = c.Quantite,
                Echeance = c.Structure.Echeance,
                Jambes = jambes
            });
        }
    }
    /// <summary>
    /// The CTA and its hedge are recorded in the same trade to keep their link and input
    /// parameters.
    /// </summary>
    public Trade BookerSurMesure(PropositionCouverture p, DateTime horodatage)
    {
        lock (_verrou)
        {
            VerifierChronologie(horodatage);
            // The booking is limited to 08:00 UTC: the official price used in the regulation is daily, not hourly.
            if (p.Option.Echeance.TimeOfDay != TimeSpan.FromHours(8))
                throw new PricerException("Le règlement Deribit quotidien est à 08:00 UTC : une autre heure reste une simulation, sans booking.");
            double pas = p.Lignes.FirstOrDefault(l => l.Nature == NatureJambe.OptionCotee)?.PasQuantite ?? PasQuantite;
            ValidateurStructure.ValiderQuantite(p.Quantite, pas);
            if (!double.IsFinite(p.PrixClientBtc) || p.PrixClientBtc <= 0) throw new PricerException("Prix client positif et fini requis.");
            var jambes = new List<JambeExecutee>
            {
                new() { Instrument = p.Option.Libelle, Nature = NatureJambe.OptionSurMesure, Type = p.Option.Type,
                    Strike = p.Option.Strike, Echeance = p.Option.Echeance, QuantiteSignee = p.QuantiteDesk,
                    PrixExecution = p.PrixClientBtc, PasQuantite = pas, Future = p.Interpolation.Future, Vol = p.Interpolation.Sigma }
            };
            jambes.AddRange(p.Lignes.Where(l => Math.Abs(l.Quantite) > 1e-12).Select(l => new JambeExecutee
            {
                Instrument = l.Nom,
                Nature = l.Nature,
                Type = l.Type,
                Strike = l.Strike ?? 0,
                Echeance = l.Echeance,
                QuantiteSignee = l.Quantite,
                PrixExecution = l.PrixExecutable,
                Future = l.Future,
                Vol = l.Nature == NatureJambe.Future ? null : l.Sigma,
                Couverture = true,
                PasQuantite = l.PasQuantite,
                PasNotionnelUsd = l.PasNotionnelUsd,
                NotionnelUsd = l.Nature == NatureJambe.Future ? l.Quantite * l.PrixExecutable : 0
            }));
            return Ajouter(new Trade
            {
                Id = Guid.NewGuid(),
                Horodatage = horodatage,
                Nature = NatureTrade.Ouverture,
                TypeStructure = TypeStructure.OptionSurMesure,
                Description = $"{p.Option.Libelle} client {p.SensClient} + couverture",
                Sens = (Sens)(-(int)p.SensClient),
                Quantite = p.Quantite,
                Echeance = p.Option.Echeance,
                Jambes = jambes
            });
        }
    }
    /// <summary>
    /// We close a quantity at current prices and round each cover to its own pace, without
    /// exceeding its open balance.
    /// </summary>
    public Trade Deboucler(Guid idOrigine, double quantite, IContexteMarche contexte)
    {
        lock (_verrou)
        {
            VerifierChronologie(contexte.Maintenant);
            var o = Obtenir(idOrigine);
            if (o.Nature != NatureTrade.Ouverture) throw new PricerException("Seule une ouverture peut être débouclée.");
            double ouverte = QuantiteOuverte(idOrigine);
            // Zero quantity is allowed only to close the blankets that remain after the termination of the main contract.
            bool residu = ouverte == 0 && quantite == 0 && ADesJambesOuvertes(idOrigine);
            if (!residu) ValidateurStructure.ValiderQuantite(quantite, o.Jambes.First(j => !j.Couverture).PasQuantite);
            if (quantite > ouverte + 1e-9) throw new SaisieInvalideException(CodeErreurSaisie.QuantiteDebouclageExcessive, $"Seulement {ouverte} restent ouverts.");
            bool total = residu || Math.Abs(quantite - ouverte) < 1e-9;
            var jambes = new List<JambeExecutee>();
            foreach (var j in o.Jambes)
            {
                double reste = QuantiteOuverteJambe(idOrigine, j.Id);
                if (reste == 0) continue;
                if (j.Echeance <= contexte.Maintenant) throw new PricerException($"{j.Instrument} est échu : prix de règlement requis.");
                double q = total ? -reste : -j.QuantiteSignee * quantite / o.Quantite;
                if (j.Nature == NatureJambe.Future)
                {
                    // The dollar amount of the future is rounded up before returning to its original units.
                    double montant = total ? -NotionnelInitial(j) * reste / j.QuantiteSignee
                        : (-NotionnelInitial(j) * quantite / o.Quantite).ArrondirAuPas(j.PasNotionnelUsd);
                    q = montant / j.PrixExecution;
                }
                else if (j.Couverture && !total) q = q.ArrondirAuPas(j.PasQuantite);
                if (Math.Abs(q) > Math.Abs(reste)) q = -reste;
                if (Math.Abs(q) < 1e-10) continue;
                if (j.Nature != NatureJambe.Future) ValidateurStructure.ValiderQuantite(Math.Abs(q), j.PasQuantite);
                jambes.Add(FermerJambe(j, q, contexte));
            }
            if (jambes.Count == 0) throw new PricerException("Aucune jambe ouverte à déboucler.");
            return Ajouter(Fermeture(o, NatureTrade.Debouclage, contexte.Maintenant, quantite, jambes, $"Débouclage {quantite}/{o.Quantite} de {o.Description}"));
        }
    }
    /// <summary>
    /// Only the legs of the date concerned are adjusted to the official prize received; the
    /// other expiries remain open.
    /// </summary>
    public Trade Regler(Guid idOrigine, double prixReglement, DateTime horodatage)
    {
        lock (_verrou)
        {
            if (!double.IsFinite(prixReglement) || prixReglement <= 0) throw new PricerException("Prix de règlement positif et fini requis.");
            var o = Obtenir(idOrigine);
            var jambes = new List<JambeExecutee>();
            if (o.Nature != NatureTrade.Ouverture) throw new PricerException("Seule une ouverture peut être réglée.");
            double avant = QuantiteOuverte(idOrigine);
            foreach (var j in o.Jambes.Where(j => j.Echeance <= horodatage && j.Echeance.Date == horodatage.Date))
            {
                double reste = QuantiteOuverteJambe(idOrigine, j.Id);
                if (reste == 0) continue;
                double prix = j.Nature == NatureJambe.Future ? prixReglement : Black76.Calculer(prixReglement, j.Strike, 0, 0, j.Type!.Value).PrixBtc;
                jambes.Add(j with
                {
                    Id = Guid.NewGuid(),
                    IdJambeOrigine = j.Id,
                    QuantiteSignee = -reste,
                    PrixExecution = prix,
                    Future = prixReglement,
                    Vol = null,
                    NotionnelUsd = j.Nature == NatureJambe.Future ? -NotionnelInitial(j) * reste / j.QuantiteSignee : 0
                });
            }
            if (jambes.Count == 0) throw new PricerException("Aucune jambe à régler pour cette date.");
            return Ajouter(Fermeture(o, NatureTrade.Reglement, horodatage, jambes.Any(j => !j.Couverture) ? avant : 0,
                jambes, $"Règlement des jambes du {horodatage:dd/MM/yyyy HH:mm} à {prixReglement:N2}"));
        }
    }
    /// <summary>
    /// The closing event is created with the link to the opening, without changing the initial
    /// trade.
    /// </summary>
    private static Trade Fermeture(Trade o, NatureTrade nature, DateTime date, double q, IReadOnlyList<JambeExecutee> jambes, string description) => new()
    {
        Id = Guid.NewGuid(),
        Horodatage = date,
        Nature = nature,
        TypeStructure = o.TypeStructure,
        Description = description,
        Sens = (Sens)(-(int)o.Sens),
        Quantite = q,
        Echeance = o.Echeance,
        Jambes = jambes,
        IdTradeOrigine = o.Id
    };
    /// <summary>
    /// The entire backup is checked before replacing the float, in order to keep the book in
    /// memory if the file is inconsistent.
    /// </summary>
    public void Charger(IEnumerable<Trade> trades)
    {
        lock (_verrou)
        {
            var liste = trades.OrderBy(t => t.Horodatage).ToList();
            if (liste.Select(t => t.Id).Distinct().Count() != liste.Count) throw new PricerException("Identifiants de trades en double.");
            for (int i = 0; i < liste.Count; i++)
            {
                var t = liste[i];
                if (t.Nature == NatureTrade.Ouverture && t.TypeStructure == TypeStructure.OptionSurMesure)
                    liste[i] = t with { Jambes = t.Jambes.Select(j => j with { Couverture = j.Nature != NatureJambe.OptionSurMesure }).ToList() };
            }
            if (liste.SelectMany(t => t.Jambes).Select(j => j.Id).Distinct().Count() != liste.Sum(t => t.Jambes.Count))
                throw new PricerException("Identifiants de jambes en double.");
            if (liste.Any(t => t.Jambes.Count == 0 || t.Jambes.Any(j => !double.IsFinite(j.QuantiteSignee) || j.QuantiteSignee == 0 ||
                !double.IsFinite(j.PrixExecution) || j.PrixExecution < 0 || !double.IsFinite(j.NotionnelUsd) ||
                !double.IsFinite(j.PasQuantite) || j.PasQuantite <= 0 || !double.IsFinite(j.PasNotionnelUsd) || j.PasNotionnelUsd <= 0 ||
                (j.Nature == NatureJambe.Future && j.PrixExecution == 0))))
                throw new PricerException("La sauvegarde contient une jambe ou un prix invalide.");
            // Old backups that did not yet have a link per leg are attached.
            for (int i = 0; i < liste.Count; i++)
            {
                var t = liste[i];
                if (t.Nature == NatureTrade.Ouverture) continue;
                var o = liste.FirstOrDefault(o => o.Id == t.IdTradeOrigine && o.Nature == NatureTrade.Ouverture) ?? throw new PricerException("Fermeture sans ouverture.");
                liste[i] = t with
                {
                    Jambes = t.Jambes.Select(j => j.IdJambeOrigine is not null ? j : j with
                    { IdJambeOrigine = o.Jambes.Single(x => x.Instrument == j.Instrument && x.Nature == j.Nature).Id }).ToList()
                };
                if (liste[i].Jambes.Any(j => !o.Jambes.Any(x => x.Id == j.IdJambeOrigine)))
                    throw new PricerException("Une fermeture référence une jambe d'origine inconnue.");
                if (liste[i].Jambes.Any(j => j.QuantiteSignee * o.Jambes.Single(x => x.Id == j.IdJambeOrigine).QuantiteSignee >= 0))
                    throw new PricerException("Une fermeture doit porter le signe opposé à l'ouverture.");
            }
            foreach (var o in liste.Where(t => t.Nature == NatureTrade.Ouverture))
                foreach (var j in o.Jambes)
                {
                    double restant = j.QuantiteSignee + liste.Where(t => t.IdTradeOrigine == o.Id).SelectMany(t => t.Jambes)
                        .Where(x => x.IdJambeOrigine == j.Id).Sum(x => x.QuantiteSignee);
                    if (restant * j.QuantiteSignee < -1e-8) throw new PricerException("La sauvegarde ferme une quantité supérieure à l'ouverture.");
                }
            _trades.Clear(); _parId.Clear();
            foreach (var t in liste) { _trades.Add(t); _parId.Add(t.Id, t); }
        }
    }
    /// <summary>
    /// We find the signed USD amount of the future; the old backups reconstitute it with
    /// quantity and entry price.
    /// </summary>
    /// <returns>Le notionnel USD fixé à l'ouverture du future.</returns>
    internal static double NotionnelInitial(JambeExecutee j) => j.NotionnelUsd != 0 ? j.NotionnelUsd : j.QuantiteSignee * j.PrixExecution;
    /// <summary>
    /// We refuse a trade prior to the last recorded event to not use a book located in the
    /// future of the game.
    /// </summary>
    private void VerifierChronologie(DateTime date)
    {
        if (_trades.Count > 0 && date < _trades.Max(t => t.Horodatage)) throw new PricerException("Le marché précède le dernier trade : nouveau book ou reprise à sa dernière date requis.");
    }
    /// <summary>
    /// Trade is added to history and index, and the backup services are notified.
    /// </summary>
    private Trade Ajouter(Trade t) { _trades.Add(t); _parId.Add(t.Id, t); TradeAjoute?.Invoke(this, t); return t; }
    /// <summary>
    /// The counteroperation of the leg is created at the current price: bid/ask for the side,
    /// interpolated value for the CTA.
    /// </summary>
    /// <returns>Une nouvelle jambe liée à celle d'origine, avec les paramètres utilisés pour la fermeture.</returns>
    private static JambeExecutee FermerJambe(JambeExecutee j, double q, IContexteMarche contexte)
    {
        var fermeture = j with { Id = Guid.NewGuid(), IdJambeOrigine = j.Id, QuantiteSignee = q };
        if (j.Nature == NatureJambe.OptionSurMesure)
        {
            var p = new JambeSurMesure(j.Type!.Value, j.Strike, j.Echeance, 1).ParametresMarche(contexte);
            return fermeture with { PrixExecution = Black76.Calculer(p.Future, j.Strike, p.Maturite, p.Sigma, j.Type.Value).PrixBtc, Future = p.Future, Vol = p.Sigma };
        }
        var cot = contexte.Cotation(j.Instrument) ?? throw new PricerException($"Cotation absente : {j.Instrument}.");
        cot.VerifierFraicheur(contexte.Maintenant);
        if (!cot.FourchetteValide) throw new PricerException($"Bid/ask indisponible ou invalide : {j.Instrument}.");
        double prix = q > 0 ? cot.Ask!.Value : cot.Bid!.Value;
        if (j.Nature == NatureJambe.Future) return fermeture with { PrixExecution = prix, Future = cot.Mark ?? cot.Mid!.Value, Vol = null, NotionnelUsd = q * j.PrixExecution };
        var param = new JambeCotee(contexte.Referentiel.Obtenir(j.Instrument), 1).ParametresMarche(contexte);
        return fermeture with { PrixExecution = prix, Future = param.Future, Vol = param.Sigma };
    }
}
