using Prospect.Core.Http;
using Prospect.Core.Tests.Common;

using Shouldly;

namespace Prospect.Core.Tests.Http;

/// <summary>
/// La fenêtre glissante qui rend le MB/s affiché lisible. Ce que ces tests protègent n'est pas la
/// justesse d'une division, c'est la STABILITÉ : un débit réel constant doit s'afficher constant,
/// même quand les blocs arrivent par à-coups, et un vrai changement de débit doit finir par se voir.
/// </summary>
public sealed class DownloadSpeedEstimatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DefaultWindow_IsFourSeconds()
        => new DownloadSpeedEstimator(new FakeClock(Noon)).Window.ShouldBe(TimeSpan.FromSeconds(4));

    [Fact]
    public void ANonPositiveWindow_FallsBackToTheDefault()
        => new DownloadSpeedEstimator(new FakeClock(Noon), TimeSpan.Zero).Window.ShouldBe(DownloadSpeedEstimator.DefaultWindow);

    /// <summary>
    /// Le cœur de la correction. Le débit RÉEL est constant à 1 Mo/s, mais les blocs arrivent en
    /// dents de scie : une seconde à 2 Mo, la suivante à rien, et ainsi de suite. Le débit
    /// instantané oscillerait entre 0 et 2 Mo/s à chaque mesure — c'est exactement ce que
    /// l'utilisateur voyait. Une fois la fenêtre pleine, la sortie doit tenir sur 1 Mo/s.
    /// </summary>
    [Fact]
    public void ASawtoothArrivalAtAConstantRealRate_ReadsAsThatConstantRate()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        var received = 0L;
        var settled = new List<double>();
        for (var second = 1; second <= 12; second++)
        {
            // Une seconde sur deux livre 2 Mo, l'autre rien : 1 Mo/s en moyenne.
            received += second % 2 == 1 ? 2_000_000 : 0;
            clock.UtcNow = Noon.AddSeconds(second);
            var speed = estimator.Update(received);

            if (second >= 5)
            {
                settled.Add(speed);
            }
        }

        // ±10 % autour du Mo/s réel : l'ondulation résiduelle tient à la phase de la dent de scie
        // dans la fenêtre, pas au bruit de mesure. Sans fenêtre, l'écart type était de 100 %.
        settled.ShouldAllBe(speed => speed >= 900_000d && speed <= 1_100_000d);
    }

    /// <summary>
    /// Un VRAI changement de débit doit se voir, et se voir proprement : la lecture rejoint la
    /// nouvelle valeur en descendant, sans osciller autour ni la dépasser. C'est ce que « lissé »
    /// doit vouloir dire — pas « lent à réagir », et surtout pas « qui saute ».
    /// </summary>
    [Fact]
    public void WhenTheRealRateDrops_TheReadingWalksDownToItWithoutOscillating()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        var received = 0L;
        var speed = 0d;

        // Quatre secondes à 4 Mo/s, la fenêtre est pleine et la lecture est juste.
        for (var tick = 1; tick <= 40; tick++)
        {
            received += 400_000;
            clock.UtcNow = Noon.AddSeconds(tick * 0.1d);
            speed = estimator.Update(received);
        }

        speed.ShouldBe(4_000_000d, tolerance: 50_000d);

        // Puis la ligne tombe à 1 Mo/s. La lecture décroît à chaque mesure, jusqu'à la nouvelle
        // valeur, et ne la dépasse pas.
        var readings = new List<double>();
        for (var tick = 41; tick <= 80; tick++)
        {
            received += 100_000;
            clock.UtcNow = Noon.AddSeconds(tick * 0.1d);
            readings.Add(estimator.Update(received));
        }

        readings.Zip(readings.Skip(1)).ShouldAllBe(pair => pair.Second <= pair.First + 1d);
        readings.ShouldAllBe(reading => reading >= 1_000_000d - 50_000d);
        readings[^1].ShouldBe(1_000_000d, tolerance: 50_000d);
    }

    /// <summary>
    /// Un débit réellement constant, mesuré à un rythme IRRÉGULIER : c'est le cas réel, où
    /// l'estimateur est nourri à chaque bloc lu. La sortie ne doit pas dépendre du rythme.
    /// </summary>
    [Fact]
    public void ARealConstantRate_IsReadTheSameWhateverTheSamplingRhythm()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        var elapsed = 0d;
        var speed = 0d;
        foreach (var step in (double[])[0.05, 0.4, 0.02, 1.2, 0.1, 0.3, 0.9, 0.05, 1.5, 0.6, 0.8, 2.0, 0.1])
        {
            elapsed += step;
            clock.UtcNow = Noon.AddSeconds(elapsed);
            speed = estimator.Update((long)(elapsed * 500_000d));
        }

        speed.ShouldBe(500_000d, tolerance: 1_000d);
    }

    /// <summary>
    /// Démarrage honnête : tant que la fenêtre n'est pas pleine, la vitesse est celle du temps
    /// réellement écoulé sur les octets réellement reçus. Rien n'est extrapolé.
    /// </summary>
    [Fact]
    public void BeforeTheWindowIsFull_TheRateIsTheOneActuallyObserved()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        clock.UtcNow = Noon.AddSeconds(0.5);
        estimator.Update(1_000_000).ShouldBe(2_000_000d);

        clock.UtcNow = Noon.AddSeconds(2);
        estimator.Update(2_000_000).ShouldBe(1_000_000d);
    }

    [Fact]
    public void TwoMeasurementsAtTheSameInstant_ReadAsZeroRatherThanDividingByZero()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(500);

        estimator.Update(900).ShouldBe(0d);
    }

    [Fact]
    public void WithoutStart_TheFirstCallStartsTheWindow()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);

        estimator.Update(1000).ShouldBe(0d);

        clock.UtcNow = Noon.AddSeconds(1);
        estimator.Update(3000).ShouldBe(2000d);
    }

    /// <summary>
    /// Un vieux débit ne doit pas hanter l'affichage : au bout d'une fenêtre à l'arrêt complet, la
    /// vitesse tombe bien à zéro plutôt que de rester sur la dernière valeur connue.
    /// </summary>
    [Fact]
    public void AStalledTransfer_FallsToZeroOnceItsWindowHasPassed()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        clock.UtcNow = Noon.AddSeconds(1);
        estimator.Update(5_000_000).ShouldBe(5_000_000d);

        clock.UtcNow = Noon.AddSeconds(9);

        estimator.Update(5_000_000).ShouldBe(0d);
    }

    /// <summary>
    /// L'inverse : un transfert lent dont les mesures s'espacent au-delà de la fenêtre garde une
    /// vitesse honnête, et ne retombe pas à zéro alors qu'il avance.
    /// </summary>
    [Fact]
    public void ASlowTransferMeasuredLessOftenThanItsWindow_StillReportsWhatItReceives()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        clock.UtcNow = Noon.AddSeconds(10);

        estimator.Update(200_000).ShouldBe(20_000d);
    }

    /// <summary>
    /// Fin de téléchargement : la dernière mesure ne fait pas bondir la valeur, et le compteur
    /// final est bien celui du fichier complet.
    /// </summary>
    [Fact]
    public void AtTheEndOfATransfer_TheLastReadingStaysOnTheObservedRate()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        var received = 0L;
        for (var tick = 1; tick <= 40; tick++)
        {
            received += 100_000;
            clock.UtcNow = Noon.AddSeconds(tick * 0.2d);
            estimator.Update(received);
        }

        // Le dernier bloc arrive presque collé au précédent : sans fenêtre, il afficherait un débit
        // délirant. Ici la fenêtre absorbe.
        received += 1_000;
        clock.UtcNow = Noon.AddSeconds(8.001d);

        estimator.Update(received).ShouldBe(500_000d, tolerance: 5_000d);
    }

    /// <summary>
    /// Reprise ou bascule de miroir : les octets d'avant la coupure n'ont pas été reçus pendant la
    /// fenêtre, et les compter donnerait une vitesse inventée.
    /// </summary>
    [Fact]
    public void RestartingAfterAResume_DoesNotCountTheBytesReceivedBeforeTheCut()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        clock.UtcNow = Noon.AddSeconds(1);
        estimator.Update(10_000_000);

        estimator.Start(10_000_000);
        clock.UtcNow = Noon.AddSeconds(2);

        estimator.Update(10_100_000).ShouldBe(100_000d);
    }

    /// <summary>Horloge qui recule (ajustement système) : on repart plutôt que de rendre une aberration.</summary>
    [Fact]
    public void AClockGoingBackwards_RestartsTheWindow()
    {
        var clock = new FakeClock(Noon);
        var estimator = new DownloadSpeedEstimator(clock);
        estimator.Start(0);

        clock.UtcNow = Noon.AddSeconds(2);
        estimator.Update(1_000_000);
        clock.UtcNow = Noon.AddSeconds(-30);

        estimator.Update(1_100_000).ShouldBe(0d);
    }

    [Fact]
    public void Constructor_NullClock_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => new DownloadSpeedEstimator(null!));

    [Fact]
    public void Ratio_WithoutAKnownTotal_IsNull()
        => new DownloadProgress(500, null, 0d).Ratio.ShouldBeNull();

    [Fact]
    public void Ratio_WithAKnownTotal_IsTheFraction()
        => new DownloadProgress(500, 1000, 0d).Ratio.ShouldBe(0.5d);

    [Fact]
    public void Ratio_MoreBytesThanAnnounced_IsClampedToOne()
        => new DownloadProgress(1500, 1000, 0d).Ratio.ShouldBe(1d);

    [Fact]
    public void None_IsTheZeroedStartingPoint()
    {
        DownloadProgress.None.ReceivedBytes.ShouldBe(0);
        DownloadProgress.None.TotalBytes.ShouldBeNull();
        DownloadProgress.None.BytesPerSecond.ShouldBe(0d);
    }
}