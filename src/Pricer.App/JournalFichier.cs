using Microsoft.Extensions.Logging;

namespace Pricer.App;

/// <summary>
/// Technical information and errors are written in a separate log to leave the console legible.
/// A simple ILggerProvider is used without adding a log library.
/// </summary>
public sealed class JournalFichierProvider : ILoggerProvider
{
    private readonly StreamWriter _ecrivain;
    private readonly LogLevel _niveau;
    private readonly object _verrou = new();

    /// <summary>
    /// The log is opened at the end of the file and the minimum level of the messages stored is
    /// prepared.
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
    /// A category is associated with the journal to identify the service that gave rise to each
    /// message.
    /// </summary>
    public ILogger CreateLogger(string categoryName) => new Journal(this, categoryName);

    /// <summary>
    /// The log file is closed when the service container releases the supplier.
    /// </summary>
    public void Dispose() => _ecrivain.Dispose();

    /// <summary>
    /// The writing of a line is protected so that two services do not mix their messages in the
    /// file.
    /// </summary>
    private void Ecrire(string ligne)
    {
        lock (_verrou) _ecrivain.WriteLine(ligne);
    }

    /// <summary>
    /// Each message is linked to its category while sharing the same file and its lock.
    /// </summary>
    /// <param name="parent">On délègue l’écriture et le filtre de niveau au fournisseur commun.</param>
    /// <param name="categorie">On indique le service à l’origine des messages.</param>
    private sealed class Journal(JournalFichierProvider parent, string categorie) : ILogger
    {
        /// <summary>
        /// We don't add an embedded context to this simple journal; the category is enough to
        /// identify its origin.
        /// </summary>
        /// <returns>On renvoie null car aucun objet de contexte n’a besoin d’être libéré.</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// Check the level before building the text of a message that could be filtered.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => logLevel >= parent._niveau;

        /// <summary>
        /// We add the hour, the level and the category to the message, then we also write the
        /// details of the exception when it exists.
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
