using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pricer.Domaine.Exceptions;

namespace Pricer.Infrastructure.Transport;

/// <summary>
/// On relit les messages enregistrés selon leurs intervalles d'origine, divisés par la vitesse choisie. On
/// répond aux requêtes avec les dernières données disponibles à l'instant virtuel courant, sans appeler le
/// réseau.
/// </summary>
public sealed class TransportRejeu : ITransportDeribit
{
    private readonly List<LigneEnregistree> _flux;
    private readonly Dictionary<string, List<LigneEnregistree>> _rest;
    private DateTime _dernierEmis;
    private readonly object _verrou = new();
    public LectureControlee Lecture { get; }

    /// <summary>
    /// On lit puis on trie la capture par heure et on sépare les réponses REST des notifications
    /// WebSocket.
    /// </summary>
    /// <param name="fichier">On accepte un JSONL brut ou compressé en gzip, présent et non vide.</param>
    /// <param name="vitesse">On règle le facteur de lecture ; zéro signifie une lecture sans attente.</param>
    public TransportRejeu(string fichier, double vitesse)
    {
        if (!File.Exists(fichier)) throw new DonneesIndisponiblesException($"Fichier de rejeu introuvable : {Path.GetFullPath(fichier)}.");
        if (vitesse < 0) throw new ArgumentOutOfRangeException(nameof(vitesse), "La vitesse de rejeu doit être positive (0 = maximale).");
        Lecture = new LectureControlee(vitesse);

        var lignes = LireLignes(fichier)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<LigneEnregistree>(l, TransportEnregistreur.OptionsJson)!)
            .OrderBy(l => l.T)
            .ToList();
        if (lignes.Count == 0) throw new DonneesIndisponiblesException("Le fichier de rejeu est vide.");

        _flux = lignes.Where(l => l.Type == "ws").ToList();
        _rest = lignes.Where(l => l.Type == "rest").GroupBy(l => l.Cle).ToDictionary(g => g.Key, g => g.ToList());
        Debut = lignes[0].T;
        Fin = lignes[^1].T;
        _dernierEmis = Debut;
    }

    /// <summary>
    /// On décompresse le fichier si nécessaire et on parcourt ses lignes sans laisser le flux ouvert
    /// après la lecture.
    /// </summary>
    private static IEnumerable<string> LireLignes(string fichier)
    {
        using Stream brut = File.OpenRead(fichier);
        using Stream flux = fichier.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(brut, CompressionMode.Decompress) : brut;
        using var lecteur = new StreamReader(flux);
        while (lecteur.ReadLine() is { } ligne) yield return ligne;
    }

    public DateTime Debut { get; }
    public DateTime Fin { get; }
    public int NombreMessages => _flux.Count;
    public int MessagesEmis { get; private set; }
    /// <summary>
    /// On place l’heure virtuelle avant le premier message émis et on borne la reprise à la période
    /// couverte par le fichier.
    /// </summary>
    /// <param name="date">On garde le début de session si aucune date de reprise n’est fournie.</param>
    public void ReprendreA(DateTime? date)
    {
        if (MessagesEmis > 0) throw new InvalidOperationException("On positionne le fichier avant de lancer sa lecture.");
        if (date is DateTime d) lock (_verrou) _dernierEmis = d < Debut ? Debut : d > Fin ? Fin : d;
    }
    public bool FluxTermine { get; private set; }

    /// <summary>
    /// On utilise l'instant de la dernière donnée émise pour que le temps du marché reste figé pendant une
    /// pause.
    /// </summary>
    public DateTime MaintenantVirtuel { get { lock (_verrou) return _dernierEmis; } }

    /// <summary>
    /// On recherche la réponse enregistrée disponible à l’heure virtuelle. Pour un ticker, on peut
    /// utiliser une notification plus récente du même instrument.
    /// </summary>
    /// <remarks>On prend la première réponse de la clé si aucune réponse n’est antérieure à l’heure virtuelle.</remarks>
    /// <returns>On renvoie la donnée réellement enregistrée ou on signale qu’aucune requête correspondante n’existe.</returns>
    public Task<ReponseRest> AppelerAsync(string methode, IReadOnlyDictionary<string, string> parametres, CancellationToken ct = default)
    {
        string cle = CleRequete.Construire(methode, parametres);
        var maintenant = MaintenantVirtuel;
        if (!_rest.TryGetValue(cle, out var reponses))
        {
            // On peut relire un ticker réellement enregistré en WebSocket, jamais inventer ses champs.
            if (methode == "public/ticker" && parametres.TryGetValue("instrument_name", out string? nom))
            {
                var tick = _flux.LastOrDefault(l => l.T <= maintenant && l.Cle.StartsWith($"ticker.{nom}.", StringComparison.Ordinal));
                if (tick is not null) return Task.FromResult(new ReponseRest(tick.Donnees, tick.T));
            }
            throw new DonneesIndisponiblesException($"Requête absente de l'enregistrement : {cle}.");
        }
        var ligne = reponses.LastOrDefault(r => r.T <= maintenant) ?? reponses[0];
        if (methode == "public/ticker" && parametres.TryGetValue("instrument_name", out string? instrument))
        {
            var tick = _flux.LastOrDefault(l => l.T <= maintenant && l.T > ligne.T && l.Cle.StartsWith($"ticker.{instrument}.", StringComparison.Ordinal));
            if (tick is not null) return Task.FromResult(new ReponseRest(tick.Donnees, tick.T));
        }
        return Task.FromResult(new ReponseRest(ligne.Donnees, ligne.T));
    }

    /// <summary>
    /// On rejoue tous les messages du fichier, indépendamment des canaux demandés à l'écran, afin de
    /// conserver la session enregistrée.
    /// </summary>
    public Task SouscrireAsync(IEnumerable<string> canaux, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// On relit les notifications à partir de l’heure choisie et on fait avancer l’heure virtuelle
    /// seulement lorsqu’un message est émis.
    /// </summary>
    public async IAsyncEnumerable<MessageFlux> LireFluxAsync([EnumeratorCancellation] CancellationToken ct)
    {
        DateTime precedent = MaintenantVirtuel;
        foreach (var ligne in _flux.Where(l => l.T >= precedent))
        {
            await Lecture.AttendreAsync(ligne.T - precedent, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            lock (_verrou) _dernierEmis = ligne.T;
            MessagesEmis++;
            precedent = ligne.T;
            yield return new MessageFlux(ligne.Cle, ligne.Donnees, ligne.T);
        }
        FluxTermine = true;
    }

    /// <summary>
    /// On termine sans ressource à libérer : le fichier a déjà été entièrement lu et fermé par le
    /// constructeur.
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
