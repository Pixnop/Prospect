#if PROSPECT_CONFORMANCE_ENGINE
using System.IO.Abstractions;

using Atlas.XUnit;

using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Storage;
using Prospect.GameConformance.Tests.Support;

using Vintagestory.API.Config;

// Prospect.Core.Common.GameVersion (le nôtre) et Vintagestory.API.Config.GameVersion (celui du
// moteur, tiré du même espace de noms que GamePaths) partagent le même nom simple.
using GameVersion = Prospect.Core.Common.GameVersion;
using SystemClock = Prospect.Core.Common.SystemClock;

namespace Prospect.GameConformance.Tests;

/// <summary>
/// PREUVE DE L'HYPOTHÈSE : docs/architecture.md (section « Arborescence disque ») et le Core
/// supposent que le dataPath d'une instance contient, du point de vue du jeu, des sous-dossiers
/// nommés <c>Mods/</c>, <c>ModConfig/</c> et <c>Saves/</c> — exactement les noms que
/// <see cref="FileSystemInstalledModRepository"/>, <see cref="ModpackArchiveLayout"/> et
/// <see cref="FileSystemInstanceRepository"/> codent en dur côté launcher.
/// </summary>
/// <remarks>
/// <para>
/// Une nuance structurante, découverte en lisant le code d'Atlas avant d'écrire ce test : Atlas
/// choisit LUI-MÊME le dataPath de son serveur embarqué (<c>Path.GetTempPath()/atlas/&lt;guid&gt;</c>,
/// généré dans <c>Atlas.Internal.Hosting.ServerHost</c>) et ne l'expose nulle part dans sa surface
/// publique (<c>WorldOptions</c>/<c>AtlasWorldAttribute</c> n'ont aucun champ de dataPath). Il est
/// donc impossible de faire booter Atlas directement SUR le dossier <c>data/</c> d'une vraie
/// instance Prospect. Ce test confronte donc les deux dataPaths sur un terrain commun et vérifiable :
/// les noms de sous-dossiers que <c>Vintagestory.API.Config.GamePaths</c> — la même classe PUBLIQUE
/// que le jeu utilise pour lui-même, pas un détail interne d'Atlas — calcule pour N'IMPORTE QUEL
/// dataPath, comparés à ceux que le Core suppose pour le dataPath de la vraie instance créée ici.
/// Puis, pour fermer la boucle empiriquement plutôt que de se fier à la seule comparaison de noms,
/// un monde est déposé dans <c>data/Saves/</c> de la vraie instance et
/// <see cref="IInstanceRepository.ListWorldsAsync"/> est appelé pour vérifier que le Core le
/// retrouve bien à cet endroit précis.
/// </para>
/// <para>
/// Toute divergence nomme le dossier fautif dans le message d'échec.
/// </para>
/// </remarks>
[Trait("Category", "Conformance")]
public sealed class DataPathLayoutTests : AtlasScenarioBase, IDisposable
{
    private readonly ConformanceTempDirectory _tempDirectory = new();

    [ConformanceFact]
    public async Task RealInstanceDataDirectory_Should_MatchEngineDataPathConventions_When_ComparedByFolderName()
    {
        // ARRANGE : une vraie instance Prospect, sur le vrai système de fichiers (InstanceService
        // tel que Prospect.Desktop.CompositionRoot le compose, sans aucun mock).
        var fileSystem = new FileSystem();
        var appPaths = new AppPaths(new SystemAppEnvironment(), _tempDirectory.Path);
        var repository = new FileSystemInstanceRepository(
            fileSystem, appPaths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(repository, fileSystem, new SystemClock());

        var record = await service.CreateAsync("Conformance Layout", GameVersion.Parse("1.22.3"));
        var dataDirectory = repository.GetDataDirectory(record.Slug);

        // ACT : quelques ticks sur le vrai moteur, pour lui laisser le temps d'installer ses
        // propres dossiers sous son dataPath (le sien, pas celui de l'instance — voir la remarque
        // de cette classe).
        await World.Ticks(5);

        // ASSERT 1 : les noms que le moteur utilise pour Mods/ModConfig/Saves sous N'IMPORTE QUEL
        // dataPath.
        AssertSameFolderName(GamePaths.DataPathMods, FileSystemInstalledModRepository.ModsFolderName, "Mods");
        AssertSameFolderName(GamePaths.ModConfig, ModpackArchiveLayout.ModConfigFolderName, "ModConfig");

        // FileSystemInstanceRepository.SavesDirectoryName est un constante privée (aucune surface
        // publique ne l'expose) : on ne peut pas la lire depuis ce test, donc on ne peut pas la
        // comparer directement à GamePaths.Saves comme les deux lignes ci-dessus. La constante
        // "Saves" reprise ici EST l'hypothèse ; ASSERT 2 referme la boucle empiriquement, en
        // vérifiant que c'est bien SOUS CE NOM que le Core retrouve un monde qu'on y dépose.
        AssertSameFolderName(GamePaths.Saves, "Saves", "Saves");

        // ASSERT 2 : le Core retrouve bien un monde déposé sous le nom qu'il suppose (data/Saves/),
        // en passant par son unique point d'entrée public (ListWorldsAsync), jamais en révélant sa
        // constante privée depuis ce test.
        var savesDirectory = fileSystem.Path.Combine(dataDirectory, "Saves");
        fileSystem.Directory.CreateDirectory(savesDirectory);
        const string worldFileName = "conformance-world.vcdbs";
        fileSystem.File.WriteAllBytes(fileSystem.Path.Combine(savesDirectory, worldFileName), [0x01, 0x02, 0x03]);

        var worlds = await repository.ListWorldsAsync(record.Slug);

        Assert.True(
            worlds.Any(world => world.FileName == worldFileName),
            $"Le Core n'a pas retrouvé « {worldFileName} » via ListWorldsAsync alors qu'il a été déposé " +
            $"dans '{savesDirectory}'. L'hypothèse de nommage du dossier Saves/ est invalidée.");
    }

    public void Dispose() => _tempDirectory.Dispose();

    private static void AssertSameFolderName(string engineComputedPath, string prospectFolderName, string label)
    {
        var engineFolderName = Path.GetFileName(
            engineComputedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        Assert.True(
            string.Equals(engineFolderName, prospectFolderName, StringComparison.Ordinal),
            $"Divergence sur le dossier « {label} » : le moteur réel (Vintagestory.API.Config.GamePaths) " +
            $"utilise « {engineFolderName} » sous le dataPath, alors que Prospect suppose « {prospectFolderName} ». " +
            "L'hypothèse de nommage du dataPath est invalidée pour ce dossier.");
    }
}
#endif