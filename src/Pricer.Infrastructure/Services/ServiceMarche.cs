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
/// Instrument loading, ratings and regulations are coordinated in this service. These tasks are
/// separated from the interface to keep the same operation live and in play.
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
    /// The source, settings and clock are linked to be shared by the market updates.
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
    /// We ask for the loops to stop and wait for their termination before deleting their list.
    /// </summary>
    public async Task ArreterSessionAsync()
    {
        _arret.Cancel();
        try { await Task.WhenAll(_taches).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _taches.Clear();
    }

    /// <summary>
    /// We stop the previous session, replace its source and reload the market with a new stop
    /// signal.
    /// </summary>
    /// <returns>On renvoie l’état initialisé avec la nouvelle source.</returns>
    public async Task<EtatMarche> RedemarrerAsync(ISourceMarche source, CancellationToken ct = default)
    {
        // We wait until the old loops stop before replacing the source, so that they do not write in the new market.
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
    /// First load the repository and a first state of the market to avoid a partially
    /// initialized screen, then launch the updates in the background.
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

        // Futures useful for delta cover are monitored and, during capture, options for timing chosen to enrich the game.
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
    /// We select several maturities spread over time, avoiding too close a maturities. The file
    /// remains useful for checking the structure by term, interpolation and hedge.
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
    /// The instruments used in the listing or book are monitored and their full ticker is
    /// requested to provide the missing details of the contract summary.
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
    /// Summaries of options and futures are reloaded, followed by the USD index, to refresh the
    /// entire market.
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
    /// Official prices for the last 30 days are charged and indexed by settlement date.
    /// </summary>
    public async Task ChargerReglementsAsync(CancellationToken ct = default)
    {
        var prix = await _source.ObtenirPrixReglementAsync(_options.Devise, 30, ct).ConfigureAwait(false);
        _prixReglement = prix.GroupBy(p => p.Date).ToDictionary(g => g.Key, g => g.First().Prix);
    }

    /// <summary>
    /// We look for the fallen legs still open and charge the necessary official prices before
    /// asking for their settlement in the book.
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
    /// Each quote received is applied to the state of the market and messages are counted.
    /// Error is retained if the flow is interrupted.
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
    /// The expiries for the follow-up book are periodically checked and the official price is
    /// expected if the settlement cannot be completed.
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
    /// We move the real hour live so that the ratings get older even during a break. We let the
    /// replay pilot its own time.
    /// </summary>
    private async Task BoucleHorlogeAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                // We let the ratings get older if the live connection is lost.
                if (_options.Mode is ModeDonnees.Live or ModeDonnees.Enregistrement) _horloge.Synchroniser(DateTime.UtcNow);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    /// <summary>
    /// The summaries are updated at regular intervals on-line and the next cycle is re-tested
    /// after a loading error.
    /// </summary>
    private async Task BoucleResumeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalleResumeSecondes), ct).ConfigureAwait(false);
                // We let the file control its time in replay: an old REST summary must not move a market on pause.
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
    /// The loops shall be stopped and the signal shall be released and, if necessary, the
    /// source held by the service.
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
