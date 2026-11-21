using Microsoft.AspNetCore.Components;

namespace Pricer.Web.Components;

/// <summary>
/// The page is refreshed as long as it is displayed to track the market. A default period of
/// 500 ms is used, which each page can adapt to its needs.
/// </summary>
public abstract class ComposantLive : ComponentBase, IDisposable
{
    private PeriodicTimer? _minuteur;
    private CancellationTokenSource? _arret;

    protected virtual TimeSpan Periode => TimeSpan.FromMilliseconds(500);
    protected bool EnPause { get; set; }

    /// <summary>
    /// A page-specific timer is started to update its results during its display.
    /// </summary>
    protected override void OnInitialized()
    {
        _arret = new CancellationTokenSource();
        _minuteur = new PeriodicTimer(Periode);
        _ = BoucleAsync(_arret.Token);
    }

    /// <summary>
    /// We wait for each tick without blocking the page, then recalculate and display the data
    /// on the Blazor context.
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
    /// We let each page define the calculations to be repeated before its display. By default,
    /// we simply reread the market condition already available.
    /// </summary>
    protected virtual Task AvantRafraichissementAsync() => Task.CompletedTask;

    /// <summary>
    /// Cancel the loop and release the timer to stop updates after the page starts.
    /// </summary>
    public virtual void Dispose()
    {
        // The timer is stopped by leaving the page to avoid unnecessary refreshments.
        _arret?.Cancel();
        _minuteur?.Dispose();
        _arret?.Dispose();
        GC.SuppressFinalize(this);
    }
}
