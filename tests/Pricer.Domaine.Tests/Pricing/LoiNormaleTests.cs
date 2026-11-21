using Pricer.Domaine.Pricing;

namespace Pricer.Domaine.Tests.Pricing;

[TestClass]
public class LoiNormaleTests
{
    /// <summary>
    /// The normal function is checked on tabulated values to control the accuracy of the
    /// pricing.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.0, 0.5)]
    [DataRow(1.5, 0.933192798731142)]
    [DataRow(-2.3, 0.010724110021676)]
    public void Cdf_ValeursDeReference_PrecisionDe1e12(double x, double attendu)
    {
        Assert.AreEqual(attendu, LoiNormale.Cdf(x), 1e-12);
    }

    /// <summary>
    /// We check the symmetry of normal law on both sides of zero.
    /// </summary>
    [TestMethod]
    public void Cdf_Symetrie_NxPlusNmoinsxEgal1()
    {
        for (double x = -8; x <= 8; x += 0.37)
            Assert.AreEqual(1.0, LoiNormale.Cdf(x) + LoiNormale.Cdf(-x), 1e-14);
    }

    /// <summary>
    /// It is verified that extreme cases do not produce a negative probability or greater than
    /// one.
    /// </summary>
    [TestMethod]
    public void Cdf_QueuesExtremes_RestentDansZeroUn()
    {
        Assert.AreEqual(0.0, LoiNormale.Cdf(-40), 1e-300);
        Assert.AreEqual(1.0, LoiNormale.Cdf(40));
    }

    /// <summary>
    /// The normalization of the density used in Greeks is checked.
    /// </summary>
    [TestMethod]
    public void Densite_EnZero_Vaut1SurRacineDe2Pi()
    {
        Assert.AreEqual(1 / Math.Sqrt(2 * Math.PI), LoiNormale.Densite(0), 1e-15);
    }
}
