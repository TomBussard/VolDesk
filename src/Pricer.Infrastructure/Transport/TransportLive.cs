using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pricer.Domaine.Exceptions;

namespace Pricer.Infrastructure.Transport;

/// <summary>
/// On utilise REST pour les chargements ponctuels et WebSocket pour les cotations suivies. On limite les
/// appels REST et on prévoit le heartbeat, la reconnexion et la reprise des abonnements après une coupure.
/// </summary>
public sealed class TransportLive : ITransportDeribit
{
    private readonly HttpClient _http;
    private readonly Uri _urlWebSocket;
    private readonly LimiteurDebit _limiteur;
    private readonly ILogger _log;
    private readonly HashSet<string> _canaux = new();
    private readonly object _verrouCanaux = new();
    private readonly Channel<MessageFlux> _messages = Channel.CreateBounded<MessageFlux>(new BoundedChannelOptions(50_000) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly SemaphoreSlim _verrouEnvoi = new(1, 1);
    private readonly CancellationTokenSource _arret = new();
    private ClientWebSocket? _ws;
    private Task? _boucle;
    private int _idRequete;

    /// <summary>
    /// On conserve les connexions, le limiteur REST et le journal qui seront utilisés pendant la session
    /// en direct.
    /// </summary>
    public TransportLive(HttpClient http, Uri urlWebSocket, LimiteurDebit limiteur, ILogger<TransportLive>? log = null)
    {
        _http = http;
        _urlWebSocket = urlWebSocket;
        _limiteur = limiteur;
        _log = log ?? NullLogger<TransportLive>.Instance;
    }

    public bool FluxTermine => false;

    /// <summary>
    /// On attend un jeton REST, on envoie les paramètres encodés puis on contrôle les erreurs HTTP et
    /// JSON-RPC avant de rendre le résultat.
    /// </summary>
    public async Task<ReponseRest> AppelerAsync(string methode, IReadOnlyDictionary<string, string> parametres, CancellationToken ct = default)
    {
        await _limiteur.AttendreAsync(ct).ConfigureAwait(false);
        string requete = methode + "?" + string.Join("&", parametres.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        using var reponse = await _http.GetAsync(requete, ct).ConfigureAwait(false);
        string corps = await reponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(corps);
        var racine = doc.RootElement;
        if (racine.TryGetProperty("error", out var erreur))
            throw new DonneesIndisponiblesException($"Deribit a refusé {methode} : {erreur.GetRawText()}");
        if (!reponse.IsSuccessStatusCode)
            throw new DonneesIndisponiblesException($"HTTP {(int)reponse.StatusCode} sur {methode}.");

        return new ReponseRest(racine.GetProperty("result").Clone(), HorodatageServeur(racine));
    }

    /// <summary>
    /// On mémorise les canaux demandés sans doublons et on démarre la réception au premier abonnement.
    /// </summary>
    public async Task SouscrireAsync(IEnumerable<string> canaux, CancellationToken ct = default)
    {
        List<string> nouveaux;
        lock (_verrouCanaux) nouveaux = canaux.Where(_canaux.Add).ToList();
        if (_boucle is null)
        {
            _boucle = Task.Run(() => BoucleReceptionAsync(_arret.Token));
            return; // On laisse la boucle souscrire tous les canaux dès que la connexion est établie.
        }
        if (nouveaux.Count > 0 && _ws is { State: WebSocketState.Open })
            await EnvoyerSouscriptionAsync(nouveaux, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// On lit les messages placés dans la file de réception pour les transmettre un à un à la source de
    /// marché.
    /// </summary>
    public async IAsyncEnumerable<MessageFlux> LireFluxAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (await _messages.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            while (_messages.Reader.TryRead(out var message))
                yield return message;
    }

    /// <summary>
    /// On ouvre le WebSocket, on rétablit ses abonnements et on reçoit les messages. On retente la
    /// connexion avec une attente progressive après une coupure.
    /// </summary>
    private async Task BoucleReceptionAsync(CancellationToken ct)
    {
        var attente = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                _ws = ws;
                await ws.ConnectAsync(_urlWebSocket, ct).ConfigureAwait(false);
                _log.LogInformation("WebSocket Deribit connecté.");
                await EnvoyerAsync("public/set_heartbeat", new { interval = 30 }, ct).ConfigureAwait(false);
                List<string> tous;
                lock (_verrouCanaux) tous = _canaux.ToList();
                if (tous.Count > 0) await EnvoyerSouscriptionAsync(tous, ct).ConfigureAwait(false);
                attente = TimeSpan.FromSeconds(1);

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    string? texte = await RecevoirAsync(ws, ct).ConfigureAwait(false);
                    if (texte is null) break;
                    await TraiterAsync(texte, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or JsonException)
            {
                _log.LogWarning(ex, "WebSocket interrompu, reconnexion dans {Attente}.", attente);
            }
            await Task.Delay(attente, ct).ConfigureAwait(false);
            attente = TimeSpan.FromSeconds(Math.Min(attente.TotalSeconds * 2, 30)); // On augmente progressivement l'attente, sans dépasser 30 secondes, pour éviter de multiplier les tentatives pendant une coupure.
        }
    }

    /// <summary>
    /// On répond aux demandes de heartbeat et on conserve uniquement les notifications d’abonnement
    /// contenant une donnée de marché.
    /// </summary>
    private async Task TraiterAsync(string texte, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(texte);
        var racine = doc.RootElement;
        if (!racine.TryGetProperty("method", out var methode)) return; // On ignore ici les accusés de souscription, qui ne contiennent pas de cotation à diffuser.

        if (methode.GetString() == "heartbeat")
        {
            if (racine.GetProperty("params").GetProperty("type").GetString() == "test_request")
                await EnvoyerAsync("public/test", new { }, ct).ConfigureAwait(false);
            return;
        }
        if (methode.GetString() == "subscription")
        {
            var p = racine.GetProperty("params");
            await _messages.Writer.WriteAsync(new MessageFlux(p.GetProperty("channel").GetString()!, p.GetProperty("data").Clone(), DateTime.UtcNow), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// On répartit les canaux en lots de cent avant d’envoyer les demandes d’abonnement.
    /// </summary>
    private async Task EnvoyerSouscriptionAsync(IReadOnlyList<string> canaux, CancellationToken ct)
    {
        foreach (var lot in canaux.Chunk(100))
            await EnvoyerAsync("public/subscribe", new { channels = lot }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// On sérialise une requête JSON-RPC et on protège l’envoi pour ne pas superposer deux messages sur
    /// le même WebSocket.
    /// </summary>
    private async Task EnvoyerAsync(string methode, object parametres, CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open) return;
        byte[] octets = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = Interlocked.Increment(ref _idRequete), method = methode, @params = parametres });
        await _verrouEnvoi.WaitAsync(ct).ConfigureAwait(false);
        try { await ws.SendAsync(octets, WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
        finally { _verrouEnvoi.Release(); }
    }

    /// <summary>
    /// On assemble tous les fragments d’un message WebSocket avant de décoder son texte UTF-8.
    /// </summary>
    /// <returns>On renvoie null lorsque le serveur demande la fermeture de la connexion.</returns>
    private static async Task<string?> RecevoirAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var tampon = new byte[64 * 1024];
        using var flux = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(tampon, ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            flux.Write(tampon, 0, r.Count);
        } while (!r.EndOfMessage);
        return Encoding.UTF8.GetString(flux.GetBuffer(), 0, (int)flux.Length);
    }

    /// <summary>
    /// On convertit usOut, exprimé en microsecondes Unix, en date UTC. On utilise l’heure UTC locale si
    /// ce champ manque.
    /// </summary>
    private static DateTime HorodatageServeur(JsonElement racine) =>
        racine.TryGetProperty("usOut", out var us)
            ? DateTime.UnixEpoch.AddTicks(us.GetInt64() * 10)
            : DateTime.UtcNow;

    private bool _libere;

    /// <summary>
    /// On annule la réception, on attend sa fin puis on libère les connexions et leurs verrous une seule
    /// fois.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_libere) return;
        _libere = true;
        _arret.Cancel();
        if (_boucle is not null)
        {
            try { await _boucle.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* On attend la fin de la boucle avant de libérer ses ressources. */ }
        }
        _messages.Writer.TryComplete();
        _arret.Dispose();
        _verrouEnvoi.Dispose();
        _http.Dispose();
    }
}
