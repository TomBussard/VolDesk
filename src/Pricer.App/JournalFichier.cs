using Microsoft.Extensions.Logging;

namespace Pricer.App;

/// <summary>
/// On écrit les informations techniques et les erreurs dans un journal séparé pour laisser la console
/// lisible. On utilise un ILoggerProvider simple, sans ajouter de bibliothèque de journalisation.
/// </summary>
public sealed class JournalFichierProvider : ILoggerProvider
{
    private readonly StreamWriter _ecrivain;
    private readonly LogLevel _niveau;
    private readonly object _verrou = new();

    /// <summary>
    /// On ouvre le journal en ajout à la fin du fichier et on prépare le niveau minimal des messages
    /// conservés.
    /// </summary>
    /// <param name="fichier">On crée son dossier parent si nécessaire.</param>
    /// <param name="niveau">On conserve les messages de ce niveau ou d’un niveau plus élevé.</param>
    public JournalFichierProvider(string fichier, LogLevel niveau)
    {
        string? dossier = Path.GetDirectoryName(Path.GetFullPath(fichier));
        if (dossier is not null) Directory.CreateDirectory(dossier);
        _ecrivain = new StreamWriter(fichier, append: true) { AutoFlush = true };
        _niveau = niveau;
    }

    /// <summary>
    /// On associe une catégorie au journal pour identifier le service à l’origine de chaque message.
    /// </summary>
    public ILogger CreateLogger(string categoryName) => new Journal(this, categoryName);

    /// <summary>
    /// On ferme le fichier du journal lorsque le conteneur de services libère le fournisseur.
    /// </summary>
    public void Dispose() => _ecrivain.Dispose();

    /// <summary>
    /// On protège l’écriture d’une ligne afin que deux services ne mélangent pas leurs messages dans le
    /// fichier.
    /// </summary>
    private void Ecrire(string ligne)
    {
        lock (_verrou) _ecrivain.WriteLine(ligne);
    }

    /// <summary>On rattache chaque message à sa catégorie tout en partageant le même fichier et son verrou.</summary>
    /// <param name="parent">On délègue l’écriture et le filtre de niveau au fournisseur commun.</param>
    /// <param name="categorie">On indique le service à l’origine des messages.</param>
    private sealed class Journal(JournalFichierProvider parent, string categorie) : ILogger
    {
        /// <summary>
        /// On n’ajoute pas de contexte imbriqué à ce journal simple ; la catégorie suffit à identifier
        /// son origine.
        /// </summary>
        /// <returns>On renvoie null car aucun objet de contexte n’a besoin d’être libéré.</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// On vérifie le niveau avant de construire le texte d’un message qui pourrait être filtré.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= parent._niveau;

        /// <summary>
        /// On ajoute l’heure, le niveau et la catégorie au message, puis on écrit aussi le détail de
        /// l’exception lorsqu’il existe.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string ligne = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {categorie} : {formatter(state, exception)}";
            if (exception is not null) ligne += Environment.NewLine + exception;
            parent.Ecrire(ligne);
        }
    }
}
