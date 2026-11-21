namespace Pricer.Domaine.Exceptions;

/// <summary>
/// Business errors are grouped under a common exception to clearly present them in the
/// interface.
/// </summary>
public class PricerException : Exception
{
    /// <summary>
    /// We create a business error without a personalized message.
    /// </summary>
    public PricerException() : base() { }
    /// <summary>
    /// This business error is described with the message that the interface can display.
    /// </summary>
    public PricerException(string message) : base(message) { }
    /// <summary>
    /// The original message and error is retained to understand the cause of this business
    /// error.
    /// </summary>
    public PricerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A code is associated with the input error to distinguish it from the text displayed to the
/// user.
/// </summary>
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

/// <summary>
/// We refuse an inconsistent input with a code and an explanation that the interface can
/// display.
/// </summary>
public class SaisieInvalideException : PricerException
{
    public CodeErreurSaisie CodeErreur { get; }

    /// <summary>
    /// We create an input error without a custom message.
    /// </summary>
    public SaisieInvalideException() : base() { }
    /// <summary>
    /// This input error is described with the message that the interface will be able to
    /// display.
    /// </summary>
    public SaisieInvalideException(string message) : base(message) { }
    /// <summary>
    /// The original message and error is retained to understand the cause of this input error.
    /// </summary>
    public SaisieInvalideException(string message, Exception inner) : base(message, inner) { }
    /// <summary>
    /// The validation code is associated with the message to identify the input rule that
    /// failed.
    /// </summary>
    public SaisieInvalideException(CodeErreurSaisie code, string message) : base(message) => CodeErreur = code;
}

/// <summary>
/// It is indicated that a strike or OTC expiry is outside the range that the rated data allow
/// to interpolate.
/// </summary>
public class HorsPlageException : PricerException
{
    /// <summary>
    /// We create an interpolation range error without custom message.
    /// </summary>
    public HorsPlageException() : base() { }
    /// <summary>
    /// This error in the interpolation range is described with the message that the interface
    /// can display.
    /// </summary>
    public HorsPlageException(string message) : base(message) { }
    /// <summary>
    /// The original message and error is retained to understand the cause of this interpolation
    /// range error.
    /// </summary>
    public HorsPlageException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// It is explained why the chosen pair does not solve gamma and vega hedge.
/// </summary>
public class CouvertureImpossibleException : PricerException
{
    public double Determinant { get; }

    /// <summary>
    /// We create an impossibility of hedge without personalized message.
    /// </summary>
    public CouvertureImpossibleException() : base() { }
    /// <summary>
    /// This impossibility of hedge is described with the message that the interface can
    /// display.
    /// </summary>
    public CouvertureImpossibleException(string message) : base(message) { }
    /// <summary>
    /// The original message and error is retained to understand the cause of this impossibility
    /// of hedge.
    /// </summary>
    public CouvertureImpossibleException(string message, Exception inner) : base(message, inner) { }
    /// <summary>
    /// We keep the determinant with the message to explain the refusal of a cover pair.
    /// </summary>
    public CouvertureImpossibleException(string message, double determinant) : base(message) => Determinant = determinant;
}

/// <summary>
/// The absence of an indispensable data rather than a quote is reported.
/// </summary>
public class DonneesIndisponiblesException : PricerException
{
    /// <summary>
    /// We create a lack of market data without a personalized message.
    /// </summary>
    public DonneesIndisponiblesException() : base() { }
    /// <summary>
    /// This lack of market data is described with the message that the interface can display.
    /// </summary>
    public DonneesIndisponiblesException(string message) : base(message) { }
    /// <summary>
    /// The original message and error is retained to understand the cause of this lack of
    /// market data.
    /// </summary>
    public DonneesIndisponiblesException(string message, Exception inner) : base(message, inner) { }
}
