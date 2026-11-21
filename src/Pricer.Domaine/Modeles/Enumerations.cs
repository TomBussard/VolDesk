namespace Pricer.Domaine.Modeles;

/// <summary>On distingue le call et le put pour appliquer leur formule de prix et leur payoff respectifs.</summary>
public enum TypeOption
{
    Call,
    Put
}

/// <summary>
/// On utilise la valeur numérique du sens comme signe dans les sommes de jambes, afin de traiter achat et
/// vente avec le même calcul.
/// </summary>
public enum Sens
{
    Achat = 1,
    Vente = -1
}

/// <summary>On énumère les structures proposées par l'application pour éviter des noms saisis librement dans le moteur.</summary>
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

/// <summary>On choisit la source de données par configuration pour changer de mode sans réécrire les calculs.</summary>
public enum ModeDonnees
{
    Live,
    Enregistrement,
    Rejeu,
    Demonstration
}

/// <summary>
/// On distingue les options cotées, les options OTC et les futures pour appliquer leurs règles de
/// valorisation et de règlement.
/// </summary>
public enum NatureJambe
{
    OptionCotee,
    OptionSurMesure,
    Future
}
