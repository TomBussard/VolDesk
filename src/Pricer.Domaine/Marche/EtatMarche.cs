using System.Collections.Concurrent;
using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Marche;

/// <summary>
/// On garde la dernière cotation de chaque instrument et on prévient les écrans lorsqu'elle change. On
/// utilise des collections adaptées aux accès concurrents, car le flux et l'interface travaillent sur des
/// tâches différentes.
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
    /// On associe le référentiel à une horloge injectée pour utiliser les mêmes calculs en direct et en
    /// rejeu.
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

    /// <summary>On expose aussi un événement C# pour que l'interface puisse suivre les cotations simplement.</summary>
    public event EventHandler<Cotation>? CotationRecue;

    /// <summary>
    /// On lit la dernière cotation enregistrée sans fabriquer de prix si l'instrument n'a pas encore été
    /// reçu.
    /// </summary>
    public Cotation? Cotation(string nomInstrument) => _cotations.TryGetValue(nomInstrument, out var c) ? c : null;

    /// <summary>
    /// On retient la médiane des futures fournis par les options fraîches de l'échéance, puis le mark du
    /// future coté de même date.
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
    /// On retient la cotation la plus récente, on avance l'horloge et on invalide les smiles avant de
    /// prévenir les observateurs.
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
    /// On fait passer chaque cotation du lot par le même contrôle de date que les messages reçus
    /// individuellement.
    /// </summary>
    public void MettreAJour(IEnumerable<Cotation> cotations)
    {
        foreach (var c in cotations) MettreAJour(c);
    }

    /// <summary>
    /// On met à jour l'indice seulement si sa valeur est valide et sa date au moins aussi récente que la
    /// précédente.
    /// </summary>
    public void DefinirIndice(double indice, DateTime horodatage)
    {
        if (!double.IsFinite(indice) || indice <= 0) return;
        // On ne laisse pas une ancienne réponse REST remplacer le dernier indice reçu en flux.
        lock (_verrouIndice)
        {
            if (horodatage < _horodatageIndice) return;
            Indice = indice;
            _horodatageIndice = horodatage;
        }
        _horloge.Synchroniser(horodatage);
    }

    /// <summary>
    /// On ajoute un observateur sous verrou pour qu'il reçoive les prochaines cotations.
    /// </summary>
    public void AjouterObservateur(IObservateur<Cotation> observateur)
    {
        lock (_verrouObservateurs) _observateurs.Add(observateur);
    }

    /// <summary>
    /// On retire l'observateur sous verrou lorsqu'il n'a plus besoin des mises à jour.
    /// </summary>
    public void SupprimerObservateur(IObservateur<Cotation> observateur)
    {
        lock (_verrouObservateurs) _observateurs.Remove(observateur);
    }

    /// <summary>
    /// On copie les abonnements avant de les appeler pour ne pas garder leur verrou pendant le traitement
    /// d'une cotation.
    /// </summary>
    private void Notifier(Cotation cotation)
    {
        IObservateur<Cotation>[] copie;
        lock (_verrouObservateurs) copie = _observateurs.ToArray();
        foreach (var o in copie) o.Actualiser(cotation);
        CotationRecue?.Invoke(this, cotation);
    }

    /// <summary>
    /// On construit les smiles avec notre propre inversion du mark. On essaie d'abord
    /// l'option hors de la monnaie, puis l'autre type au même strike si sa cotation est inutilisable.
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
                // On préfère l'option hors de la monnaie, puis on essaie l'autre si son mark est inutilisable.
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
    /// On conserve le message le plus récent ; les champs de prix absents de la nouvelle cotation ne sont
    /// pas repris d'un ancien carnet.
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
