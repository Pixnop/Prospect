using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Core.Runtime;
using Prospect.Core.Storage;

namespace Prospect.Core.Diagnostics;

/// <summary>
/// Diagnostic STATIQUE et HORS LIGNE d'une instance : agrège cinq signaux qui existent déjà dans le
/// Core (version du jeu, runtime .NET, dépendances de mods, compatibilité de version de jeu, espace
/// disque) sans jamais toucher le réseau. Aucune des dépendances de cette classe ne sait faire une
/// requête HTTP — ce n'est pas une convention respectée à la main, c'est structurellement
/// impossible : <see cref="DiagnoseAsync"/> ne peut pas appeler ce dont il ne tient pas de référence.
/// </summary>
/// <remarks>
/// La vérification 4 (compatibilité de version de jeu) accepte le dernier
/// <see cref="InstanceUpdateReport"/> connu en paramètre plutôt que d'aller le chercher : ce cache
/// vit côté Desktop (<c>IModUpdateCheckCache</c>, en mémoire pour la session), le Core n'en a pas
/// besoin pour fonctionner et ne doit pas en dépendre (docs/architecture.md, séparation
/// logique/interface). Un appelant qui n'a rien à donner passe <see langword="null"/> : la
/// vérification retombe alors entièrement sur la provenance, ce qui reste correct, seulement moins
/// précis.
/// </remarks>
public sealed class InstanceDoctor
{
    /// <summary>
    /// Seuil sous lequel l'espace libre du volume de la racine Prospect déclenche un avertissement.
    /// 2 Gio : une installation du jeu pèse déjà plusieurs centaines de Mo, les mondes et les mods
    /// s'ajoutent par-dessus, et ce docteur n'est qu'un avertissement précoce, pas un blocage — la
    /// valeur vise à prévenir avant que ça ne coince, pas à alerter au moindre octet grignoté.
    /// </summary>
    public const long LowDiskSpaceThresholdBytes = 2L * 1024 * 1024 * 1024;

    private readonly IInstanceRepository _instances;
    private readonly IInstalledGameVersionRepository _gameVersions;
    private readonly IDotnetLocator _dotnetLocator;
    private readonly IInstalledModRepository _mods;
    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;

    public InstanceDoctor(
        IInstanceRepository instances,
        IInstalledGameVersionRepository gameVersions,
        IDotnetLocator dotnetLocator,
        IInstalledModRepository mods,
        IFileSystem fileSystem,
        AppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(gameVersions);
        ArgumentNullException.ThrowIfNull(dotnetLocator);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);

        _instances = instances;
        _gameVersions = gameVersions;
        _dotnetLocator = dotnetLocator;
        _mods = mods;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
    }

    /// <summary>
    /// Exécute les cinq vérifications pour <paramref name="slug"/>. Purement local : lecture
    /// d'<c>instance.json</c>, scan de <c>versions/&lt;version&gt;</c> et de <c>data/Mods/</c>,
    /// <c>dotnet --list-runtimes</c> (un processus local, pas une requête), lecture du volume. Rien
    /// de tout cela ne peut échouer pour cause de réseau injoignable.
    /// </summary>
    /// <param name="slug">Instance à diagnostiquer.</param>
    /// <param name="lastUpdateCheck">
    /// Dernier résultat connu d'une vérification de mises à jour pour cette instance, s'il en existe
    /// un pour la session en cours (voir la remarque de classe). <see langword="null"/> si aucune
    /// vérification n'a eu lieu.
    /// </param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour ce slug.</exception>
    public async Task<InstanceDoctorReport> DiagnoseAsync(
        string slug,
        InstanceUpdateReport? lastUpdateCheck = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);

        var instance = await _instances.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var gameVersion = instance.Metadata.GameVersion;

        var gameVersionResult = CheckGameVersion(gameVersion);

        // Sans court-circuit sur gameVersionResult : ReadRequirementAsync rend Unknown (donc
        // Indeterminate) de lui-même quand le dossier ou le runtimeconfig.json n'existent pas, la
        // même règle que suit déjà GameLauncher.
        var runtimeResult = await _dotnetLocator
            .CheckAsync(_gameVersions.GetVersionDirectory(gameVersion), cancellationToken)
            .ConfigureAwait(false);

        var installedMods = await _mods.ScanAsync(slug, cancellationToken).ConfigureAwait(false);
        var modIssues = CheckModDependencies(installedMods);
        var compatibility = CheckModCompatibility(installedMods, lastUpdateCheck);
        var diskSpace = CheckDiskSpace();

        return new InstanceDoctorReport(gameVersionResult, runtimeResult, modIssues, compatibility, diskSpace);
    }

    // Vérification 1 : IsInstalled distingue déjà « complète » de « pas complète », il reste à
    // distinguer « incomplète » (dossier présent, sentinelle absente) d'« absente » (aucun dossier)
    // pour proposer Installer plutôt que Réinstaller.
    private GameVersionDoctorResult CheckGameVersion(GameVersion gameVersion)
    {
        if (_gameVersions.IsInstalled(gameVersion))
        {
            return new GameVersionDoctorResult(GameVersionDoctorStatus.Installed, gameVersion);
        }

        var directory = _gameVersions.GetVersionDirectory(gameVersion);
        var status = _fileSystem.Directory.Exists(directory) ? GameVersionDoctorStatus.Incomplete : GameVersionDoctorStatus.Missing;

        return new GameVersionDoctorResult(status, gameVersion);
    }

    // Vérification 3 : machinerie existante (ModDependencyResolver), jamais resollicitée avec les
    // dépendances signalées par le ModDB (reportedByModDb: null) puisque ce croisement suppose un
    // appel réseau que ce docteur s'interdit. Un mod désactivé est ignoré comme candidat : il ne
    // sera pas chargé par le jeu, ses propres dépendances ne comptent donc pour rien tant qu'il
    // reste éteint — cohérent avec ModDependencyResolver.Evaluate, qui traite symétriquement un
    // mod désactivé comme un fournisseur qui ne fournit rien (ModDependencyStatus.Disabled).
    private static List<ModDoctorIssue> CheckModDependencies(IReadOnlyList<InstalledMod> installedMods)
    {
        var issues = new List<ModDoctorIssue>();

        foreach (var mod in installedMods)
        {
            if (!mod.IsIdentified)
            {
                issues.Add(new ModDoctorIssue(ModDoctorIssueKind.Unidentified, mod.DisplayName, Problem: mod.Problem));
                continue;
            }

            if (!mod.IsEnabled)
            {
                continue;
            }

            foreach (var dependency in ModDependencyResolver.FindUnsatisfied(mod.Info, installedMods, reportedByModDb: null))
            {
                issues.Add(new ModDoctorIssue(ModDoctorIssueKind.UnsatisfiedDependency, mod.DisplayName, Dependency: dependency));
            }
        }

        return issues;
    }

    // Vérification 4 : provenance d'abord (le seul signal garanti local et persisté), le dernier
    // résultat de vérification connu ensuite s'il couvre ce fichier précis (identifié par FileName,
    // stable que le mod soit activé ou non — voir InstalledMod.FileName). Les mods désactivés ou non
    // identifiés sont hors périmètre : les premiers ne tourneront pas avec cette version du jeu tant
    // qu'ils restent éteints, les seconds relèvent déjà de la vérification 3.
    private static ModCompatibilityDoctorResult CheckModCompatibility(
        IReadOnlyList<InstalledMod> installedMods,
        InstanceUpdateReport? lastUpdateCheck)
    {
        var confirmed = 0;
        var approximate = 0;
        var unknown = 0;
        var total = 0;

        foreach (var mod in installedMods.Where(mod => mod.IsEnabled && mod.IsIdentified))
        {
            total++;
            switch (ClassifyCompatibility(mod, lastUpdateCheck))
            {
                case ModLocalCompatibilitySignal.Confirmed:
                    confirmed++;
                    break;
                case ModLocalCompatibilitySignal.Approximate:
                    approximate++;
                    break;
                default:
                    unknown++;
                    break;
            }
        }

        return new ModCompatibilityDoctorResult(confirmed, approximate, unknown, total);
    }

    private enum ModLocalCompatibilitySignal
    {
        Confirmed,
        Approximate,
        Unknown,
    }

    private static ModLocalCompatibilitySignal ClassifyCompatibility(InstalledMod mod, InstanceUpdateReport? lastUpdateCheck)
    {
        var lastResult = lastUpdateCheck?.Mods
            .FirstOrDefault(result => string.Equals(result.Mod.FileName, mod.FileName, StringComparison.OrdinalIgnoreCase));

        if (lastResult is not null)
        {
            return lastResult.Status switch
            {
                // À jour ou en retard : dans les deux cas, une release EXISTE pour la version de jeu
                // de l'instance (ModUpdateChecker ne filtre que sur des releases compatibles, voir
                // ModReleaseSelector), donc ce qui tourne est confirmé compatible. Une mise à jour
                // disponible ne change rien à la compatibilité de la version déjà installée.
                ModUpdateStatus.UpToDate or ModUpdateStatus.UpdateAvailable => ModLocalCompatibilitySignal.Confirmed,
                _ => ModLocalCompatibilitySignal.Unknown,
            };
        }

        if (mod.Provenance is { } provenance)
        {
            return provenance.ApproximateMatch ? ModLocalCompatibilitySignal.Approximate : ModLocalCompatibilitySignal.Confirmed;
        }

        // Ni vérification récente ni provenance ModDB (mod déposé à la main) : rien de local ne
        // permet de juger.
        return ModLocalCompatibilitySignal.Unknown;
    }

    // Vérification 5 : le volume qui porte la racine Prospect, pas celui d'une instance précise —
    // toutes les instances et versions du jeu partagent la même racine (docs/architecture.md).
    private DiskSpaceDoctorResult CheckDiskSpace()
    {
        var drive = _fileSystem.DriveInfo.New(_appPaths.RootDirectory);

        return new DiskSpaceDoctorResult(drive.AvailableFreeSpace, LowDiskSpaceThresholdBytes);
    }
}