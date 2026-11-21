using System.Text.Json;

namespace Pricer.Infrastructure.Transport;

/// <summary>
/// On conserve le résultat REST et l'horodatage du serveur pour relire les réponses dans leur contexte
/// temporel d'origine.
/// </summary>
public sealed record ReponseRest(JsonElement Resultat, DateTime HorodatageServeur);

/// <summary>
/// On conserve le canal, le contenu et l'heure de réception d'un message WebSocket pour pouvoir
/// l'enregistrer puis le rejouer.
/// </summary>
public sealed record MessageFlux(string Canal, JsonElement Donnees, DateTime Reception);

/// <summary>
/// On fait passer le JSON brut par une même interface de transport. On peut donc utiliser le réseau,
/// enregistrer les échanges ou relire un fichier sans changer l'analyse des données au-dessus.
/// </summary>
public interface ITransportDeribit : IAsyncDisposable
{
    /// <summary>
    /// On demande une réponse REST accompagnée de son horodatage, par le réseau ou depuis un
    /// enregistrement.
    /// </summary>
    Task<ReponseRest> AppelerAsync(string methode, IReadOnlyDictionary<string, string> parametres, CancellationToken ct = default);
    /// <summary>
    /// On demande le suivi des canaux utiles à la session sans imposer la façon dont le transport les
    /// alimente.
    /// </summary>
    Task SouscrireAsync(IEnumerable<string> canaux, CancellationToken ct = default);
    /// <summary>
    /// On fournit les messages au fur et à mesure de leur disponibilité, jusqu’à la fin du flux ou à son
    /// annulation.
    /// </summary>
    IAsyncEnumerable<MessageFlux> LireFluxAsync(CancellationToken ct);
    /// <summary>
    /// On distingue un flux encore actif d'un fichier arrivé à sa fin, pour adapter les commandes de
    /// lecture.
    /// </summary>
    bool FluxTermine { get; }
}

public static class CleRequete
{
    /// <summary>
    /// On trie les paramètres pour retrouver une réponse enregistrée même si l'appelant fournit les mêmes
    /// paramètres dans un autre ordre.
    /// </summary>
    public static string Construire(string methode, IReadOnlyDictionary<string, string> parametres) =>
        methode + "?" + string.Join("&", parametres.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
}
