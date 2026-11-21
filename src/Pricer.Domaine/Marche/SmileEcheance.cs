namespace Pricer.Domaine.Marche;

/// <summary>
/// Each strike is associated with the decimal volatility obtained by inverting its mark.
/// </summary>
public readonly record struct PointSmile(double Strike, double Vol);

/// <summary>
/// We combine the future, maturity and points of a expiry, sorted by strike, to interpolate
/// his smile.
/// </summary>
public sealed record SmileEcheance(DateTime Echeance, double Maturite, double Future, IReadOnlyList<PointSmile> Points);
