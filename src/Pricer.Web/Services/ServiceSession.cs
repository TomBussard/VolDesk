using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Modeles;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Exports;
using Pricer.Infrastructure.Services;

namespace Pricer.Web.Services;

/// <summary>
/// Books are separated by source, currency and reading session.
/// </summary>
public sealed class ServiceSession : IDisposable
{
    private readonly OptionsPricer _options;
    private readonly ServiceMarche _marche;
    private readonly Blotter _blotter;
    private readonly EtatApplication _etat;
    private readonly ILoggerFactory _journaux;
    private readonly object _verrou = new();
    private readonly Dictionary<string, int> _dernieresSauvegardes = new();
    private readonly SemaphoreSlim _changement = new(1, 1);
    public bool ChangementEnCours { get; private set; }
    public string? ErreurSauvegarde { get; private set; }
    public string CheminBook => Path.Combine(Path.GetDirectoryName(_options.FichierSession)!, "books", _options.CleBookSession + ".json");
    public string LibelleMode => _options.Mode switch
    {
        ModeDonnees.Demonstration => "Synthetic market",
        ModeDonnees.Rejeu => "Recorded market",
        ModeDonnees.Enregistrement => "Deribit live recording",
        _ => "Deribit live"
    };

    /// <summary>
    /// We connect the book to the market service and subscribe to the new trades to trigger
    /// their backup.
    /// </summary>
    public ServiceSession(OptionsPricer options, ServiceMarche marche, Blotter blotter, Book book, EtatApplication etat, ILoggerFactory journaux)
    {
        _options = options; _marche = marche; _blotter = blotter; _etat = etat; _journaux = journaux;
        _marche.BookSuivi = book;
        _blotter.TradeAjoute += TradeAjoute;
    }

    /// <summary>
    /// The source and currency are identified to find their book; a replay is recognized by the
    /// content of the file.
    /// </summary>
    public static string CleSource(OptionsPricer options)
    {
        // The content of the file is used to identify a replay, in order to find its book even if the file is renamed.
        string source = options.Mode is ModeDonnees.Live or ModeDonnees.Enregistrement ? "live" : options.Mode.ToString();
        string empreinte = "";
        if (options.Mode == ModeDonnees.Rejeu)
        {
            if (File.Exists(options.FichierRejeu))
            {
                using var flux = File.OpenRead(options.FichierRejeu);
                empreinte = Convert.ToHexString(SHA256.HashData(flux));
            }
            else empreinte = Path.GetFullPath(options.FichierRejeu);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + "|" + options.Devise + "|" + empreinte)))[..24].ToLowerInvariant();
    }

    /// <summary>
    /// We restore the valid configuration and the time of the last trade, keeping a warning if
    /// the files are unreadable.
    /// </summary>
    public static void ChargerAvantDemarrage(OptionsPricer options, bool chargerConfiguration)
    {
        if (chargerConfiguration && File.Exists(options.FichierSession))
        {
            try
            {
                var sauvee = JsonSerializer.Deserialize<OptionsPricer>(File.ReadAllText(options.FichierSession));
                if (sauvee is not null)
                {
                    if (!Enum.IsDefined(sauvee.Mode) || sauvee.Devise is not "BTC" and not "ETH" ||
                        !double.IsFinite(sauvee.VitesseRejeu) || sauvee.VitesseRejeu < 0 || sauvee.VitesseRejeu > 1000 ||
                        string.IsNullOrWhiteSpace(sauvee.FichierRejeu) ||
                        (!string.IsNullOrWhiteSpace(sauvee.CleBookSession) && sauvee.CleBookSession.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
                        throw new InvalidDataException("La configuration de session contient un mode, une devise ou un identifiant invalide.");
                    options.Mode = sauvee.Mode; options.Devise = sauvee.Devise; options.FichierRejeu = sauvee.FichierRejeu;
                    options.FichierEnregistrement = sauvee.FichierEnregistrement; options.VitesseRejeu = sauvee.VitesseRejeu;
                    options.CleBookSession = sauvee.CleBookSession;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { options.Mode = ModeDonnees.Demonstration; options.Devise = "BTC"; options.CleBookSession = ""; options.AvertissementSession = "Configuration enregistrée illisible : on démarre la démonstration. " + ex.Message; }
        }
        if (string.IsNullOrWhiteSpace(options.CleBookSession))
        {
            string cleSource = CleSource(options);
            var index = LireIndex(options);
            options.CleBookSession = index.GetValueOrDefault(cleSource, cleSource);
        }
        if (options.CleBookSession.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException("Identifiant de session invalide.");
        string book = Path.Combine(Path.GetDirectoryName(options.FichierSession)!, "books", options.CleBookSession + ".json");
        try
        {
            var trades = ExportJson.Charger(book);
            options.ReprendreAu = trades.Count == 0 ? null : trades.Max(t => t.Horodatage);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { options.ReprendreAu = null; options.AvertissementSession = "Book sauvegardé illisible : " + ex.Message; }
    }

    /// <summary>
    /// Reload the recorded events for the active session in the shared float.
    /// </summary>
    public void ChargerBook() => _blotter.Charger(ExportJson.Charger(CheminBook));
    /// <summary>
    /// We restore the float then we subscribe the instruments still necessary to the open
    /// positions.
    /// </summary>
    public async Task ChargerBookAsync()
    {
        ChargerBook();
        if (_marche.BookSuivi is not null) await _marche.SuivreAsync(_marche.BookSuivi.InstrumentsSuivis());
    }

    /// <summary>
    /// We find the last session of each source; an absent or unreadable index is treated as
    /// empty.
    /// </summary>
    private static Dictionary<string, string> LireIndex(OptionsPricer options)
    {
        string fichier = Path.Combine(Path.GetDirectoryName(options.FichierSession)!, "sources-sessions.json");
        try { return File.Exists(fichier) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fichier)) ?? [] : []; }
        catch (Exception ex) when (ex is JsonException or IOException) { return []; }
    }

    /// <summary>
    /// Complete content is written in a temporary before replacing the file to avoid a
    /// truncated backup.
    /// </summary>
    private static void EcrireAtomique(string fichier, string texte)
    {
        // The full backup is written in a temporary file before replacing the old one, to avoid a truncated JSON.
        Directory.CreateDirectory(Path.GetDirectoryName(fichier)!);
        string temporaire = fichier + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaire, texte);
        File.Move(temporaire, fichier, true);
    }

    /// <summary>
    /// A consistent snapshot of the session book, configuration and index is kept under the
    /// same lock.
    /// </summary>
    public void Sauvegarder()
    {
        string cle = _options.CleBookSession;
        // A copy of the float is taken before the session lock to avoid two locks in an opposite order.
        var trades = _blotter.Trades;
        lock (_verrou)
        {
            // We don't replace a newer book with a snapshot taken before a competing booking.
            if (cle != _options.CleBookSession || trades.Count < _dernieresSauvegardes.GetValueOrDefault(cle)) return;
            EcrireAtomique(CheminBook, JsonSerializer.Serialize(trades, ExportJson.Options));
            _options.ReprendreAu = trades.Count == 0 ? null : trades.Max(t => t.Horodatage);
            EcrireAtomique(_options.FichierSession, JsonSerializer.Serialize(_options, new JsonSerializerOptions { WriteIndented = true }));
            var index = LireIndex(_options);
            index[CleSource(_options)] = _options.CleBookSession;
            EcrireAtomique(Path.Combine(Path.GetDirectoryName(_options.FichierSession)!, "sources-sessions.json"), JsonSerializer.Serialize(index));
            _dernieresSauvegardes[cle] = trades.Count;
            ErreurSauvegarde = null;
        }
    }

    /// <summary>
    /// You save when you add an operation and make a write error visible without canceling the
    /// already registered trade.
    /// </summary>
    private void TradeAjoute(object? sender, Trade trade)
    {
        try { Sauvegarder(); }
        catch (IOException ex) { ErreurSauvegarde = "Sauvegarde impossible : " + ex.Message; }
        catch (UnauthorizedAccessException ex) { ErreurSauvegarde = "Sauvegarde impossible : " + ex.Message; }
    }

    /// <summary>
    /// You save the old session, replace its source and book, and then restore the previous
    /// state if the change fails.
    /// </summary>
    public async Task ChangerAsync(ModeDonnees mode, string devise, string fichier, bool nouvelleSession = false)
    {
        if (devise is not "BTC" and not "ETH") throw new ArgumentException("Choisissez BTC ou ETH.");
        // Only one source change is allowed to not mix market conditions with the associated book.
        await _changement.WaitAsync();
        var ancienne = JsonSerializer.Deserialize<OptionsPricer>(JsonSerializer.Serialize(_options))!;
        var ancienBook = _blotter.Trades;
        try
        {
            ChangementEnCours = true;
            if (_etat.Pret) Sauvegarder();
            var choix = JsonSerializer.Deserialize<OptionsPricer>(JsonSerializer.Serialize(_options))!;
            choix.Mode = mode; choix.Devise = devise; choix.FichierRejeu = Path.GetFullPath(fichier);
            if (mode == ModeDonnees.Enregistrement)
                choix.FichierEnregistrement = Path.Combine(Path.GetDirectoryName(choix.FichierSession)!, "enregistrements", $"deribit-{devise}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl.gz");
            choix.CleBookSession = nouvelleSession ? Guid.NewGuid().ToString("N") : "";
            choix.ReprendreAu = null;
            ChargerAvantDemarrage(choix, false);
            var source = FabriqueSourceDonnees.Creer(choix, _journaux);
            _etat.MarquerChargement();
            await _marche.ArreterSessionAsync();
            Copier(choix, _options);
            _blotter.Charger([]);
            await _marche.RedemarrerAsync(source);
            await ChargerBookAsync();
            _etat.MarquerPret();
            Sauvegarder();
        }
        catch
        {
            // The previous source is returned if the new connection fails.
            Copier(ancienne, _options);
            _blotter.Charger(ancienBook);
            try
            {
                var source = FabriqueSourceDonnees.Creer(_options, _journaux);
                await _marche.RedemarrerAsync(source);
                _etat.MarquerPret();
            }
            catch (Exception ex) { _etat.MarquerErreur(ex.Message); }
            throw;
        }
        finally { ChangementEnCours = false; _changement.Release(); }
    }

    /// <summary>
    /// The source settings are reported on the shared object so that all services read the same
    /// configuration.
    /// </summary>
    private static void Copier(OptionsPricer a, OptionsPricer b)
    {
        b.Mode = a.Mode; b.Devise = a.Devise; b.FichierRejeu = a.FichierRejeu; b.FichierEnregistrement = a.FichierEnregistrement;
        b.VitesseRejeu = a.VitesseRejeu; b.CleBookSession = a.CleBookSession; b.ReprendreAu = a.ReprendreAu;
    }

    /// <summary>
    /// Reboot the source from the beginning with a new session ID to keep the old book
    /// separate.
    /// </summary>
    public Task RecommencerAsync() => ChangerAsync(_options.Mode, _options.Devise, _options.FichierRejeu, true);
    /// <summary>
    /// We set the pace of active replay and save this choice for the next launch.
    /// </summary>
    public void Vitesse(double vitesse)
    {
        if (_marche.Rejeu is not { } r) return;
        r.Lecture.Vitesse = vitesse; _options.VitesseRejeu = vitesse; Sauvegarder();
    }
    /// <summary>
    /// The subscription to the float is removed and the change lock is released when the
    /// service is stopped.
    /// </summary>
    public void Dispose() { _blotter.TradeAjoute -= TradeAjoute; _changement.Dispose(); }
}
