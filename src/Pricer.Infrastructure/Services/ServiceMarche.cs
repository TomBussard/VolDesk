using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Deribit;
using Pricer.Infrastructure.Transport;

namespace Pricer.Infrastructure.Services;

/// <summary>
/// On coordonne le chargement des instruments, les cotations et les règlements dans ce service. On sépare
/// ces tâches de l'interface pour garder le même fonctionnement en direct et en rejeu.
/// </summary>
public sealed class ServiceMarche : IAsyncDisposable
{
    private ISourceMarche _source;
    private readonly OptionsPricer _options;
    private readonly IHorloge _horloge;
    private readonly ILogger _log;
    private CancellationTokenSource _arret = new();
    private bool _possedeSource;
    private readonly List<Task> _taches = new();
    private Dictionary<DateOnly, double> _prixReglement = new();

    /// <summary>
    /// On relie la source, les réglages et l’horloge qui seront partagés par les mises à jour du marché.
    /// </summary>
    /// <param name="possedeSource">On libère nous-mêmes la source uniquement lorsque ce service en est responsable.</param>
    public ServiceMarche(ISourceMarche source, OptionsPricer options, IHorloge horloge, ILogger<ServiceMarche>? log = null, bool possedeSource = false)
    {
        _source = source;
        _options = options;
        _horloge = horloge;
        _log = log ?? NullLogger<ServiceMarche>.Instance;
        _possedeSource = possedeSource;
    }

    public EtatMarche? Etat { get; private set; }
    public EtatMarche EtatCourant => Etat ?? throw new InvalidOperationException("Le marché n'est pas encore chargé : appeler DemarrerAsync.");
    public long MessagesRecus { get; private set; }
    public bool FluxTermine => _source.FluxTermine;
    public IReadOnlyDictionary<DateOnly, double> PrixReglement => _prixReglement;
    public TransportRejeu? Rejeu => ((_source is SourceMarcheDifferee differee ? differee.SourceActuelle : _source) as SourceMarcheDeribit)?.Transport as TransportRejeu;
    public Book? BookSuivi { get; set; }
    public string? ErreurFlux { get; private set; }

    /// <summary>
    /// On demande l’arrêt des boucles et on attend leur terminaison avant d’effacer leur liste.
    /// </summary>
    public async Task ArreterSessionAsync()
    {
        _arret.Cancel();
        try { await Task.WhenAll(_taches).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _taches.Clear();
    }

    /// <summary>
    /// On arrête la session précédente, on remplace sa source et on recharge le marché avec un nouveau
    /// signal d’arrêt.
    /// </summary>
    /// <returns>On renvoie l’état initialisé avec la nouvelle source.</returns>
    public async Task<EtatMarche> RedemarrerAsync(ISourceMarche source, CancellationToken ct = default)
    {
        // On attend l'arrêt des anciennes boucles avant de remplacer la source, pour qu'elles n'écrivent pas dans le nouveau marché.
        await ArreterSessionAsync().ConfigureAwait(false);
        if (_possedeSource) await _source.DisposeAsync().ConfigureAwait(false);
        _source = source;
        _possedeSource = true;
        _arret.Dispose();
        _arret = new CancellationTokenSource();
        if (_horloge is HorlogeMarche h) h.Reinitialiser();
        Etat = null;
        MessagesRecus = 0;
        ErreurFlux = null;
        return await DemarrerAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// On charge d'abord le référentiel et un premier état du marché pour éviter un écran partiellement
    /// initialisé, puis on lance les mises à jour en arrière-plan.
    /// </summary>
    public async Task<EtatMarche> DemarrerAsync(CancellationToken ct = default)
    {
        var options = await _source.ChargerOptionsAsync(_options.Devise, ct).ConfigureAwait(false);
        if (options.Count == 0 || options.Any(o => !string.Equals(o.Devise, _options.Devise, StringComparison.OrdinalIgnoreCase)))
            throw new DonneesIndisponiblesException("Le fichier ne contient pas la devise choisie. Choisissez le fichier correspondant à BTC ou ETH.");
        var futures = await _source.ChargerFuturesAsync(_options.Devise, ct).ConfigureAwait(false);
        var etat = new EtatMarche(new ReferentielOptions(_options.Devise, options, futures), _horloge);
        Etat = etat;

        await RafraichirResumeAsync(ct).ConfigureAwait(false);
        await ChargerReglementsAsync(ct).ConfigureAwait(false);

        // On suit les futures utiles à la couverture du delta et, pendant une capture, les options des échéances choisies pour enrichir le rejeu.
        var suivis = futures.Select(f => f.Nom).ToList();
        if (_options.Mode == ModeDonnees.Enregistrement)
            suivis.AddRange(EcheancesAEnregistrer(etat)
                .SelectMany(e => etat.Referentiel.Strikes(e).Where(p => etat.Future(e) is double f && Math.Abs(p.Strike / f - 1) <= _options.LargeurEnregistrement))
                .SelectMany(p => new[] { p.Call?.Nom, p.Put?.Nom }).OfType<string>());
        await _source.SuivreAsync(suivis, ct).ConfigureAwait(false);

        _taches.Add(Task.Run(() => BoucleFluxAsync(_arret.Token)));
        _taches.Add(Task.Run(() => BoucleResumeAsync(_arret.Token)));
        _taches.Add(Task.Run(() => BoucleReglementsAsync(_arret.Token)));
        _taches.Add(Task.Run(() => BoucleHorlogeAsync(_arret.Token)));
        return etat;
    }

    /// <summary>
    /// On choisit plusieurs maturités réparties dans le temps, en évitant une échéance trop proche. Le
    /// fichier reste ainsi utile pour vérifier la structure par terme, l'interpolation et la couverture.
    /// </summary>
    private IEnumerable<DateTime> EcheancesAEnregistrer(EtatMarche etat)
    {
        var futures = etat.Referentiel.Echeances.Where(e => e > etat.Maintenant.AddHours(12)).ToList();
        var choix = new SortedSet<DateTime>();
        foreach (double jours in new[] { 0.0, 7, 21, 56 }.Take(_options.EcheancesEnregistrees))
        {
            var e = futures.FirstOrDefault(x => (x - etat.Maintenant).TotalDays >= jours);
            if (e != default) choix.Add(e);
        }
        return choix;
    }

    /// <summary>
    /// On suit les instruments utiles à la cotation ou au book et on demande leur ticker complet, pour
    /// disposer des détails absents du résumé de marché.
    /// </summary>
    public async Task SuivreAsync(IEnumerable<string> instruments, CancellationToken ct = default)
    {
        var liste = instruments.Distinct().ToList();
        await _source.SuivreAsync(liste, ct).ConfigureAwait(false);
        foreach (var nom in liste)
        {
            try { EtatCourant.MettreAJour(await _source.ObtenirTickerAsync(nom, ct).ConfigureAwait(false)); }
            catch (DonneesIndisponiblesException ex) { _log.LogInformation("Ticker {Nom} indisponible : {Message}", nom, ex.Message); }
        }
    }

    /// <summary>
    /// On recharge les résumés des options et des futures, puis l’indice USD, pour rafraîchir l’ensemble
    /// du marché.
    /// </summary>
    public async Task RafraichirResumeAsync(CancellationToken ct = default)
    {
        var etat = EtatCourant;
        etat.MettreAJour(await _source.ChargerResumeAsync(_options.Devise, "option", ct).ConfigureAwait(false));
        etat.MettreAJour(await _source.ChargerResumeAsync(_options.Devise, "future", ct).ConfigureAwait(false));
        var (indice, horodatage) = await _source.ObtenirIndiceAsync(_options.Devise, ct).ConfigureAwait(false);
        etat.DefinirIndice(indice, horodatage);
    }

    /// <summary>
    /// On recharge les prix officiels des trente dernières journées et on les indexe par date de
    /// règlement.
    /// </summary>
    public async Task ChargerReglementsAsync(CancellationToken ct = default)
    {
        var prix = await _source.ObtenirPrixReglementAsync(_options.Devise, 30, ct).ConfigureAwait(false);
        _prixReglement = prix.GroupBy(p => p.Date).ToDictionary(g => g.Key, g => g.First().Prix);
    }

    /// <summary>
    /// On recherche les jambes échues encore ouvertes et on charge les prix officiels nécessaires avant de
    /// demander leur règlement au book.
    /// </summary>
    public async Task<IReadOnlyList<Trade>> ReglerEcheancesAsync(Book book, CancellationToken ct = default)
    {
        var maintenant = EtatCourant.Maintenant;
        bool echus = book.Blotter.Ouvertures.Any(o => o.Jambes.Any(j => j.Echeance <= maintenant && Math.Abs(book.Blotter.QuantiteOuverteJambe(o.Id, j.Id)) > 1e-9));
        if (!echus) return [];
        await ChargerReglementsAsync(ct).ConfigureAwait(false);
        return book.ReglerEcheances(maintenant, _prixReglement);
    }

    /// <summary>
    /// On applique chaque cotation reçue à l’état du marché et on compte les messages. On conserve
    /// l’erreur si le flux s’interrompt.
    /// </summary>
    private async Task BoucleFluxAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cotation in _source.FluxCotationsAsync(ct).ConfigureAwait(false))
            {
                EtatCourant.MettreAJour(cotation);
                MessagesRecus++;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* On termine la lecture après la demande d’arrêt. */ }
        catch (Exception ex)
        {
            ErreurFlux = ex.Message;
            _log.LogError(ex, "Flux de cotations interrompu.");
        }
    }

    /// <summary>
    /// On vérifie périodiquement les échéances du book suivi et on attend le prix officiel si le
    /// règlement ne peut pas encore être effectué.
    /// </summary>
    private async Task BoucleReglementsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                if (BookSuivi is not null) await ReglerEcheancesAsync(BookSuivi, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is PricerException or HttpRequestException or IOException)
            { _log.LogWarning(ex, "On attend le prix de règlement avant de régler les jambes échues."); }
        }
    }

    /// <summary>
    /// On fait avancer l’heure réelle en direct pour que les cotations vieillissent même pendant une
    /// coupure. On laisse le rejeu piloter son propre temps.
    /// </summary>
    private async Task BoucleHorlogeAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                // On laisse vieillir les cotations si la connexion live est perdue.
                if (_options.Mode is ModeDonnees.Live or ModeDonnees.Enregistrement) _horloge.Synchroniser(DateTime.UtcNow);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>
    /// On actualise les résumés à intervalle régulier en direct et on réessaie au cycle suivant après
    /// une erreur de chargement.
    /// </summary>
    private async Task BoucleResumeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalleResumeSecondes), ct).ConfigureAwait(false);
                // On laisse le fichier piloter son temps en rejeu : un ancien résumé REST ne doit pas faire bouger un marché en pause.
                if (_options.Mode is ModeDonnees.Rejeu or ModeDonnees.Demonstration) continue;
                await RafraichirResumeAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is DonneesIndisponiblesException or HttpRequestException or TaskCanceledException)
            {
                _log.LogWarning(ex, "Rafraîchissement du résumé impossible, nouvel essai au prochain cycle.");
            }
        }
    }

    private bool _libere;

    /// <summary>
    /// On arrête les boucles puis on libère le signal d’arrêt et, si nécessaire, la source détenue par
    /// le service.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_libere) return;
        _libere = true;
        await ArreterSessionAsync().ConfigureAwait(false);
        if (_possedeSource) await _source.DisposeAsync().ConfigureAwait(false);
        _arret.Dispose();
    }
}
