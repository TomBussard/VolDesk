using System.Globalization;
using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Structures;

/// <summary>
/// On rassemble les jambes d'une même échéance dans une structure pour appliquer leur recette et leur sens
/// de façon cohérente.
/// </summary>
public sealed class Structure
{
    private readonly List<Jambe> _jambes;

    /// <summary>
    /// On copie les jambes et on refuse une liste vide avant de retenir le type et l'échéance de la structure.
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
    /// On réutilise la description commune pour identifier la structure dans les messages et les tableaux.
    /// </summary>
    public override string ToString() => Description;
}
