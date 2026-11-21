using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Marche;

/// <summary>
/// The call and the put of the same strike are assembled to facilitate the display and the
/// choice of a replacement option.
/// </summary>
public sealed record PaireStrike(double Strike, Instrument? Call, Instrument? Put)
{
    /// <summary>
    /// The call or put of the striker is chosen to allow the same route to process both types
    /// of options.
    /// </summary>
    public Instrument? Option(TypeOption type) => type == TypeOption.Call ? Call : Put;
}

/// <summary>
/// The options are classified by expiry and then by strike. A dictionary for name searches
/// and dictionaries sorted to browse the strikes in order.
/// </summary>
public sealed class ReferentielOptions
{
    private readonly SortedDictionary<DateTime, SortedDictionary<double, PaireStrike>> _parEcheance = new();
    private readonly Dictionary<string, Instrument> _parNom = new();
    private readonly List<InstrumentFuture> _futures;

    public string Devise { get; }

    /// <summary>
    /// Options are indexed by name, expiry, and strike, then sorted for futures to facilitate
    /// research.
    /// </summary>
    public ReferentielOptions(string devise, IEnumerable<Instrument> options, IEnumerable<InstrumentFuture> futures)
    {
        Devise = devise;
        foreach (var option in options)
        {
            _parNom[option.Nom] = option;
            if (!_parEcheance.TryGetValue(option.Echeance, out var strikes))
            {
                strikes = new SortedDictionary<double, PaireStrike>();
                _parEcheance[option.Echeance] = strikes;
            }
            strikes.TryGetValue(option.Strike, out var paire);
            paire ??= new PaireStrike(option.Strike, null, null);
            strikes[option.Strike] = option.Type == TypeOption.Call ? paire with { Call = option } : paire with { Put = option };
        }
        _futures = futures.OrderBy(f => f.Echeance).ToList();
    }

    public IReadOnlyList<DateTime> Echeances => _parEcheance.Keys.ToList();
    public IReadOnlyList<InstrumentFuture> Futures => _futures;
    public IEnumerable<Instrument> Options => _parNom.Values;
    public int NombreOptions => _parNom.Count;

    /// <summary>
    /// One goes through the strikes of a expiry in increasing order and rejects a date
    /// absent from the benchmark.
    /// </summary>
    public IReadOnlyList<PaireStrike> Strikes(DateTime echeance) =>
        _parEcheance.TryGetValue(echeance, out var strikes)
            ? strikes.Values.ToList()
            : throw new SaisieInvalideException(CodeErreurSaisie.EcheanceInconnue, $"L'échéance {echeance:dd/MM/yyyy HH:mm} n'est pas cotée.");

    /// <summary>
    /// We are checking to see if a date belongs to the repository before we look for his
    /// strikes.
    /// </summary>
    public bool EcheanceCotee(DateTime echeance) => _parEcheance.ContainsKey(echeance);

    /// <summary>
    /// We look for an option by date, strike and type without replacing a neighbouring
    /// contract.
    /// </summary>
    public Instrument? Trouver(DateTime echeance, double strike, TypeOption type) =>
        _parEcheance.TryGetValue(echeance, out var strikes) && strikes.TryGetValue(strike, out var paire) ? paire.Option(type) : null;

    /// <summary>
    /// The instrument is found by its name or it is reported not to belong to the loaded
    /// repository.
    /// </summary>
    public Instrument Obtenir(string nom) =>
        _parNom.TryGetValue(nom, out var instrument) ? instrument : throw new DonneesIndisponiblesException($"Instrument inconnu : {nom}.");

    /// <summary>
    /// An option name is checked without triggering a search exception.
    /// </summary>
    public bool Contient(string nom) => _parNom.ContainsKey(nom);

    /// <summary>
    /// Use the future with the same expiry, then the nearest dated future. Fall back to the perpetual if neither is available.
    /// </summary>
    public InstrumentFuture FuturePourEcheance(DateTime echeance)
    {
        var dates = _futures.Where(f => !f.EstPerpetuel).ToList();
        var exact = dates.FirstOrDefault(f => f.Echeance == echeance);
        if (exact is not null) return exact;
        var proche = dates.OrderBy(f => Math.Abs((f.Echeance - echeance).TotalSeconds)).FirstOrDefault();
        return proche ?? _futures.FirstOrDefault() ?? throw new DonneesIndisponiblesException("Aucun future dans le référentiel.");
    }

    /// <summary>
    /// Nearby strikes are proposed in the refusal message to help the user correct his/her
    /// entry.
    /// </summary>
    public IReadOnlyList<double> StrikesLesPlusProches(DateTime echeance, double strike, int nombre = 3) =>
        _parEcheance.TryGetValue(echeance, out var strikes)
            ? strikes.Keys.OrderBy(k => Math.Abs(k - strike)).Take(nombre).OrderBy(k => k).ToList()
            : [];
}
