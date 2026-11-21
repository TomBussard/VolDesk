using System.Globalization;
using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Structures;

/// <summary>
/// The legs of the same maturity are grouped together in a structure to apply their recipe and
/// meaning in a coherent way.
/// </summary>
public sealed class Structure
{
    private readonly List<Jambe> _jambes;

    /// <summary>
    /// We copy the legs and reject an empty list before we retain the type and maturity of the
    /// structure.
    /// </summary>
    internal Structure(TypeStructure type, IEnumerable<Jambe> jambes)
    {
        _jambes = jambes.ToList();
        if (_jambes.Count == 0) throw new ArgumentException("Une structure contient au moins une jambe.", nameof(jambes));
        Type = type;
        Echeance = _jambes[0].Echeance;
    }

    public TypeStructure Type { get; }
    public DateTime Echeance { get; }
    public IReadOnlyList<Jambe> Jambes => _jambes;
    public bool EstSurMesure => _jambes.Any(j => j is JambeSurMesure);

    public string Description =>
        $"{Type} " + Echeance.ToString("dMMMyy", CultureInfo.InvariantCulture).ToUpperInvariant() + " "
        + string.Join("/", _jambes.Select(j => string.Create(CultureInfo.InvariantCulture, $"{j.Strike:0}{(j.Type == TypeOption.Call ? "C" : "P")}")));

    /// <summary>
    /// The common description is re-used to identify the structure in messages and tables.
    /// </summary>
    public override string ToString() => Description;
}
