namespace Pricer.Web.Services;

/// <summary>
/// Start-up status is shared between pages to display the load or error before using market
/// data.
/// </summary>
public sealed class EtatApplication
{
    public bool Pret { get; private set; }
    public string? Erreur { get; private set; }
    public DateTime Demarrage { get; } = DateTime.UtcNow;

    public event Action? Change;

    /// <summary>
    /// The data is made accessible and the pages are notified by deleting a possible start-up
    /// error.
    /// </summary>
    public void MarquerPret()
    {
        Pret = true;
        Erreur = null;
        Change?.Invoke();
    }

    /// <summary>
    /// The market is rendered unavailable and the message is sent to the pages to allow a
    /// resumption.
    /// </summary>
    public void MarquerErreur(string message)
    {
        Pret = false;
        Erreur = message;
        Change?.Invoke();
    }

    /// <summary>
    /// The pages are placed on hold during replacement or loading of the data source.
    /// </summary>
    public void MarquerChargement()
    {
        Pret = false;
        Erreur = null;
        Change?.Invoke();
    }
}
