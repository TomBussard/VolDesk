using Pricer.Domaine.Abstractions;

namespace Pricer.Domaine.Marche;

/// <summary>
/// On partage une seule horloge de marché dans l'application et on protège ses accès concurrents. On
/// l'expose derrière une interface pour la remplacer facilement dans les tests ; en rejeu, elle avance avec
/// les données enregistrées.
/// </summary>
public sealed class HorlogeMarche : IHorloge
{
    private static readonly Lazy<HorlogeMarche> _instance = new(() => new HorlogeMarche());
    private readonly object _verrou = new();
    private DateTime? _dernierHorodatage;

    /// <summary>
    /// On réserve la création de l'horloge à Instance pour partager le même temps de marché dans
    /// l'application.
    /// </summary>
    private HorlogeMarche() { }

    public static HorlogeMarche Instance => _instance.Value;

    public DateTime Maintenant
    {
        get { lock (_verrou) return _dernierHorodatage ?? DateTime.UtcNow; }
    }

    /// <summary>
    /// On avance l'heure partagée avec un horodatage plus récent ; un message reçu en retard ne fait pas
    /// reculer les calculs.
    /// </summary>
    public void Synchroniser(DateTime horodatageMarche)
    {
        lock (_verrou)
        {
            if (_dernierHorodatage is null || horodatageMarche > _dernierHorodatage)
                _dernierHorodatage = horodatageMarche;
        }
    }

    /// <summary>
    /// On remet l'horloge à zéro quand on change de session, pour ne pas mélanger les dates de deux
    /// enregistrements.
    /// </summary>
    public void Reinitialiser()
    {
        lock (_verrou) _dernierHorodatage = null;
    }
}

/// <summary>On utilise une horloge contrôlée dans les tests pour choisir précisément l'instant du calcul.</summary>
/// <param name="maintenant">L'instant initial de marché que l'on veut utiliser dans le scénario.</param>
public sealed class HorlogeFixe(DateTime maintenant) : IHorloge
{
    public DateTime Maintenant { get; private set; } = maintenant;

    /// <summary>
    /// On avance l'horloge de test à l'instant demandé, sans revenir avant la date déjà atteinte.
    /// </summary>
    public void Synchroniser(DateTime horodatageMarche)
    {
        if (horodatageMarche > Maintenant) Maintenant = horodatageMarche;
    }
}
