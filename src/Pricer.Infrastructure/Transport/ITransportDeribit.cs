using System.Text.Json;

namespace Pricer.Infrastructure.Transport;

/// <summary>
/// The REST result and the time stamping of the server are retained to reread the answers in
/// their original time context.
/// </summary>
public sealed record ReponseRest(JsonElement Resultat, DateTime HorodatageServeur);

/// <summary>
/// The channel, content, and time of receipt of a WebSocket message is retained for recording
/// and replaying.
/// </summary>
public sealed record MessageFlux(string Canal, JsonElement Donnees, DateTime Reception);

/// <summary>
/// The raw JSON is passed through the same transport interface, so you can use the network,
/// save the exchanges or reread a file without changing the analysis of the data above.
/// </summary>
public interface ITransportDeribit : IAsyncDisposable
{
    /// <summary>
    /// A REST response is requested with its time stamp, via the network or from a recording.
    /// </summary>
    Task<ReponseRest> AppelerAsync(string methode, IReadOnlyDictionary<string, string> parametres, CancellationToken ct = default);
    /// <summary>
    /// Follow-up of useful channels is requested at the session without imposing the way in
    /// which transport feeds them.
    /// </summary>
    Task SouscrireAsync(IEnumerable<string> canaux, CancellationToken ct = default);
    /// <summary>
    /// Messages are provided as they become available, until the end of the stream or its
    /// cancellation.
    /// </summary>
    IAsyncEnumerable<MessageFlux> LireFluxAsync(CancellationToken ct);
    /// <summary>
    /// A still active stream is distinguished from a file that has come to an end, to adapt the
    /// playback commands.
    /// </summary>
    bool FluxTermine { get; }
}

public static class CleRequete
{
    /// <summary>
    /// The parameters are sorted to find a recorded response even if the caller provides the
    /// same parameters in another order.
    /// </summary>
    public static string Construire(string methode, IReadOnlyDictionary<string, string> parametres) =>
        methode + "?" + string.Join("&", parametres.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
}
