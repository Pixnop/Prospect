using Prospect.Core.Common;

namespace Prospect.Core.Http;

/// <summary>
/// Estime la vitesse d'un téléchargement en lissant les mesures par moyenne mobile exponentielle.
/// Sans lissage, l'affichage sauterait à chaque bloc reçu (le débit instantané d'un bloc de 128 Ko
/// est très bruité) et deviendrait illisible.
/// </summary>
internal sealed class DownloadSpeedEstimator
{
    private readonly IClock _clock;
    private readonly double _smoothing;

    private DateTimeOffset _lastSample;
    private long _lastBytes;
    private double _smoothedBytesPerSecond;
    private bool _started;

    /// <summary>
    /// Construit l'estimateur.
    /// </summary>
    /// <param name="clock">Horloge injectée, pour que les tests contrôlent le temps écoulé.</param>
    /// <param name="smoothing">Poids de la mesure la plus récente, entre 0 et 1. Plus il est bas, plus l'affichage est stable.</param>
    public DownloadSpeedEstimator(IClock clock, double smoothing = 0.3d)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _smoothing = smoothing;
    }

    /// <summary>Démarre (ou redémarre) la mesure à partir du cumul <paramref name="receivedBytes"/>.</summary>
    public void Start(long receivedBytes)
    {
        _lastSample = _clock.UtcNow;
        _lastBytes = receivedBytes;
        _smoothedBytesPerSecond = 0d;
        _started = true;
    }

    /// <summary>
    /// Intègre un nouveau cumul d'octets reçus et rend la vitesse lissée en octets par seconde.
    /// Deux mesures prises au même instant d'horloge laissent la vitesse inchangée plutôt que de
    /// diviser par zéro.
    /// </summary>
    public double Update(long receivedBytes)
    {
        if (!_started)
        {
            Start(receivedBytes);
            return 0d;
        }

        var now = _clock.UtcNow;
        var elapsed = (now - _lastSample).TotalSeconds;
        if (elapsed <= 0d)
        {
            return _smoothedBytesPerSecond;
        }

        var instant = Math.Max(0d, receivedBytes - _lastBytes) / elapsed;
        _smoothedBytesPerSecond = _smoothedBytesPerSecond <= 0d
            ? instant
            : (_smoothing * instant) + ((1d - _smoothing) * _smoothedBytesPerSecond);

        _lastSample = now;
        _lastBytes = receivedBytes;

        return _smoothedBytesPerSecond;
    }
}