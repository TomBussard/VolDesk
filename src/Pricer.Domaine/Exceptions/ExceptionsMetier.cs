namespace Pricer.Domaine.Exceptions;

/// <summary>On regroupe les erreurs métier sous une exception commune pour les présenter clairement dans l'interface.</summary>
public class PricerException : Exception
{
    /// <summary>
    /// On crée une erreur métier sans message personnalisé.
    /// </summary>
    public PricerException() : base() { }
    /// <summary>
    /// On décrit cette erreur métier avec le message que l'interface pourra afficher.
    /// </summary>
    public PricerException(string message) : base(message) { }
    /// <summary>
    /// On conserve le message et l'erreur d'origine pour comprendre la cause de cette erreur métier.
    /// </summary>
    public PricerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>On associe un code à l'erreur de saisie pour la distinguer du texte affiché à l'utilisateur.</summary>
public enum CodeErreurSaisie
{
    StrikeNonCote = 1,
    OrdreStrikes = 2,
    ButterflyNonSymetrique = 3,
    QuantiteHorsPas = 4,
    EcheancesDifferentes = 5,
    QuantiteNonPositive = 6,
    NombreStrikes = 7,
    EcheanceInconnue = 8,
    QuantiteDebouclageExcessive = 9,
    TradeInconnu = 10
}

/// <summary>On refuse une saisie incohérente avec un code et une explication que l'interface peut afficher.</summary>
public class SaisieInvalideException : PricerException
{
    public CodeErreurSaisie CodeErreur { get; }

    /// <summary>
    /// On crée une erreur de saisie sans message personnalisé.
    /// </summary>
    public SaisieInvalideException() : base() { }
    /// <summary>
    /// On décrit cette erreur de saisie avec le message que l'interface pourra afficher.
    /// </summary>
    public SaisieInvalideException(string message) : base(message) { }
    /// <summary>
    /// On conserve le message et l'erreur d'origine pour comprendre la cause de cette erreur de saisie.
    /// </summary>
    public SaisieInvalideException(string message, Exception inner) : base(message, inner) { }
    /// <summary>
    /// On associe le code de validation au message pour identifier la règle de saisie qui a échoué.
    /// </summary>
    public SaisieInvalideException(CodeErreurSaisie code, string message) : base(message) => CodeErreur = code;
}

/// <summary>On indique qu'un strike ou une échéance OTC sort de la plage que les données cotées permettent d'interpoler.</summary>
public class HorsPlageException : PricerException
{
    /// <summary>
    /// On crée une erreur de plage d'interpolation sans message personnalisé.
    /// </summary>
    public HorsPlageException() : base() { }
    /// <summary>
    /// On décrit cette erreur de plage d'interpolation avec le message que l'interface pourra afficher.
    /// </summary>
    public HorsPlageException(string message) : base(message) { }
    /// <summary>
    /// On conserve le message et l'erreur d'origine pour comprendre la cause de cette erreur de plage
    /// d'interpolation.
    /// </summary>
    public HorsPlageException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>On explique pourquoi la paire choisie ne permet pas de résoudre la couverture gamma et vega.</summary>
public class CouvertureImpossibleException : PricerException
{
    public double Determinant { get; }

    /// <summary>
    /// On crée une impossibilité de couverture sans message personnalisé.
    /// </summary>
    public CouvertureImpossibleException() : base() { }
    /// <summary>
    /// On décrit cette impossibilité de couverture avec le message que l'interface pourra afficher.
    /// </summary>
    public CouvertureImpossibleException(string message) : base(message) { }
    /// <summary>
    /// On conserve le message et l'erreur d'origine pour comprendre la cause de cette impossibilité de
    /// couverture.
    /// </summary>
    public CouvertureImpossibleException(string message, Exception inner) : base(message, inner) { }
    /// <summary>
    /// On conserve le déterminant avec le message pour expliquer le refus d'une paire de couverture.
    /// </summary>
    public CouvertureImpossibleException(string message, double determinant) : base(message) => Determinant = determinant;
}

/// <summary>On signale l'absence d'une donnée indispensable au lieu de fabriquer une cotation.</summary>
public class DonneesIndisponiblesException : PricerException
{
    /// <summary>
    /// On crée une absence de donnée de marché sans message personnalisé.
    /// </summary>
    public DonneesIndisponiblesException() : base() { }
    /// <summary>
    /// On décrit cette absence de donnée de marché avec le message que l'interface pourra afficher.
    /// </summary>
    public DonneesIndisponiblesException(string message) : base(message) { }
    /// <summary>
    /// On conserve le message et l'erreur d'origine pour comprendre la cause de cette absence de donnée de
    /// marché.
    /// </summary>
    public DonneesIndisponiblesException(string message, Exception inner) : base(message, inner) { }
}
