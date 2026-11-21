using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Pricer.Infrastructure.Transport;

/// <summary>
/// On enregistre chaque échange sur une ligne JSON avec son heure, son type et sa clé pour le retrouver
/// pendant le rejeu.
/// </summary>
public sealed record LigneEnregistree(DateTime T, string Type, string Cle, JsonElement Donnees);

/// <summary>
/// On ajoute l'enregistrement autour d'un transport existant : les données continuent de circuler
/// normalement et une copie horodatée est conservée dans le fichier JSON Lines.
/// </summary>
public sealed class TransportEnregistreur : ITransportDeribit
{
    public static readonly JsonSerializerOptions OptionsJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ITransportDeribit _interne;
    private readonly StreamWriter _ecrivain;
    private readonly SemaphoreSlim _verrou = new(1, 1);

    /// <summary>
    /// On entoure le transport fourni d’un fichier de capture neuf, avec compression si son nom se
    /// termine par .gz.
    /// </summary>
    /// <param name="fichier">On refuse d’écraser un fichier existant afin de conserver les captures précédentes.</param>
    public TransportEnregistreur(ITransportDeribit interne, string fichier)
    {
        _interne = interne;
        string? dossier = Path.GetDirectoryName(Path.GetFullPath(fichier));
        if (dossier is not null) Directory.CreateDirectory(dossier);
        // On compresse au fil de l'écriture lorsque le nom se termine par .gz, afin de réduire la taille des captures JSON.
        Stream flux = new FileStream(fichier, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        if (fichier.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) flux = new GZipStream(flux, CompressionLevel.Optimal);
        _ecrivain = new StreamWriter(flux) { AutoFlush = false };
    }

    public long LignesEcrites { get; private set; }
    public bool FluxTermine => _interne.FluxTermine;

    /// <summary>
    /// On exécute l’appel REST puis on enregistre sa réponse avec une clé stable avant de la rendre à
    /// l’appelant.
    /// </summary>
    public async Task<ReponseRest> AppelerAsync(string methode, IReadOnlyDictionary<string, string> parametres, CancellationToken ct = default)
    {
        var reponse = await _interne.AppelerAsync(methode, parametres, ct).ConfigureAwait(false);
        await EcrireAsync(new LigneEnregistree(reponse.HorodatageServeur, "rest", CleRequete.Construire(methode, parametres), reponse.Resultat)).ConfigureAwait(false);
        return reponse;
    }

    /// <summary>
    /// On laisse le transport interne gérer les abonnements ; les messages effectivement reçus seront
    /// enregistrés lors de leur lecture.
    /// </summary>
    public Task SouscrireAsync(IEnumerable<string> canaux, CancellationToken ct = default) => _interne.SouscrireAsync(canaux, ct);

    /// <summary>
    /// On écrit chaque message WebSocket dans la capture avant de le transmettre au consommateur du
    /// flux.
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
    /// On protège les écritures concurrentes et on vide périodiquement le tampon pour conserver une
    /// capture cohérente.
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
    /// On ferme le transport puis on termine l’écriture et la compression du fichier avant de libérer le
    /// verrou.
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
