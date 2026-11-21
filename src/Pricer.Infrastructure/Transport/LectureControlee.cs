using System.Diagnostics;

namespace Pricer.Infrastructure.Transport;

/// <summary>
/// We control the reading without advancing time during a break.
/// </summary>
public sealed class LectureControlee
{
    private readonly object _verrou = new();
    private bool _pause;
    private bool _pas;
    private double _vitesse;
    private double _avanceMs;

    /// <summary>
    /// The reading speed shall be initialized with the same limits as the subsequent speed
    /// changes.
    /// </summary>
    /// <param name="vitesse">On utilise zéro pour la vitesse maximale, ou un facteur compris entre zéro et mille.</param>
    public LectureControlee(double vitesse) => Vitesse = vitesse;
    public bool EnPause { get { lock (_verrou) return _pause; } }
    public double Vitesse
    {
        get { lock (_verrou) return _vitesse; }
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > 1000)
                throw new ArgumentOutOfRangeException(nameof(value), "Vitesse entre 0 et 1000.");
            lock (_verrou) _vitesse = value;
        }
    }

    /// <summary>
    /// We block the reading and cancel a possible step on hold so that the break takes over.
    /// </summary>
    public void Pause() { lock (_verrou) { _pause = true; _pas = false; _avanceMs = 0; } }
    /// <summary>
    /// We restart the continuous reading with the chosen speed and erase a possible step
    /// request.
    /// </summary>
    public void Reprendre() { lock (_verrou) { _pause = false; _pas = false; } }
    /// <summary>
    /// Only one additional message is allowed while maintaining paused playback.
    /// </summary>
    public void DonneeSuivante() { lock (_verrou) { _pause = true; _pas = true; } }

    /// <summary>
    /// The interval between two messages is expected taking into account the speed and the
    /// pause or step controls.
    /// </summary>
    /// <param name="ecart">On reçoit l’intervalle d’origine entre les deux données enregistrées.</param>
    /// <param name="ct">On interrompt l’attente lorsque la session est annulée.</param>
    public async Task AttendreAsync(TimeSpan ecart, CancellationToken ct)
    {
        double restant;
        lock (_verrou) { restant = Math.Max(0, ecart.TotalMilliseconds) - _avanceMs; _avanceMs = 0; }
        var chrono = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            double vitesse;
            bool pause;
            lock (_verrou)
            {
                if (_pas) { _pas = false; _avanceMs = 0; return; }
                pause = _pause;
                vitesse = _vitesse;
                if (!pause && (restant <= 0 || vitesse == 0))
                {
                    _avanceMs = Math.Max(0, -restant);
                    return;
                }
            }
            chrono.Restart();
            await Task.Delay(10, ct).ConfigureAwait(false);
            // Only the actual reading time is counted.
            lock (_verrou)
                if (!pause && !_pause) restant -= chrono.Elapsed.TotalMilliseconds * vitesse;
        }
    }
}
