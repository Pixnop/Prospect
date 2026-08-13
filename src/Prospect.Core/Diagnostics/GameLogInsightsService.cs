using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.ModDb;

namespace Prospect.Core.Diagnostics;

/// <summary>
/// Croise le journal du dernier lancement d'une instance (<see cref="GameLogAnalyzer"/>) avec ce
/// que ses archives annoncent (<see cref="ModIntegrationScanner"/>) pour produire, par mod, ce que
/// l'onglet Mods affiche : le verdict du dernier lancement et les intégrations avec les autres
/// mods installés.
/// </summary>
/// <remarks>
/// <para>
/// Rien n'est persisté : le JOURNAL est la persistance, et il est relu à la demande. C'est aussi
/// pourquoi ce service ne connaît pas le chemin du journal et se le fait donner : il appartient au
/// domaine du lancement (<c>GameLauncher.GetLogFilePath</c>), et le lire suffit à ce service, qui
/// n'a aucune raison de dépendre de tout ce qui sait le produire.
/// </para>
/// <para>
/// Tout ce qui sort d'ici est HEURISTIQUE et INFORMATIF (docs/architecture.md, niveau 3 des
/// dépendances) : aucune de ces observations ne bloque une action, et une intégration non détectée
/// n'est pas une intégration absente.
/// </para>
/// </remarks>
public sealed class GameLogInsightsService
{
    private readonly IFileSystem _fileSystem;
    private readonly IInstalledModRepository _mods;
    private readonly ModIntegrationScanner _scanner;
    private readonly IClock _clock;
    private readonly IAppLog _log;

    /// <summary>Construit le service.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait, pour lire le journal.</param>
    /// <param name="mods">Mods installés de l'instance, source de vérité du dossier <c>Mods/</c>.</param>
    /// <param name="scanner">Analyse statique des archives.</param>
    /// <param name="clock">Horloge, pour horodater le résultat.</param>
    /// <param name="log">Journal de diagnostic ; optionnel, voir la remarque d'<see cref="IAppLog"/>.</param>
    public GameLogInsightsService(
        IFileSystem fileSystem,
        IInstalledModRepository mods,
        ModIntegrationScanner scanner,
        IClock clock,
        IAppLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(clock);

        _fileSystem = fileSystem;
        _mods = mods;
        _scanner = scanner;
        _clock = clock;
        _log = log ?? NullAppLog.Instance;
    }

    /// <summary>
    /// Analyse le journal <paramref name="logFilePath"/> pour l'instance <paramref name="slug"/>.
    /// </summary>
    /// <param name="slug">Instance concernée (son dossier <c>Mods/</c> est scanné).</param>
    /// <param name="logFilePath">
    /// Chemin du journal du dernier lancement, obtenu de <c>GameLauncher.GetLogFilePath</c>. Un
    /// fichier absent n'est pas une erreur : c'est une instance qui n'a jamais été lancée, et le
    /// résultat le dit (<see cref="InstanceLogInsights.HasLog"/>).
    /// </param>
    /// <param name="installedMods">
    /// Mods déjà scannés par l'appelant, s'il vient de le faire : un scan ouvre chaque archive
    /// pour en lire les métadonnées et l'icône, et l'onglet Mods en fait un juste avant d'appeler
    /// ici. <see langword="null"/> pour que ce service scanne lui-même.
    /// </param>
    /// <param name="cancellationToken">Annulation.</param>
    public async Task<InstanceLogInsights> AnalyzeAsync(
        string slug,
        string logFilePath,
        IReadOnlyList<InstalledMod>? installedMods = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(logFilePath);

        var installed = installedMods ?? await _mods.ScanAsync(slug, cancellationToken).ConfigureAwait(false);
        var hasLog = _fileSystem.File.Exists(logFilePath);

        var report = hasLog
            ? GameLogAnalyzer.Analyze(ReadLines(logFilePath), [.. installed.Select(Identify)])
            : GameLogReport.Empty;

        cancellationToken.ThrowIfCancellationRequested();

        var integrations = Merge(report.Integrations, ScanArchives(installed));
        var insights = new InstanceLogInsights(_clock.UtcNow, hasLog, report.Mods, integrations);

        _log.Write(
            AppLogLevel.Info,
            $"Journal de lancement analysé : instance « {slug} », {report.Mods.Count} mod(s) signalé(s), {integrations.Count} intégration(s) relevée(s).");

        return insights;
    }

    private static ModLogIdentity Identify(InstalledMod mod)
        => new(mod.Identity, mod.FileName, mod.Info?.Name);

    // Les archives ne sont lues que pour les mods ACTIFS et identifiés : un mod éteint ne patchera
    // rien au prochain lancement, et un mod dont l'archive n'a pas livré de modinfo.json n'a pas de
    // domaine auquel rapporter ses cibles.
    private List<ModIntegration> ScanArchives(IReadOnlyList<InstalledMod> installed)
    {
        var present = installed
            .Where(mod => mod.IsEnabled && mod.Info is not null)
            .Select(mod => mod.Info!.ModId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var integrations = new List<ModIntegration>();

        foreach (var mod in installed.Where(candidate => candidate.IsEnabled && candidate.Info is not null))
        {
            var domain = mod.Info!.ModId;
            foreach (var signal in _scanner.Scan(mod.FilePath, domain))
            {
                var nature = present.Contains(signal.TargetDomain)
                    ? ModIntegrationNature.Resolved
                    : signal.Conditional ? ModIntegrationNature.Optional : ModIntegrationNature.Missing;

                integrations.Add(new ModIntegration(domain, signal.TargetDomain, nature, signal.Evidence));
            }
        }

        return integrations;
    }

    // Une paire (source, cible) n'apparaît qu'une fois, sous sa nature la plus exigeante : ce que
    // le JEU a constaté au dernier lancement prime sur ce que l'archive annonce, parce qu'une
    // référence que le moteur a refusé de résoudre ne « fonctionne » pas, quoi qu'en dise le zip.
    private static IReadOnlyList<ModIntegration> Merge(IReadOnlyList<ModIntegration> fromLog, IReadOnlyList<ModIntegration> fromArchives)
    {
        var merged = new Dictionary<(string Source, string Target), ModIntegration>();

        foreach (var integration in fromArchives.Concat(fromLog))
        {
            var key = (integration.SourceModId.ToLowerInvariant(), integration.TargetModId.ToLowerInvariant());
            if (!merged.TryGetValue(key, out var existing) || Rank(integration.Nature) > Rank(existing.Nature))
            {
                merged[key] = integration;
            }
        }

        return [.. merged.Values
            .OrderBy(integration => integration.SourceModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(integration => integration.TargetModId, StringComparer.OrdinalIgnoreCase)];
    }

    private static int Rank(ModIntegrationNature nature) => nature switch
    {
        ModIntegrationNature.Missing => 2,
        ModIntegrationNature.Resolved => 1,
        _ => 0,
    };

    // Lecture en FLUX, ligne à ligne : un journal de session longue ne rentre pas forcément
    // confortablement en mémoire, et l'analyseur ne garde de toute façon que des agrégats. Une
    // lecture qui échoue en cours de route (fichier tronqué pendant qu'on le lit, verrou) rend ce
    // qui a été lu plutôt que rien : un rapport partiel vaut mieux qu'un écran muet.
    private IEnumerable<string> ReadLines(string path)
    {
        System.IO.StreamReader? reader = null;
        try
        {
            reader = new System.IO.StreamReader(_fileSystem.File.OpenRead(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reader = null;
        }

        if (reader is null)
        {
            yield break;
        }

        using (reader)
        {
            while (true)
            {
                string? line = null;
                var failed = false;
                try
                {
                    line = reader.ReadLine();
                }
                catch (IOException)
                {
                    failed = true;
                }

                if (failed || line is null)
                {
                    yield break;
                }

                yield return line;
            }
        }
    }
}