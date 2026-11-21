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
    /// The transport of the active mode is chosen, the reading is repeated at the saved time
    /// and adapted to the business source.
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
    /// We generate a fictitious session specific to the currency and then re-read it with the
    /// same commands as a real record.
    /// </summary>
    private static TransportRejeu CreerDemonstration(OptionsPricer options)
    {
        string fichier = Path.Combine(Path.GetDirectoryName(options.FichierSession)!, "demonstrations", $"demo-{options.Devise}.jsonl.gz");
        GenerateurDemonstration.Creer(fichier, options.Devise);
        return new TransportRejeu(fichier, options.VitesseRejeu);
    }

    /// <summary>
    /// REST and WebSocket connections are prepared with their log and the flow limit
    /// configured.
    /// </summary>
    private static TransportLive CreerLive(OptionsPricer options, ILoggerFactory journaux) => new(
        new HttpClient { BaseAddress = new Uri(options.UrlRest), Timeout = TimeSpan.FromSeconds(15) },
        new Uri(options.UrlWebSocket),
        new LimiteurDebit(options.RequetesParSeconde, options.Rafale),
        journaux.CreateLogger<TransportLive>());
}
