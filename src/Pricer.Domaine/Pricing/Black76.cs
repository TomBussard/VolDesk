using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Pricing;

/// <summary>
/// On renvoie ensemble le prix USD, le prix crypto et les grecques pour utiliser les mêmes paramètres dans
/// tout le pricer.
/// </summary>
/// <param name="PrixUsd">On exprime le prix du modèle en USD.</param>
/// <param name="PrixBtc">On convertit le prix USD en crypto en le divisant par le future F.</param>
/// <param name="Grecques">On regroupe le delta, le gamma, le vega par point de volatilité et le theta par jour.</param>
/// <param name="VegaBrut">On garde aussi le vega brut, non divisé par 100, pour l’itération de Newton.</param>
public readonly record struct ResultatBlack76(double PrixUsd, double PrixBtc, Grecques Grecques, double VegaBrut);

/// <summary>
/// On applique Black-76 à taux nul. On calcule d1 et d2 à partir du future de
/// l'échéance, du strike, de la maturité et de la volatilité.
/// </summary>
public static class Black76
{
    /// <summary>
    /// On calcule le prix et les quatre grecques d'une option européenne sur future avec un delta en sous-jacent,
    /// un gamma par USD, un vega en USD par point de vol et un theta en USD par jour.
    /// </summary>
    /// <param name="future">Le prix du future de l'échéance en USD, strictement positif.</param>
    /// <param name="strike">Le strike de l'option en USD, strictement positif.</param>
    /// <param name="maturite">La durée restante en années ACT/365, positive ou nulle.</param>
    /// <param name="sigma">La volatilité décimale, positive ou nulle ; 0,5 représente 50 %.</param>
    /// <param name="type">Le type call ou put qui détermine le payoff et le signe du delta.</param>
    /// <returns>Les prix USD et crypto, les grecques et le vega brut utilisé pour inverser le prix.</returns>
    public static ResultatBlack76 Calculer(double future, double strike, double maturite, double sigma, TypeOption type)
    {
        if (!double.IsFinite(future) || !(future > 0)) throw new ArgumentOutOfRangeException(nameof(future), future, "Le future doit être strictement positif.");
        if (!double.IsFinite(strike) || !(strike > 0)) throw new ArgumentOutOfRangeException(nameof(strike), strike, "Le strike doit être strictement positif.");

        if (!double.IsFinite(maturite) || maturite < 0 || !double.IsFinite(sigma) || sigma < 0)
            throw new ArgumentOutOfRangeException(nameof(sigma), "Maturité et volatilité positives ou nulles et finies obligatoires.");

        // On revient au payoff à maturité ou volatilité nulle pour éviter une division par zéro.
        if (maturite <= 0 || sigma <= 0)
        {
            double intrinseque = type == TypeOption.Call ? Math.Max(future - strike, 0) : Math.Max(strike - future, 0);
            double deltaIntrinseque = type == TypeOption.Call ? (future > strike ? 1 : 0) : (future < strike ? -1 : 0);
            return new ResultatBlack76(intrinseque, intrinseque / future, new Grecques(deltaIntrinseque, 0, 0, 0), 0);
        }

        double racineT = Math.Sqrt(maturite);
        double volRacineT = sigma * racineT;
        double d1 = (Math.Log(future / strike) + 0.5 * sigma * sigma * maturite) / volRacineT;
        double d2 = d1 - volRacineT;
        double nd1 = LoiNormale.Densite(d1);

        double prix = type == TypeOption.Call
            ? future * LoiNormale.Cdf(d1) - strike * LoiNormale.Cdf(d2)
            : strike * LoiNormale.Cdf(-d2) - future * LoiNormale.Cdf(-d1);

        double delta = type == TypeOption.Call ? LoiNormale.Cdf(d1) : LoiNormale.Cdf(d1) - 1;
        double gamma = nd1 / (future * volRacineT);
        double vegaBrut = future * nd1 * racineT;
        double thetaBrut = -future * nd1 * sigma / (2 * racineT);

        // On convertit le vega par point de volatilité et le theta par jour pour garder les mêmes unités dans tout le pricer.
        var grecques = new Grecques(delta, gamma, vegaBrut / 100, thetaBrut / 365);
        return new ResultatBlack76(prix, prix / future, grecques, vegaBrut);
    }

    /// <summary>
    /// On propose une surcharge qui renvoie seulement le prix USD lorsque les autres résultats ne sont pas
    /// nécessaires.
    /// </summary>
    public static double Prix(double future, double strike, double maturite, double sigma, TypeOption type) =>
        Calculer(future, strike, maturite, sigma, type).PrixUsd;

    /// <summary>On accepte aussi un jeu de paramètres déjà regroupé pour éviter de les réécrire à chaque appel.</summary>
    public static double Prix(ParametresOption p) => Calculer(p.Future, p.Strike, p.Maturite, p.Sigma, p.Type).PrixUsd;
}

/// <summary>On regroupe les paramètres d'une option pour les transmettre au calcul sans perdre leurs unités.</summary>
public readonly record struct ParametresOption(double Future, double Strike, double Maturite, double Sigma, TypeOption Type);
