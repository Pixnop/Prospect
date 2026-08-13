using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Desktop.Tests.Services;

/// <summary>
/// Garde de câblage du journal de diagnostic. <see cref="IAppLog"/> est le seul port du projet à
/// s'injecter par un paramètre de constructeur OPTIONNEL (voir sa remarque), et cette commodité a
/// une contrepartie : un service qui ne le recevrait pas n'écrirait rien, sans se plaindre et sans
/// qu'aucun test de comportement ne s'en aperçoive. La faute serait invisible jusqu'au jour où un
/// rapport de terrain arriverait avec un journal vide.
/// </summary>
/// <remarks>
/// Ce que ces tests vérifient n'est donc pas un comportement mais une RÉSOLUTION : le conteneur
/// réel livre bien un <see cref="FileAppLog"/> aux services qui journalisent. C'est la seule chose
/// que l'optionnalité met en danger.
/// </remarks>
public sealed class AppLogWiringTests
{
    private static readonly Type[] Journalising =
    [
        typeof(InstanceService),
        typeof(RunningInstanceTracker),
        typeof(GameLauncher),
        typeof(GameInstallService),
        typeof(ModInstallService),
        typeof(ModUpdateChecker),
        typeof(IInstalledModRepository),
        typeof(IDownloadManager),
        typeof(IGameVersionCatalog),
        typeof(Desktop.Services.IToastService),
    ];

    [Fact]
    public void TheContainer_ResolvesARealFileLog()
    {
        using var provider = TestServiceProviderFactory.Create(out _);

        provider.GetRequiredService<IAppLog>().ShouldBeOfType<FileAppLog>();
    }

    /// <summary>
    /// Chaque service qui journalise se construit depuis le conteneur réel. Un paramètre optionnel
    /// que le conteneur ne saurait pas résoudre lèverait ici, ce qui est précisément le but.
    /// </summary>
    [Fact]
    public void EveryJournalisingService_IsConstructibleFromTheRealContainer()
    {
        using var provider = TestServiceProviderFactory.Create(out _);

        foreach (var type in Journalising)
        {
            provider.GetService(type).ShouldNotBeNull($"{type.Name} devrait être résolvable par le conteneur.");
        }
    }

    /// <summary>
    /// Le vrai journal atteint bien un service du domaine : une désinstallation de mod passe par le
    /// dépôt résolu du conteneur, et sa ligne se retrouve dans le fichier que la page Journaux lit.
    /// </summary>
    /// <remarks>
    /// Bout en bout exprès, et sur un service dont le journal N'EST PAS passé à la main par la
    /// composition root : c'est le chemin qui repose entièrement sur la résolution du paramètre
    /// optionnel, donc le seul que ce test ait une raison d'exercer.
    /// </remarks>
    [Fact]
    public async Task AWrittenLine_ReachesTheFileTheLogsPageReads()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        var instances = provider.GetRequiredService<InstanceService>();
        var mods = provider.GetRequiredService<IInstalledModRepository>();

        var record = await instances.CreateAsync("Homestead", Core.Common.GameVersion.Parse("1.22.6"));

        TestDoubles.ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(record.Slug),
            "configlib-1.11.1.zip",
            TestDoubles.ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));

        var installed = (await mods.ScanAsync(record.Slug, CancellationToken.None)).ShouldHaveSingleItem();
        await mods.RemoveAsync(record.Slug, installed, CancellationToken.None);

        var lines = provider.GetRequiredService<Core.Diagnostics.AppLogService>().ReadTail();
        lines.ShouldContain(line => line.Text.Contains("Instance créée", StringComparison.Ordinal));
        lines.ShouldContain(line => line.Text.Contains("Mod désinstallé", StringComparison.Ordinal));
    }
}