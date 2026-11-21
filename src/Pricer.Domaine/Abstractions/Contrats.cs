namespace Pricer.Domaine.Abstractions;

/// <summary>
/// Market time is injected so that the replay uses the file date and tests remain reproducible.
/// </summary>
public interface IHorloge
{
    DateTime Maintenant { get; }

    /// <summary>
    /// The clock is moved forward with the messages received, without returning to an earlier
    /// date.
    /// </summary>
    void Synchroniser(DateTime horodatageMarche);
}

/// <summary>
/// Screens and calculations are given the same contract to receive new listings.
/// </summary>
public interface IObservateur<in T>
{
    /// <summary>
    /// A new data is transmitted to the observer to update his/her own condition.
    /// </summary>
    void Actualiser(T donnee);
}

/// <summary>
/// The listing subscriptions are grouped together to warn observers when the market changes.
/// </summary>
public interface ISujet<out T>
{
    /// <summary>
    /// An observer is registered to provide the next data received.
    /// </summary>
    void AjouterObservateur(IObservateur<T> observateur);
    /// <summary>
    /// An observer is removed to stop transmitting the updates.
    /// </summary>
    void SupprimerObservateur(IObservateur<T> observateur);
}
