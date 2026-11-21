using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Modeles;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Exports;
using Pricer.Infrastructure.Services;

namespace Pricer.Web.Services;

/// <summary>On sépare les books par source, devise et session de lecture.</summary>
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
        ModeDonnees.Demonstration => "Démonstration fictive",
        ModeDonnees.Rejeu => "Marché enregistré",
        ModeDonnees.Enregistrement => "Direct Deribit · enregistrement",
        _ => "Direct Deribit"
    };

    /// <summary>
    /// On relie le book au service de marché et on s'abonne aux nouveaux trades pour déclencher leur
    /// sauvegarde.
    /// </summary>
    public ServiceSession(OptionsPricer options, ServiceMarche marche, Blotter blotter, Book book, EtatApplication etat, ILoggerFactory journaux)
    {
        _options = options; _marche = marche; _blotter = blotter; _etat = etat; _journaux = journaux;
        _marche.BookSuivi = book;
        _blotter.TradeAjoute += TradeAjoute;
    }

    /// <summary>
    /// On identifie la source et la devise pour retrouver leur book ; un rejeu est reconnu par le
    /// contenu du fichier.
    /// </summary>
    public static string CleSource(OptionsPricer options)
    {
        // On utilise le contenu du fichier pour identifier un rejeu, afin de retrouver son book même si le fichier est renommé.
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
    /// On restaure la configuration valide et l'heure du dernier trade, en conservant un avertissement
    /// si les fichiers sont illisibles.
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
    /// On recharge les événements enregistrés pour la session active dans le blotter partagé.
    /// </summary>
    public void ChargerBook() => _blotter.Charger(ExportJson.Charger(CheminBook));
    /// <summary>
    /// On restaure le blotter puis on abonne les instruments encore nécessaires aux positions ouvertes.
    /// </summary>
    public async Task ChargerBookAsync()
    {
        ChargerBook();
        if (_marche.BookSuivi is not null) await _marche.SuivreAsync(_marche.BookSuivi.InstrumentsSuivis());
    }

    /// <summary>
    /// On retrouve la dernière session de chaque source ; un index absent ou illisible est traité comme
    /// vide.
    /// </summary>
    private static Dictionary<string, string> LireIndex(OptionsPricer options)
    {
        string fichier = Path.Combine(Path.GetDirectoryName(options.FichierSession)!, "sources-sessions.json");
        try { return File.Exists(fichier) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fichier)) ?? [] : []; }
        catch (Exception ex) when (ex is JsonException or IOException) { return []; }
    }

    /// <summary>
    /// On écrit le contenu complet dans un temporaire avant de remplacer le fichier pour éviter une
    /// sauvegarde tronquée.
    /// </summary>
    private static void EcrireAtomique(string fichier, string texte)
    {
        // On écrit la sauvegarde complète dans un fichier temporaire avant de remplacer l'ancienne, pour éviter un JSON tronqué.
        Directory.CreateDirectory(Path.GetDirectoryName(fichier)!);
        string temporaire = fichier + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaire, texte);
        File.Move(temporaire, fichier, true);
    }

    /// <summary>
    /// On conserve un instantané cohérent du book, de la configuration et de l'index des sessions sous
    /// le même verrou.
    /// </summary>
    public void Sauvegarder()
    {
        string cle = _options.CleBookSession;
        // On prend la copie du blotter avant le verrou de session pour éviter deux verrous pris dans un ordre opposé.
        var trades = _blotter.Trades;
        lock (_verrou)
        {
            // On ne remplace pas un book plus récent par un instantané pris avant un booking concurrent.
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
    /// On sauvegarde dès l'ajout d'une opération et on rend une erreur d'écriture visible sans annuler
    /// le trade déjà enregistré.
    /// </summary>
    private void TradeAjoute(object? sender, Trade trade)
    {
        try { Sauvegarder(); }
        catch (IOException ex) { ErreurSauvegarde = "Sauvegarde impossible : " + ex.Message; }
        catch (UnauthorizedAccessException ex) { ErreurSauvegarde = "Sauvegarde impossible : " + ex.Message; }
    }

    /// <summary>
    /// On sauvegarde l'ancienne session, on remplace sa source et son book, puis on restaure l'état
    /// précédent si le changement échoue.
    /// </summary>
    public async Task ChangerAsync(ModeDonnees mode, string devise, string fichier, bool nouvelleSession = false)
    {
        if (devise is not "BTC" and not "ETH") throw new ArgumentException("Choisissez BTC ou ETH.");
        // On n'autorise qu'un changement de source à la fois pour ne pas mélanger l'état du marché et le book associé.
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
            // On remet la source précédente si la nouvelle connexion échoue.
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
    /// On reporte les réglages de source sur l'objet partagé afin que tous les services lisent la même
    /// configuration.
    /// </summary>
    private static void Copier(OptionsPricer a, OptionsPricer b)
    {
        b.Mode = a.Mode; b.Devise = a.Devise; b.FichierRejeu = a.FichierRejeu; b.FichierEnregistrement = a.FichierEnregistrement;
        b.VitesseRejeu = a.VitesseRejeu; b.CleBookSession = a.CleBookSession; b.ReprendreAu = a.ReprendreAu;
    }

    /// <summary>
    /// On relance la source depuis le début avec un nouvel identifiant de session pour garder l'ancien
    /// book séparé.
    /// </summary>
    public Task RecommencerAsync() => ChangerAsync(_options.Mode, _options.Devise, _options.FichierRejeu, true);
    /// <summary>
    /// On règle le rythme du rejeu actif et on sauvegarde ce choix pour le prochain lancement.
    /// </summary>
    public void Vitesse(double vitesse)
    {
        if (_marche.Rejeu is not { } r) return;
        r.Lecture.Vitesse = vitesse; _options.VitesseRejeu = vitesse; Sauvegarder();
    }
    /// <summary>
    /// On retire l'abonnement au blotter et on libère le verrou de changement lorsque le service est
    /// arrêté.
    /// </summary>
    public void Dispose() { _blotter.TradeAjoute -= TradeAjoute; _changement.Dispose(); }
}
