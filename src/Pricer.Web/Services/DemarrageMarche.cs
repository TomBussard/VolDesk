using Pricer.Domaine.Booking;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Exports;
using Pricer.Infrastructure.Services;

namespace Pricer.Web.Services;

/// <summary>
/// The market and book are prepared in the background so that the pages can display a loading
/// state from the start. You save the ready session when the server stops. You open the source
/// in the protected block to report an absent file without blocking the interface.
/// </summary>
public sealed class DemarrageMarche(IServiceProvider services, EtatApplication etat, ILogger<DemarrageMarche> log)
    : BackgroundService
{
    /// <summary>
    /// The source and the book are loaded in the background, and then the pages are notified if
    /// the data is ready or unavailable.
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
    /// Save the ready session before leaving the service in the background to complete its
    /// shutdown.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (etat.Pret) services.GetRequiredService<ServiceSession>().Sauvegarder();
        await base.StopAsync(cancellationToken);
    }
}
