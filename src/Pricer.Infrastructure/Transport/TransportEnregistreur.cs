using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Pricer.Infrastructure.Transport;

/// <summary>
/// We record every exchange on a JSON line with his hour, his type and his key to find him
/// during the replay.
/// </summary>
public sealed record LigneEnregistree(DateTime T, string Type, string Cle, JsonElement Donnees);

/// <summary>
/// We add the recording around an existing transport: the data continues to circulate normally
/// and a timed copy is kept in the JSON Lines file.
/// </summary>
public sealed class TransportEnregistreur : ITransportDeribit
{
    public static readonly JsonSerializerOptions OptionsJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ITransportDeribit _interne;
    private readonly StreamWriter _ecrivain;
    private readonly SemaphoreSlim _verrou = new(1, 1);

    /// <summary>
    /// We surround the transport provided by a new capture file, with compression if its name
    /// ends with .gz.
    /// </summary>
    /// <param name="fichier">On refuse d’écraser un fichier existant afin de conserver les captures précédentes.</param>
    public TransportEnregistreur(ITransportDeribit interne, string fichier)
    {
        _interne = interne;
        string? dossier = Path.GetDirectoryName(Path.GetFullPath(fichier));
        if (dossier is not null) Directory.CreateDirectory(dossier);
        // We compress as we write when the name ends with .gz, in order to reduce the size of JSON catches.
        Stream flux = new FileStream(fichier, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        if (fichier.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) flux = new GZipStream(flux, CompressionLevel.Optimal);
        _ecrivain = new StreamWriter(flux) { AutoFlush = false };
    }

    public long LignesEcrites { get; private set; }
    public bool FluxTermine => _interne.FluxTermine;

    /// <summary>
    /// Run the REST call and then record the answer with a stable key before returning it to
    /// the appellant.
    /// </summary>
    public async Task<ReponseRest> AppelerAsync(string methode, IReadOnlyDictionary<string, string> parametres, CancellationToken ct = default)
    {
        var reponse = await _interne.AppelerAsync(methode, parametres, ct).ConfigureAwait(false);
        await EcrireAsync(new LigneEnregistree(reponse.HorodatageServeur, "rest", CleRequete.Construire(methode, parametres), reponse.Resultat)).ConfigureAwait(false);
        return reponse;
    }

    /// <summary>
    /// Internal transport is allowed to manage subscriptions; messages actually received will
    /// be recorded when they are read.
    /// </summary>
    public Task SouscrireAsync(IEnumerable<string> canaux, CancellationToken ct = default) => _interne.SouscrireAsync(canaux, ct);

    /// <summary>
    /// We write every WebSocket message in the capture before transmitting it to the consumer
    /// of the stream.
    /// </summary>
    public async IAsyncEnumerable<MessageFlux> LireFluxAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var m in _interne.LireFluxAsync(ct).ConfigureAwait(false))
        {
            await EcrireAsync(new LigneEnregistree(m.Reception, "ws", m.Canal, m.Donnees)).ConfigureAwait(false);
            yield return m;
        }
    }

    /// <summary>
    /// Competing writings are protected and the buffer is emptied periodically to maintain
    /// consistent capture.
    /// </summary>
    private async Task EcrireAsync(LigneEnregistree ligne)
    {
        await _verrou.WaitAsync().ConfigureAwait(false);
        try
        {
            await _ecrivain.WriteLineAsync(JsonSerializer.Serialize(ligne, OptionsJson)).ConfigureAwait(false);
            if (++LignesEcrites % 200 == 0) await _ecrivain.FlushAsync().ConfigureAwait(false);
        }
        finally { _verrou.Release(); }
    }

    private bool _libere;

    /// <summary>
    /// We close the transport and then finish writing and compression the file before releasing
    /// the lock.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_libere) return;
        _libere = true;
        await _interne.DisposeAsync().ConfigureAwait(false);
        await _verrou.WaitAsync().ConfigureAwait(false);
        await _ecrivain.FlushAsync().ConfigureAwait(false);
        await _ecrivain.DisposeAsync().ConfigureAwait(false);
        _verrou.Dispose();
    }
}
