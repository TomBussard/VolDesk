namespace Pricer.Domaine.Modeles;

/// <summary>
/// The call and the put are distinguished to apply their respective price formula and payoff.
/// </summary>
public enum TypeOption
{
    Call,
    Put
}

/// <summary>
/// The numerical value of the meaning is used as a sign in the leg amounts, in order to process
/// purchase and sale with the same calculation.
/// </summary>
public enum Sens
{
    Achat = 1,
    Vente = -1
}

/// <summary>
/// The structures proposed by the application are listed to avoid freely entered names in the
/// engine.
/// </summary>
public enum TypeStructure
{
    CallSpread,
    PutSpread,
    ButterflyCall,
    ButterflyPut,
    OptionSurMesure,
    Straddle,
    Strangle,
    RiskReversal,
    OptionSimple
}

/// <summary>
/// The data source is selected by configuration to change the mode without rewriting the
/// calculations.
/// </summary>
public enum ModeDonnees
{
    Live,
    Enregistrement,
    Rejeu,
    Demonstration
}

/// <summary>
/// Listed options, TBT options and future options are distinguished for applying their
/// valuation and settlement rules.
/// </summary>
public enum NatureJambe
{
    OptionCotee,
    OptionSurMesure,
    Future
}
