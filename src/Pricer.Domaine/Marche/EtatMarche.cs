using System.Collections.Concurrent;
using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Marche;

/// <summary>
/// The last rating of each instrument is kept and screens are prevented when it changes.
/// Collections are used adapted to competing accesses, as the flow and interface work on
/// different tasks.
/// </summary>
public sealed class EtatMarche : IContexteMarche, ISujet<Cotation>
{
    private readonly ConcurrentDictionary<string, Cotation> _cotations = new();
    private readonly object _verrouIndice = new();
    private DateTime _horodatageIndice = DateTime.MinValue;
    private readonly List<IObservateur<Cotation>> _observateurs = new();
    private readonly object _verrouObservateurs = new();
    private readonly IHorloge _horloge;
    private IReadOnlyList<SmileEcheance>? _cacheSmiles;
    private DateTime _cacheSmilesDate;

    /// <summary>
    /// The reference frame is combined with an injected clock to use the same calculations in
    /// live and replay.
    /// </summary>
    public EtatMarche(ReferentielOptions referentiel, IHorloge horloge)
    {
        Referentiel = referentiel;
        _horloge = horloge;
    }

    public ReferentielOptions Referentiel { get; }
    public DateTime Maintenant => _horloge.Maintenant;
    public double? Indice { get; private set; }
    public int NombreCotations => _cotations.Count;

    /// <summary>
    /// A C# event is also displayed so that the interface can follow the ratings simply.
    /// </summary>
    public event EventHandler<Cotation>? CotationRecue;

    /// <summary>
    /// The last recorded quote is read without making prices if the instrument has not yet
    /// been received.
    /// </summary>
    public Cotation? Cotation(string nomInstrument) => _cotations.TryGetValue(nomInstrument, out var c) ? c : null;

    /// <summary>
    /// The median of futures provided by the fresh maturity options is retained, followed by
    /// the mark on the future side of the same date.
    /// </summary>
    /// <returns>Un prix en USD, ou null si aucune donnée de l'échéance n'est disponible.</returns>
    public double? Future(DateTime echeance)
    {
        var frais = Referentiel.Options.Where(o => o.Echeance == echeance)
            .Select(o => Cotation(o.Nom)).Where(c => c is not null && c.EstFraiche(Maintenant) && c.Future is > 0)
            .Select(c => c!.Future!.Value).Where(double.IsFinite).OrderBy(f => f).ToList();
        if (frais.Count > 0) return frais[frais.Count / 2];
        var future = Referentiel.Futures.FirstOrDefault(x => x.Echeance == echeance);
        return future is not null && Cotation(future.Nom) is { } cot && cot.EstFraiche(Maintenant) ? cot.Mark : null;
    }

    /// <summary>
    /// The most recent rating is retained, the clock is moved forward and the smiles are
    /// invalidated before warning observers.
    /// </summary>
    public void MettreAJour(Cotation cotation)
    {
        // On garde le message le plus récent ; un champ absent ne devient pas une valeur inventée.
        var retenue = _cotations.AddOrUpdate(cotation.NomInstrument, cotation, (_, ancienne) => Fusionner(ancienne, cotation));
        _horloge.Synchroniser(retenue.Horodatage);
        if (retenue.Indice is double indice) DefinirIndice(indice, retenue.Horodatage);
        _cacheSmiles = null;
        Notifier(retenue);
    }

    /// <summary>
    /// Each lot rating is passed through the same date check as the individual messages
    /// received.
    /// </summary>
    public void MettreAJour(IEnumerable<Cotation> cotations)
    {
        foreach (var c in cotations) MettreAJour(c);
    }

    /// <summary>
    /// The index is updated only if its value is valid and its date at least as recent as the
    /// previous one.
    /// </summary>
    public void DefinirIndice(double indice, DateTime horodatage)
    {
        if (!double.IsFinite(indice) || indice <= 0) return;
        // An old REST response is not allowed to replace the last index received in flow.
        lock (_verrouIndice)
        {
            if (horodatage < _horodatageIndice) return;
            Indice = indice;
            _horodatageIndice = horodatage;
        }
        _horloge.Synchroniser(horodatage);
    }

    /// <summary>
    /// A locked observer is added to receive the next quotes.
    /// </summary>
    public void AjouterObservateur(IObservateur<Cotation> observateur)
    {
        lock (_verrouObservateurs) _observateurs.Add(observateur);
    }

    /// <summary>
    /// The observer is removed under lock when it no longer needs updates.
    /// </summary>
    public void SupprimerObservateur(IObservateur<Cotation> observateur)
    {
        lock (_verrouObservateurs) _observateurs.Remove(observateur);
    }

    /// <summary>
    /// Subscriptions are copied before they are called so that they do not lock during the
    /// processing of a listing.
    /// </summary>
    private void Notifier(Cotation cotation)
    {
        IObservateur<Cotation>[] copie;
        lock (_verrouObservateurs) copie = _observateurs.ToArray();
        foreach (var o in copie) o.Actualiser(cotation);
        CotationRecue?.Invoke(this, cotation);
    }

    /// <summary>
    /// We build the smiles with our own inversion of the mark. First we try the option out of
    /// the currency, then the other type in the same strike if its rating is unusable.
    /// </summary>
    public IReadOnlyList<SmileEcheance> Smiles()
    {
        var maintenant = Maintenant;
        if (_cacheSmiles is not null && _cacheSmilesDate == maintenant) return _cacheSmiles;

        var smiles = new List<SmileEcheance>();
        foreach (var echeance in Referentiel.Echeances)
        {
            double t = Maturite.EnAnnees(echeance, maintenant);
            if (t <= 0 || Future(echeance) is not double f) continue;

            var points = new List<PointSmile>();
            foreach (var paire in Referentiel.Strikes(echeance))
            {
                // We prefer the option out of the money, then we try the other if his mark is unusable.
                var candidats = paire.Strike >= f ? new[] { paire.Call, paire.Put } : new[] { paire.Put, paire.Call };
                foreach (var instrument in candidats)
                {
                    if (instrument is null || !instrument.Actif || Cotation(instrument.Nom) is not { Mark: double mark, Future: double fCot } cot ||
                        !cot.EstFraiche(maintenant) || !double.IsFinite(fCot) || fCot <= 0) continue;
                    var vol = VolImplicite.CalculerDepuisBtc(mark, fCot, paire.Strike, Maturite.EnAnnees(echeance, cot.Horodatage), instrument.Type);
                    if (!vol.EstCalculee) continue;
                    points.Add(new PointSmile(paire.Strike, vol.Sigma!.Value));
                    break;
                }
            }
            if (points.Count >= 2) smiles.Add(new SmileEcheance(echeance, t, f, points));
        }
        _cacheSmiles = smiles;
        _cacheSmilesDate = maintenant;
        return smiles;
    }

    /// <summary>
    /// The most recent message is kept; the price fields missing from the new listing are not
    /// taken from an old notebook.
    /// </summary>
    private static Cotation Fusionner(Cotation ancienne, Cotation nouvelle) => nouvelle.Horodatage < ancienne.Horodatage
        ? ancienne // On conserve le ticker déjà reçu si le résumé est plus ancien.
        : nouvelle with
        {
            Bid = nouvelle.Bid,
            Ask = nouvelle.Ask,
            BidIv = nouvelle.BidIv,
            AskIv = nouvelle.AskIv,
            MarkIv = nouvelle.MarkIv,
            Future = nouvelle.Future,
            Indice = nouvelle.Indice ?? ancienne.Indice,
            GrecquesDeribit = nouvelle.GrecquesDeribit

        };
}
