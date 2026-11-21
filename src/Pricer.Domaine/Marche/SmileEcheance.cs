namespace Pricer.Domaine.Marche;

/// <summary>On associe à chaque strike la volatilité décimale obtenue en inversant son mark.</summary>
public readonly record struct PointSmile(double Strike, double Vol);

/// <summary>On regroupe le future, la maturité et les points d'une échéance, triés par strike, pour interpoler son smile.</summary>
public sealed record SmileEcheance(DateTime Echeance, double Maturite, double Future, IReadOnlyList<PointSmile> Points);
