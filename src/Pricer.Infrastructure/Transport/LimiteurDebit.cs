namespace Pricer.Infrastructure.Transport;

/// <summary>
/// A token bucket is used to allow a short burst, then limit the number of calls per second.
/// Time measurement is injected to test this mechanism without actually waiting.
/// </summary>
public sealed class LimiteurDebit
{
    private readonly double _parSeconde;
    private readonly double _capacite;
    private readonly Func<TimeSpan> _temps;
    private readonly object _verrou = new();
    private double _jetons;
    private TimeSpan _dernier;

    /// <summary>
    /// A reserve full of chips is prepared to allow an initial gust, then a regular flow.
    /// </summary>
    /// <param name="parSeconde">On ajoute ce nombre de jetons par seconde écoulée.</param>
    /// <param name="rafale">On limite à cette valeur le nombre de jetons disponibles simultanément.</param>
    /// <param name="temps">On peut fournir une mesure du temps contrôlée pour vérifier le limiteur sans attendre réellement.</param>
    public LimiteurDebit(double parSeconde, int rafale, Func<TimeSpan>? temps = null)
    {
        if (parSeconde <= 0 || rafale < 1) throw new ArgumentOutOfRangeException(nameof(parSeconde), "Débit et rafale doivent être positifs.");
        _parSeconde = parSeconde;
        _capacite = rafale;
        _jetons = rafale;
        var chrono = System.Diagnostics.Stopwatch.StartNew();
        _temps = temps ?? (() => chrono.Elapsed);
        _dernier = _temps();
    }

    public double JetonsDisponibles
    {
        get { lock (_verrou) { Remplir(); return _jetons; } }
    }

    /// <summary>
    /// One consumes a token when a request can go. Otherwise, one calculates the necessary time
    /// before trying again.
    /// </summary>
    /// <param name="attente">On indique le délai avant le prochain jeton si la réserve est insuffisante.</param>
    /// <returns>On renvoie vrai lorsqu’un jeton a été consommé, faux si l’appelant doit encore attendre.</returns>
    public bool TenterPrendre(out TimeSpan attente)
    {
        lock (_verrou)
        {
            Remplir();
            if (_jetons >= 1)
            {
                _jetons -= 1;
                attente = TimeSpan.Zero;
                return true;
            }
            attente = TimeSpan.FromSeconds((1 - _jetons) / _parSeconde);
            return false;
        }
    }

    /// <summary>
    /// We wait until a token can be consumed before letting go a REST request.
    /// </summary>
    public async Task AttendreAsync(CancellationToken ct = default)
    {
        while (!TenterPrendre(out var attente))
            await Task.Delay(attente, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// We add the tokens won since the last control without exceeding the gust capacity.
    /// </summary>
    private void Remplir()
    {
        var maintenant = _temps();
        _jetons = Math.Min(_capacite, _jetons + (maintenant - _dernier).TotalSeconds * _parSeconde);
        _dernier = maintenant;
    }
}
