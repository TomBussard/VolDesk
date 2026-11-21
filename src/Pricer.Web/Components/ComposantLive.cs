using Microsoft.AspNetCore.Components;

namespace Pricer.Web.Components;

/// <summary>
/// On rafraîchit la page tant qu'elle est affichée pour suivre le marché. On utilise une période de 500 ms
/// par défaut, que chaque page peut adapter à ses besoins.
/// </summary>
public abstract class ComposantLive : ComponentBase, IDisposable
{
    private PeriodicTimer? _minuteur;
    private CancellationTokenSource? _arret;

    protected virtual TimeSpan Periode => TimeSpan.FromMilliseconds(500);
    protected bool EnPause { get; set; }

    /// <summary>
    /// On démarre un minuteur propre à la page pour actualiser ses résultats pendant son affichage.
    /// </summary>
    protected override void OnInitialized()
    {
        _arret = new CancellationTokenSource();
        _minuteur = new PeriodicTimer(Periode);
        _ = BoucleAsync(_arret.Token);
    }

    /// <summary>
    /// On attend chaque tick sans bloquer la page, puis on recalcule et on affiche les données sur le
    /// contexte Blazor.
    /// </summary>
    private async Task BoucleAsync(CancellationToken ct)
    {
        try
        {
            while (await _minuteur!.WaitForNextTickAsync(ct))
                if (!EnPause) await InvokeAsync(async () => { await AvantRafraichissementAsync(); StateHasChanged(); });
        }
        catch (OperationCanceledException) { /* On termine le rafraîchissement lorsque la page est quittée. */ }
    }

    /// <summary>
    /// On laisse chaque page définir les calculs à refaire avant son affichage. Par défaut, on relit
    /// simplement l'état du marché déjà disponible.
    /// </summary>
    protected virtual Task AvantRafraichissementAsync() => Task.CompletedTask;

    /// <summary>
    /// On annule la boucle et on libère le minuteur pour arrêter les mises à jour après le départ de la
    /// page.
    /// </summary>
    public virtual void Dispose()
    {
        // On arrête le minuteur en quittant la page pour ne pas conserver des rafraîchissements devenus inutiles.
        _arret?.Cancel();
        _minuteur?.Dispose();
        _arret?.Dispose();
        GC.SuppressFinalize(this);
    }
}
