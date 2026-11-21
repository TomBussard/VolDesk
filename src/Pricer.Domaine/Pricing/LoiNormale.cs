namespace Pricer.Domaine.Pricing;

/// <summary>
/// The normal law is calculated without adding a pricing library. Hart's rational approximation
/// is used in the version of West (2005), chosen for its double accuracy; the tests compare the
/// function with reference values.
/// </summary>
public static class LoiNormale
{
    private static readonly double UnSurRacineDeuxPi = 1.0 / Math.Sqrt(2 * Math.PI);

    /// <summary>
    /// The density of normal law, used in gamma, vega and theta, is calculated.
    /// </summary>
    public static double Densite(double x) => UnSurRacineDeuxPi * Math.Exp(-0.5 * x * x);

    /// <summary>
    /// The cumulative probability of normal law used in price and delta is calculated.
    /// </summary>
    public static double Cdf(double x)
    {
        double xAbs = Math.Abs(x);
        double queue;

        if (xAbs > 37)
        {
            queue = 0;
        }
        else
        {
            double e = Math.Exp(-xAbs * xAbs / 2);
            if (xAbs < 7.07106781186547)
            {
                double num = 3.52624965998911E-02 * xAbs + 0.700383064443688;
                num = num * xAbs + 6.37396220353165;
                num = num * xAbs + 33.912866078383;
                num = num * xAbs + 112.079291497871;
                num = num * xAbs + 221.213596169931;
                num = num * xAbs + 220.206867912376;

                double den = 8.83883476483184E-02 * xAbs + 1.75566716318264;
                den = den * xAbs + 16.064177579207;
                den = den * xAbs + 86.7807322029461;
                den = den * xAbs + 296.564248779674;
                den = den * xAbs + 637.333633378831;
                den = den * xAbs + 793.826512519948;
                den = den * xAbs + 440.413735824752;

                queue = e * num / den;
            }
            else
            {
                // A continuous fraction is used in the tails, where the probability becomes very small.
                double b = xAbs + 0.65;
                b = xAbs + 4 / b;
                b = xAbs + 3 / b;
                b = xAbs + 2 / b;
                b = xAbs + 1 / b;
                queue = e / b / 2.506628274631;
            }
        }

        return x > 0 ? 1 - queue : queue;
    }
}
