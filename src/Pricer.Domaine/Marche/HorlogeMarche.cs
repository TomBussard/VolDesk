using Pricer.Domaine.Abstractions;

namespace Pricer.Domaine.Marche;

/// <summary>
/// We share a single market clock in the application and protect its competing accesses. We
/// expose it behind an interface to easily replace it in tests; in replay, it advances with
/// recorded data.
/// </summary>
public sealed class HorlogeMarche : IHorloge
{
    private static readonly Lazy<HorlogeMarche> _instance = new(() => new HorlogeMarche());
    private readonly object _verrou = new();
    private DateTime? _dernierHorodatage;

    /// <summary>
    /// We reserve the creation of the clock to Instance to share the same market time in the
    /// application.
    /// </summary>
    private HorlogeMarche() { }

    public static HorlogeMarche Instance => _instance.Value;

    public DateTime Maintenant
    {
        get { lock (_verrou) return _dernierHorodatage ?? DateTime.UtcNow; }
    }

    /// <summary>
    /// The time shared with a more recent time stamp is advanced; a message received late does
    /// not reduce the calculations.
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
    /// We reset the clock to zero when we change session, so we don't mix the dates of two
    /// recordings.
    /// </summary>
    public void Reinitialiser()
    {
        lock (_verrou) _dernierHorodatage = null;
    }
}

/// <summary>
/// A controlled clock is used in the tests to select precisely the moment of calculation.
/// </summary>
/// <param name="maintenant">L'instant initial de marché que l'on veut utiliser dans le scénario.</param>
public sealed class HorlogeFixe(DateTime maintenant) : IHorloge
{
    public DateTime Maintenant { get; private set; } = maintenant;

    /// <summary>
    /// We move the test clock at the requested moment, without returning before the date
    /// already reached.
    /// </summary>
    public void Synchroniser(DateTime horodatageMarche)
    {
        if (horodatageMarche > Maintenant) Maintenant = horodatageMarche;
    }
}
