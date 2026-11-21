using Microsoft.Extensions.Logging;
using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Modeles;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Deribit;
using Pricer.Infrastructure.Transport;

namespace Pricer.Infrastructure.Services;

/// <summary>
/// On choisit la source à partir de la configuration dans cette Factory. Le reste de l'application utilise
/// seulement <see cref="ISourceMarche"/>, ce qui évite de disperser les conditions sur les modes dans les
/// calculs.
/// </summary>
public static class FabriqueSourceDonnees
{
    /// <summary>
    /// On choisit le transport du mode actif, on reprend éventuellement la lecture à l’heure sauvegardée
    /// et on l’adapte à la source métier.
    /// </summary>
    public static ISourceMarche Creer(OptionsPricer options, ILoggerFactory journaux)
    {
        ITransportDeribit transport = options.Mode switch
        {
            ModeDonnees.Live => CreerLive(options, journaux),
            ModeDonnees.Enregistrement => new TransportEnregistreur(CreerLive(options, journaux), options.FichierEnregistrement),
            ModeDonnees.Rejeu => new TransportRejeu(options.FichierRejeu, options.VitesseRejeu),
            ModeDonnees.Demonstration => CreerDemonstration(options),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Mode de données inconnu.")
        };
        if (transport is TransportRejeu rejeu) rejeu.ReprendreA(options.ReprendreAu);
        return new SourceMarcheDeribit(transport, options.IntervalleTicker);
    }

    /// <summary>
    /// On génère une session fictive propre à la devise puis on la relit avec les mêmes commandes qu’un
    /// enregistrement réel.
    /// </summary>
    private static TransportRejeu CreerDemonstration(OptionsPricer options)
    {
        string fichier = Path.Combine(Path.GetDirectoryName(options.FichierSession)!, "demonstrations", $"demo-{options.Devise}.jsonl.gz");
        GenerateurDemonstration.Creer(fichier, options.Devise);
        return new TransportRejeu(fichier, options.VitesseRejeu);
    }

    /// <summary>
    /// On prépare les connexions REST et WebSocket avec leur journal et la limite de débit configurée.
    /// </summary>
    private static TransportLive CreerLive(OptionsPricer options, ILoggerFactory journaux) => new(
        new HttpClient { BaseAddress = new Uri(options.UrlRest), Timeout = TimeSpan.FromSeconds(15) },
        new Uri(options.UrlWebSocket),
        new LimiteurDebit(options.RequetesParSeconde, options.Rafale),
        journaux.CreateLogger<TransportLive>());
}
