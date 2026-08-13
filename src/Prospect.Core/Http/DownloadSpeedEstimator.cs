using Prospect.Core.Common;

namespace Prospect.Core.Http;

/// <summary>
/// Estime la vitesse d'un téléchargement sur une FENÊTRE GLISSANTE de temps : la vitesse rendue est
/// celle des <see cref="DefaultWindow"/> dernières secondes, pas celle du dernier bloc reçu.
/// </summary>
/// <remarks>
/// <para>
/// La version précédente lissait par moyenne mobile exponentielle, un poids fixe appliqué à chaque
/// MESURE. Son défaut n'était pas le lissage mais son unité : l'estimateur est nourri à chaque bloc
/// lu, donc plusieurs centaines de fois par seconde sur une bonne ligne et quelques fois par
/// seconde sur une mauvaise. La constante de temps effective dépendait ainsi du débit lui-même —
/// exactement la grandeur qu'on veut mesurer — et le MB/s affiché sautait dans tous les sens.
/// </para>
/// <para>
/// Une fenêtre de temps n'a pas ce défaut : quel que soit le rythme d'échantillonnage, la vitesse
/// rendue vaut <c>(octets reçus dans la fenêtre) / (durée de la fenêtre)</c>. Quatre secondes,
/// parce qu'en dessous de deux ou trois le bruit d'une ligne domestique reste visible, et qu'au-delà
/// de cinq la valeur met trop longtemps à reconnaître un vrai changement de débit.
/// </para>
/// <para>
/// Tant que la fenêtre n'est pas pleine, la vitesse rendue est celle du temps réellement écoulé
/// depuis le début, sur les octets réellement reçus : HONNÊTE plutôt qu'extrapolée sur une fenêtre
/// qu'on n'a pas encore observée. Symétriquement, un transfert dont les mesures s'espacent au-delà
/// de la fenêtre garde le dernier point antérieur comme borne, plutôt que de retomber à zéro alors
/// qu'il avance.
/// </para>
/// </remarks>
internal sealed class DownloadSpeedEstimator
{
    /// <summary>Largeur de la fenêtre par défaut.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Écart minimal entre deux points d'historique. Un point par bloc lu en ferait des dizaines de
    /// milliers par fenêtre sur une ligne rapide, pour une valeur identique : les mesures plus
    /// rapprochées ne créent pas de point, sans qu'aucun octet soit perdu puisque c'est le CUMUL
    /// reçu qui est mesuré, jamais un delta.
    /// </summary>
    private static readonly TimeSpan MinimumSampleInterval = TimeSpan.FromMilliseconds(100);

    private readonly IClock _clock;
    private readonly TimeSpan _window;

    // Une quarantaine de points au plus (fenêtre / écart minimal) : une liste est plus simple qu'une
    // file ici, parce que la coupure a besoin de regarder le SECOND point, pas seulement le premier.
    private readonly List<Sample> _samples = [];

    /// <summary>
    /// Construit l'estimateur.
    /// </summary>
    /// <param name="clock">Horloge injectée, pour que les tests contrôlent le temps écoulé.</param>
    /// <param name="window">Largeur de la fenêtre glissante. <see cref="DefaultWindow"/> si omise ou non positive.</param>
    public DownloadSpeedEstimator(IClock clock, TimeSpan? window = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _window = window is { Ticks: > 0 } value ? value : DefaultWindow;
    }

    /// <summary>Largeur de la fenêtre effectivement appliquée.</summary>
    public TimeSpan Window => _window;

    /// <summary>Démarre (ou redémarre) la mesure à partir du cumul <paramref name="receivedBytes"/>.</summary>
    /// <remarks>
    /// Redémarrer VIDE la fenêtre : après une bascule de miroir ou une reprise, les octets d'avant
    /// la coupure n'ont pas été reçus dans les dernières secondes, et les compter donnerait une
    /// vitesse inventée.
    /// </remarks>
    public void Start(long receivedBytes)
    {
        _samples.Clear();
        _samples.Add(new Sample(_clock.UtcNow, receivedBytes));
    }

    /// <summary>
    /// Intègre un nouveau cumul d'octets reçus et rend la vitesse en octets par seconde, moyennée
    /// sur la fenêtre. Deux mesures prises au même instant d'horloge rendent zéro plutôt que de
    /// diviser par zéro.
    /// </summary>
    public double Update(long receivedBytes)
    {
        if (_samples.Count == 0)
        {
            Start(receivedBytes);

            return 0d;
        }

        var now = _clock.UtcNow;
        var newest = _samples[^1];
        if (now < newest.Instant)
        {
            // Horloge qui recule (ajustement système) : la fenêtre n'a plus de sens, on repart.
            Start(receivedBytes);

            return 0d;
        }

        if (now - newest.Instant >= MinimumSampleInterval)
        {
            _samples.Add(new Sample(now, receivedBytes));
        }

        DropSamplesOutsideTheWindow(now);

        var oldest = _samples[0];
        var elapsed = (now - oldest.Instant).TotalSeconds;

        return elapsed > 0d ? Math.Max(0d, receivedBytes - oldest.Bytes) / elapsed : 0d;
    }

    /// <summary>
    /// Retire les points sortis de la fenêtre, en gardant TOUJOURS le dernier point antérieur à
    /// l'horizon : c'est lui qui borne la fenêtre tant qu'aucun autre n'y est entré, et sans lui un
    /// transfert lent verrait sa vitesse retomber à zéro entre deux mesures espacées.
    /// </summary>
    private void DropSamplesOutsideTheWindow(DateTimeOffset now)
    {
        var horizon = now - _window;
        var expired = 0;
        while (expired + 1 < _samples.Count && _samples[expired + 1].Instant <= horizon)
        {
            expired++;
        }

        if (expired > 0)
        {
            _samples.RemoveRange(0, expired);
        }
    }

    private readonly record struct Sample(DateTimeOffset Instant, long Bytes);
}