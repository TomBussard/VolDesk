namespace Pricer.Web.Services;

/// <summary>
/// On partage l'état de démarrage entre les pages pour afficher le chargement ou une erreur avant
/// d'utiliser les données du marché.
/// </summary>
public sealed class EtatApplication
{
    public bool Pret { get; private set; }
    public string? Erreur { get; private set; }
    public DateTime Demarrage { get; } = DateTime.UtcNow;

    public event Action? Change;

    /// <summary>
    /// On rend les données accessibles et on notifie les pages en effaçant une éventuelle erreur de
    /// démarrage.
    /// </summary>
    public void MarquerPret()
    {
        Pret = true;
        Erreur = null;
        Change?.Invoke();
    }

    /// <summary>
    /// On rend le marché indisponible et on transmet le message aux pages pour permettre une reprise.
    /// </summary>
    public void MarquerErreur(string message)
    {
        Pret = false;
        Erreur = message;
        Change?.Invoke();
    }

    /// <summary>
    /// On place les pages en attente pendant le remplacement ou le chargement de la source de données.
    /// </summary>
    public void MarquerChargement()
    {
        Pret = false;
        Erreur = null;
        Change?.Invoke();
    }
}
