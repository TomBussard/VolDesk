using Pricer.Domaine.Booking;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Exports;
using Pricer.Infrastructure.Services;

namespace Pricer.Web.Services;

/// <summary>
/// On prépare le marché et le book en arrière-plan afin que les pages puissent afficher un état de
/// chargement dès le lancement. On sauvegarde la session prête lorsque le serveur s'arrête.
/// On ouvre la source dans le bloc protégé pour signaler un fichier absent sans bloquer l'interface.
/// </summary>
public sealed class DemarrageMarche(IServiceProvider services, EtatApplication etat, ILogger<DemarrageMarche> log)
    : BackgroundService
{
    /// <summary>
    /// On charge la source et le book en arrière-plan, puis on signale aux pages si les données sont
    /// prêtes ou indisponibles.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // On rend la main pour afficher le chargement sans attendre la connexion au marché.
        try
        {
            HorlogeMarche.Instance.Reinitialiser();
            var marche = services.GetRequiredService<ServiceMarche>();
            var session = services.GetRequiredService<ServiceSession>();
            await marche.DemarrerAsync(stoppingToken);
            await session.ChargerBookAsync();
            etat.MarquerPret();
            log.LogInformation("Marché chargé en mode {Mode}", services.GetRequiredService<OptionsPricer>().Mode);
        }
        catch (Exception ex) when (ex is PricerException or HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            log.LogError(ex, "Démarrage impossible");
            etat.MarquerErreur(ex.Message);
        }
    }

    /// <summary>
    /// On sauvegarde la session prête avant de laisser le service en arrière-plan terminer son arrêt.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (etat.Pret) services.GetRequiredService<ServiceSession>().Sauvegarder();
        await base.StopAsync(cancellationToken);
    }
}
