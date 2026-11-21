namespace Pricer.Domaine.Abstractions;

/// <summary>
/// On injecte l'heure de marché pour que le rejeu utilise la date du fichier et que les tests restent
/// reproductibles.
/// </summary>
public interface IHorloge
{
    DateTime Maintenant { get; }

    /// <summary>On fait avancer l'horloge avec les messages reçus, sans revenir à une date antérieure.</summary>
    void Synchroniser(DateTime horodatageMarche);
}

/// <summary>On donne aux écrans et aux calculs un même contrat pour recevoir les nouvelles cotations.</summary>
public interface IObservateur<in T>
{
    /// <summary>
    /// On transmet une nouvelle donnée à l'observateur pour qu'il mette à jour son propre état.
    /// </summary>
    void Actualiser(T donnee);
}

/// <summary>On regroupe les abonnements aux cotations pour prévenir les observateurs quand le marché change.</summary>
public interface ISujet<out T>
{
    /// <summary>
    /// On inscrit un observateur pour lui transmettre les prochaines données reçues.
    /// </summary>
    void AjouterObservateur(IObservateur<T> observateur);
    /// <summary>
    /// On retire un observateur pour arrêter de lui transmettre les mises à jour.
    /// </summary>
    void SupprimerObservateur(IObservateur<T> observateur);
}
