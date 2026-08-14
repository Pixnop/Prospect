using System.IO.Abstractions;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Estimation honnête de l'avancement d'un installeur qui ne dit rien : on regarde le dossier cible
/// grossir.
/// </summary>
/// <remarks>
/// <para>
/// L'installeur Inno silencieux ne publie aucun avancement et ne rend la main qu'à la fin. Il écrit
/// en revanche ses fichiers PROGRESSIVEMENT dans le dossier passé à <c>/DIR</c>, et cette taille
/// cumulée est observable. C'est une estimation, jamais une mesure : elle est étiquetée comme telle
/// dans l'interface (<c>installation · ~42 %</c>) plutôt que présentée comme un décompte exact.
/// </para>
/// <para>
/// Deux garde-fous rendent l'estimation supportable même quand le dénominateur est faux. Elle est
/// MONOTONE : un échantillon plus bas que le précédent (fichier temporaire remplacé, dossier
/// nettoyé en cours de route) ne fait jamais reculer la barre, parce qu'une barre qui recule est
/// pire que pas de barre du tout. Et elle est PLAFONNÉE sous 100 % : seul le retour du processus
/// autorise à dire que c'est fini, et une barre pleine devant un installeur qui travaille encore
/// serait un mensonge.
/// </para>
/// </remarks>
internal sealed class InstallDirectoryGrowthReporter
{
    /// <summary>
    /// Ce que l'installé pèse par rapport à l'installeur téléchargé.
    /// </summary>
    /// <remarks>
    /// L'installeur est une archive LZMA : le contenu déposé sur le disque est donc plus lourd que
    /// le <c>.exe</c> d'où il sort, sans qu'on sache de combien exactement — nous ne mesurons pas
    /// sous Windows, et le rapport dépend de la part d'assets déjà compressés (PNG, OGG) dans la
    /// version. 1,8 est le facteur retenu, choisi du côté PRUDENT : surestimer la taille attendue
    /// fait terminer la barre un peu court avant qu'elle ne saute à 100 %, alors que la sous-estimer
    /// la collerait au plafond de 99 % pendant la moitié de l'installation. La valeur se recale
    /// d'une mesure réelle : comparer la taille du dossier de version à celle du <c>.exe</c>
    /// correspondant dans <c>cache/downloads/</c>.
    /// </remarks>
    public const double ExpandedSizeFactor = 1.8d;

    /// <summary>Plafond d'affichage tant que le processus n'a pas rendu la main.</summary>
    public const double MaxReportedRatio = 0.99d;

    /// <summary>Période d'échantillonnage : assez fine pour que la barre vive, assez large pour ne pas marteler le disque.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    private readonly IFileSystem _fileSystem;
    private readonly string _targetDirectory;
    private readonly long _expectedBytes;
    private readonly IProgress<GameInstallProgress>? _progress;

    private double _lastRatio;

    private InstallDirectoryGrowthReporter(
        IFileSystem fileSystem,
        string targetDirectory,
        long expectedBytes,
        IProgress<GameInstallProgress>? progress)
    {
        _fileSystem = fileSystem;
        _targetDirectory = targetDirectory;
        _expectedBytes = expectedBytes;
        _progress = progress;
    }

    /// <summary>
    /// Construit l'observateur à partir de l'installeur téléchargé, ou rend <see langword="null"/>
    /// quand il n'y a rien à estimer : pas d'observateur d'avancement, ou une taille d'installeur
    /// illisible. Dans ce cas la phase reste franchement indéterminée, comme avant.
    /// </summary>
    public static InstallDirectoryGrowthReporter? TryCreate(
        IFileSystem fileSystem,
        string installerPath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (progress is null)
        {
            return null;
        }

        var installerBytes = TryReadLength(fileSystem, installerPath);
        if (installerBytes <= 0)
        {
            return null;
        }

        var expected = (long)(installerBytes * ExpandedSizeFactor);

        return new InstallDirectoryGrowthReporter(fileSystem, targetDirectory, expected, progress);
    }

    /// <summary>Publie un échantillon. Jamais décroissant, jamais au-dessus du plafond.</summary>
    public void Sample()
    {
        var ratio = Math.Clamp((double)MeasureDirectory() / _expectedBytes, 0d, MaxReportedRatio);
        if (ratio <= _lastRatio)
        {
            return;
        }

        _lastRatio = ratio;
        _progress?.Report(GameInstallProgress.ForInstalling(ratio, isEstimated: true, runsVendorInstaller: true));
    }

    /// <summary>
    /// Échantillonne jusqu'à ce que <paramref name="cancellationToken"/> se déclenche, c'est-à-dire
    /// jusqu'à ce que l'installeur rende la main. Ne lève jamais : l'annulation est la sortie
    /// NORMALE de cette boucle, et une estimation ratée ne doit pas faire échouer une installation
    /// qui, elle, se passe bien.
    /// </summary>
    public async Task RunAsync(
        TimeSpan interval,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delay);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await delay(interval, cancellationToken).ConfigureAwait(false);
                Sample();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Le dossier n'existe pas encore, ou disparaît sous nos pieds : zéro, pas une exception. Cet
    // observateur est décoratif, il n'a aucun droit de faire tomber l'installation.
    private long MeasureDirectory()
    {
        try
        {
            if (!_fileSystem.Directory.Exists(_targetDirectory))
            {
                return 0L;
            }

            var total = 0L;
            foreach (var path in _fileSystem.Directory.EnumerateFiles(_targetDirectory, "*", SearchOption.AllDirectories))
            {
                total += TryReadLength(_fileSystem, path);
            }

            return total;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    }

    private static long TryReadLength(IFileSystem fileSystem, string path)
    {
        try
        {
            var info = fileSystem.FileInfo.New(path);

            return info.Exists ? info.Length : 0L;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    }
}