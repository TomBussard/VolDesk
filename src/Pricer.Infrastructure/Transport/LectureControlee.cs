using System.Diagnostics;

namespace Pricer.Infrastructure.Transport;

/// <summary>On contrôle la lecture sans faire avancer le temps pendant une pause.</summary>
public sealed class LectureControlee
{
    private readonly object _verrou = new();
    private bool _pause;
    private bool _pas;
    private double _vitesse;
    private double _avanceMs;

    /// <summary>
    /// On initialise la vitesse de lecture avec les mêmes limites que les changements de vitesse
    /// effectués ensuite.
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
    /// On bloque la lecture et on annule un éventuel pas en attente pour que la pause prenne le dessus.
    /// </summary>
    public void Pause() { lock (_verrou) { _pause = true; _pas = false; _avanceMs = 0; } }
    /// <summary>
    /// On relance la lecture continue avec la vitesse choisie et on efface une éventuelle demande de
    /// pas.
    /// </summary>
    public void Reprendre() { lock (_verrou) { _pause = false; _pas = false; } }
    /// <summary>
    /// On autorise un seul message supplémentaire tout en maintenant la lecture en pause.
    /// </summary>
    public void DonneeSuivante() { lock (_verrou) { _pause = true; _pas = true; } }

    /// <summary>
    /// On attend l’intervalle entre deux messages en tenant compte de la vitesse et des commandes de
    /// pause ou de pas.
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
            // On ne décompte que le temps effectivement écoulé en lecture.
            lock (_verrou)
                if (!pause && !_pause) restant -= chrono.Elapsed.TotalMilliseconds * vitesse;
        }
    }
}
