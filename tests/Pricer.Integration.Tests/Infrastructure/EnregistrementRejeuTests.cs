using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pricer.Domaine.Exceptions;
using Pricer.Infrastructure.Deribit;
using Pricer.Infrastructure.Transport;

namespace Pricer.Integration.Tests.Infrastructure;

/// <summary>On fixe les réponses REST et les messages WebSocket en mémoire pour tester le transport sans réseau.</summary>
internal sealed class FauxTransport : ITransportDeribit
{
    public static readonly DateTime T0 = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
    public List<MessageFlux> Messages { get; } = new();
    public Dictionary<string, string> Reponses { get; } = new();
    public bool FluxTermine { get; private set; }

    /// <summary>
    /// On renvoie la réponse REST fixée par le test sans utiliser de connexion réseau.
    /// </summary>
    public Task<ReponseRest> AppelerAsync(string methode, IReadOnlyDictionary<string, string> parametres, CancellationToken ct = default) =>
        Task.FromResult(new ReponseRest(JsonDocument.Parse(Reponses[CleRequete.Construire(methode, parametres)]).RootElement.Clone(), T0));

    /// <summary>
    /// On accepte l'abonnement sans action réseau car les messages sont déjà préparés en mémoire.
    /// </summary>
    public Task SouscrireAsync(IEnumerable<string> canaux, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// On émet les messages préparés dans leur ordre pour rendre les essais d'enregistrement
    /// reproductibles.
    /// </summary>
    public async IAsyncEnumerable<MessageFlux> LireFluxAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var m in Messages)
        {
            await Task.Yield();
            yield return m;
        }
        FluxTermine = true;
    }

    /// <summary>
    /// On termine le faux transport, qui ne possède aucune ressource réseau à libérer.
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// On fabrique un ticker JSON avec un prix et un instant contrôlés pour les essais de transport.
    /// </summary>
    public static MessageFlux Tick(string instrument, double mark, DateTime t) => new(
        $"ticker.{instrument}.100ms",
        JsonDocument.Parse($$"""{"instrument_name":"{{instrument}}","timestamp":{{(long)(t - DateTime.UnixEpoch).TotalMilliseconds}},"mark_price":{{mark.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"mark_iv":50.0,"underlying_price":74000.0}""").RootElement.Clone(),
        t);
}

[TestClass]
public class EnregistrementRejeuTests
{
    private string _dossier = "";

    /// <summary>
    /// On crée un dossier temporaire propre à ce test pour isoler ses fichiers des autres exécutions.
    /// </summary>
    [TestInitialize]
    public void Preparer() => _dossier = Directory.CreateTempSubdirectory("pricer-tests-").FullName;

    /// <summary>
    /// On supprime les fichiers temporaires du scénario une fois ses vérifications terminées.
    /// </summary>
    [TestCleanup]
    public void Nettoyer() => Directory.Delete(_dossier, true);

    /// <summary>
    /// On prépare une réponse d'indice et cinq ticks espacés pour comparer le direct simulé au rejeu.
    /// </summary>
    private static FauxTransport Faux()
    {
        var f = new FauxTransport();
        f.Reponses[CleRequete.Construire("public/get_index_price", new Dictionary<string, string> { ["index_name"] = "btc_usd" })] = """{"index_price":73900.5}""";
        for (int i = 0; i < 5; i++)
            f.Messages.Add(FauxTransport.Tick("BTC-20NOV26-76000-C", 0.01 + i * 0.0001, FauxTransport.T0.AddMilliseconds(250 * i)));
        return f;
    }

    /// <summary>
    /// On vérifie que l'enregistrement simple ou compressé restitue les réponses et les ticks sans en
    /// changer l'ordre.
    /// </summary>
    [DataTestMethod]
    [DataRow("session.jsonl")]
    [DataRow("session.jsonl.gz")]
    public async Task EnregistrerPuisRejouer_MemesDonneesDansLeMemeOrdre(string nomFichier)
    {
        string fichier = Path.Combine(_dossier, nomFichier);
        var live = new SourceMarcheDeribit(new TransportEnregistreur(Faux(), fichier));
        var indiceLive = await live.ObtenirIndiceAsync("BTC");
        var marksLive = new List<double>();
        await foreach (var c in live.FluxCotationsAsync(CancellationToken.None)) marksLive.Add(c.Mark!.Value);
        await live.DisposeAsync();

        await using var rejeu = new SourceMarcheDeribit(new TransportRejeu(fichier, 0));
        var indiceRejeu = await rejeu.ObtenirIndiceAsync("BTC");
        var marksRejeu = new List<double>();
        await foreach (var c in rejeu.FluxCotationsAsync(CancellationToken.None)) marksRejeu.Add(c.Mark!.Value);

        Assert.AreEqual(indiceLive, indiceRejeu);
        CollectionAssert.AreEqual(marksLive, marksRejeu);
        Assert.IsTrue(rejeu.FluxTermine);
    }

    /// <summary>
    /// On vérifie le rythme du rejeu sur des messages espacés d'intervalles connus.
    /// </summary>
    [TestMethod]
    public async Task Rejouer_Vitesse10_RespecteLesEcartsDOrigineDivisesPar10()
    {
        string fichier = Path.Combine(_dossier, "s.jsonl");
        var faux = Faux(); // 5 messages espacés de 250 ms : 1 s au total
        await using (var e = new TransportEnregistreur(faux, fichier))
            await foreach (var _ in e.LireFluxAsync(CancellationToken.None)) { }

        var rejeu = new TransportRejeu(fichier, 10);
        var chrono = Stopwatch.StartNew();
        int n = 0;
        await foreach (var _ in rejeu.LireFluxAsync(CancellationToken.None)) n++;

        Assert.AreEqual(5, n);
        Assert.IsTrue(chrono.ElapsedMilliseconds >= 90, $"durée {chrono.ElapsedMilliseconds} ms : attendu ≈ 100 ms");
        Assert.IsTrue(chrono.ElapsedMilliseconds < 900, "le rejeu à ×10 ne doit pas durer 1 s");
    }

    /// <summary>
    /// On vérifie qu'un enregistrement incomplet ne fournit pas une réponse REST inventée.
    /// </summary>
    [TestMethod]
    public void Rejouer_RequeteNonEnregistree_DonneesIndisponibles()
    {
        string fichier = Path.Combine(_dossier, "vide.jsonl");
        File.WriteAllText(fichier, """{"t":"2026-11-11T10:00:00Z","type":"ws","cle":"ticker.X.100ms","donnees":{}}""" + "\n");
        var rejeu = new TransportRejeu(fichier, 1);

        Assert.ThrowsExceptionAsync<DonneesIndisponiblesException>(() =>
            rejeu.AppelerAsync("public/ticker", new Dictionary<string, string> { ["instrument_name"] = "X" }));
    }

    /// <summary>
    /// On vérifie que l'absence de fichier est signalée par l'exception métier prévue.
    /// </summary>
    [TestMethod]
    public void Rejouer_FichierAbsent_DonneesIndisponibles()
    {
        Assert.ThrowsException<DonneesIndisponiblesException>(() => new TransportRejeu(Path.Combine(_dossier, "absent.jsonl"), 1));
    }

    /// <summary>
    /// On vérifie que deux appels équivalents retrouvent la même réponse même si leurs paramètres sont
    /// rangés autrement.
    /// </summary>
    [TestMethod]
    public void CleRequete_IndependanteDeLOrdreDesParametres()
    {
        var a = CleRequete.Construire("m", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        var b = CleRequete.Construire("m", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        Assert.AreEqual(a, b);
    }
}
