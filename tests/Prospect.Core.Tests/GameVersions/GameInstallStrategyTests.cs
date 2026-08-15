using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Tests.GameVersions.Inno;

using Shouldly;

namespace Prospect.Core.Tests.GameVersions;

public sealed class GameInstallStrategyTests
{
    private const UnixFileMode Mode755 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const string ArchivePath = "/data/prospect/cache/downloads/vs_client_linux-x64_1.22.6.tar.gz";
    private const string WindowsInstallerPath = "/data/prospect/cache/downloads/vs_install_win-x64_1.22.6.exe";
    private const string TargetDirectory = "/data/prospect/versions/1.22.6";

    private static MockFileSystem WithArchive(byte[] archive)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(ArchivePath, new MockFileData(archive));

        return fileSystem;
    }

    /// <summary>
    /// Le VRAI layout de l'archive client Linux, relevé le 2026-08-13 sur la 1.22.6 : tout est sous
    /// un dossier racine <c>vintagestory/</c>. C'est cette fixture-là qui manquait, et c'est
    /// pourquoi le défaut a traversé toute la suite sans être vu.
    /// </summary>
    /// <remarks>
    /// Elle porte en PERMANENCE un fichier vide, <c>assets/version-1.22.6.txt</c>, parce que
    /// l'archive réelle en porte un et qu'il pèse zéro octet. Une fixture dont tous les fichiers ont
    /// du contenu ne peut pas voir le défaut du 2026-08-13 (fichiers vides jamais matérialisés), et
    /// ce fichier-là n'est pas décoratif : le jeu s'appuie sur sa présence pour juger l'installation
    /// propre.
    /// </remarks>
    private static byte[] RealLinuxArchive() => TarGzSamples.Create(
        ("vintagestory/", null),
        ("vintagestory/Mods/", null),
        ("vintagestory/Vintagestory", TarGzSamples.Text("#!/bin/sh")),
        ("vintagestory/Vintagestory.dll", TarGzSamples.Text("IL")),
        ("vintagestory/assets/version-1.22.6.txt", TarGzSamples.Empty),
        ("vintagestory/assets/game/lang/fr.json", TarGzSamples.Text("{}")));

    [Fact]
    public async Task LinuxStrategy_RealTarGz_FlattensTheRootFolderTheArchiveCarries()
    {
        var fileSystem = WithArchive(RealLinuxArchive());
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        // Le jeu à la racine du dossier de version, exactement là où le lancement et la
        // vérification post-installation le cherchent.
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "Vintagestory")).ShouldBe("#!/bin/sh");
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "assets", "game", "lang", "fr.json")).ShouldBe("{}");
        fileSystem.Directory.Exists(fileSystem.Path.Combine(TargetDirectory, "Mods")).ShouldBeTrue();

        // Et surtout : plus de « versions/1.22.6/vintagestory/ », le dossier qui faisait échouer
        // toute installation Linux.
        fileSystem.Directory.Exists(fileSystem.Path.Combine(TargetDirectory, "vintagestory")).ShouldBeFalse();
        fileSystem.Directory.GetDirectories(TargetDirectory).Length.ShouldBe(2);
    }

    /// <summary>
    /// Un fichier de zéro octet est un FICHIER, pas une absence : il doit se retrouver sur le disque.
    /// </summary>
    /// <remarks>
    /// Le cas n'est pas théorique et il n'est pas cosmétique. L'archive client porte un
    /// <c>assets/version-&lt;version&gt;.txt</c> vide, et le jeu s'appuie sur sa présence pour juger
    /// qu'une installation est propre : sans lui, il ouvre au démarrage un avertissement affirmant
    /// que des fichiers d'une version précédente traînent, sur une installation pourtant neuve.
    /// </remarks>
    [Fact]
    public async Task LinuxStrategy_ZeroByteFile_IsMaterialisedInsteadOfBeingSkipped()
    {
        var fileSystem = WithArchive(RealLinuxArchive());
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        var marker = fileSystem.Path.Combine(TargetDirectory, "assets", "version-1.22.6.txt");
        fileSystem.File.Exists(marker).ShouldBeTrue();
        fileSystem.File.ReadAllBytes(marker).ShouldBeEmpty();
    }

    /// <summary>
    /// Le même fichier vide, mais dans un dossier que rien d'autre ne peuple : ses parents doivent
    /// être créés pour lui, exactement comme pour un fichier qui a du contenu.
    /// </summary>
    [Fact]
    public async Task LinuxStrategy_ZeroByteFileAlone_InItsFolder_GetsItsParentsCreated()
    {
        var archive = TarGzSamples.Create(
            ("Vintagestory", TarGzSamples.Text("#!/bin/sh")),
            ("assets/version-1.22.6.txt", TarGzSamples.Empty));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "assets", "version-1.22.6.txt")).ShouldBeTrue();
    }

    /// <summary>
    /// Un fichier vide compte comme une entrée écrite : il fait partie du relevé de topologie qui
    /// décide de l'aplatissement, sans quoi une archive dont le dossier racine ne contiendrait que
    /// des fichiers vides ne serait pas aplatie.
    /// </summary>
    [Fact]
    public async Task LinuxStrategy_RootFolderHoldingOnlyEmptyFiles_IsStillFlattened()
    {
        var archive = TarGzSamples.Create(
            ("vintagestory/", null),
            ("vintagestory/assets/version-1.22.6.txt", TarGzSamples.Empty));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "assets", "version-1.22.6.txt")).ShouldBeTrue();
        fileSystem.Directory.Exists(fileSystem.Path.Combine(TargetDirectory, "vintagestory")).ShouldBeFalse();
    }

    /// <summary>
    /// L'aplatissement est CONDITIONNEL : une archive déjà à plat n'est pas touchée. C'est la forme
    /// que les fixtures modélisaient avant le 2026-08-13, et elle doit continuer de marcher.
    /// </summary>
    [Fact]
    public async Task LinuxStrategy_ArchiveWithoutARootFolder_IsExtractedAsIs()
    {
        var archive = TarGzSamples.Create(
            ("Vintagestory", TarGzSamples.Text("#!/bin/sh")),
            ("assets/game/lang/fr.json", TarGzSamples.Text("{}")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "Vintagestory")).ShouldBe("#!/bin/sh");
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "assets", "game", "lang", "fr.json")).ShouldBe("{}");
    }

    /// <summary>
    /// Le cas pathologique : DEUX dossiers de premier niveau. Rien n'est aplati, sans quoi on
    /// perdrait la moitié de l'archive.
    /// </summary>
    [Fact]
    public async Task LinuxStrategy_ArchiveWithTwoRootFolders_KeepsThemBoth()
    {
        var archive = TarGzSamples.Create(
            ("vintagestory/Vintagestory", TarGzSamples.Text("jeu")),
            ("outils/patch.sh", TarGzSamples.Text("outil")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "vintagestory", "Vintagestory")).ShouldBe("jeu");
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "outils", "patch.sh")).ShouldBe("outil");
    }

    /// <summary>
    /// Une archive réduite à un fichier unique n'a pas de dossier racine à retirer : le nom de
    /// premier niveau est bien unique, mais il ne contient rien.
    /// </summary>
    [Fact]
    public async Task LinuxStrategy_ArchiveOfASingleFile_IsNotFlattened()
    {
        var fileSystem = WithArchive(TarGzSamples.Create(("Vintagestory", TarGzSamples.Text("seul"))));
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "Vintagestory")).ShouldBe("seul");
    }

    /// <summary>
    /// Le piège de l'aplatissement, et c'est exactement la forme de l'archive réelle : le dossier
    /// racine <c>vintagestory</c> contient un fichier <c>Vintagestory</c>, dont le nom ne se
    /// distingue du sien que par une majuscule. Sur un système de fichiers insensible à la casse,
    /// remonter l'enfant l'écraserait contre son propre parent.
    /// </summary>
    [Fact]
    public async Task LinuxStrategy_ChildNamedLikeItsRootFolder_SurvivesTheFlattening()
    {
        var archive = TarGzSamples.Create(
            ("vintagestory/", null),
            ("vintagestory/vintagestory", TarGzSamples.Text("homonyme")),
            ("vintagestory/Lib/protobuf.dll", TarGzSamples.Text("lib")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "vintagestory")).ShouldBe("homonyme");
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "Lib", "protobuf.dll")).ShouldBe("lib");
    }

    [Fact]
    public async Task LinuxStrategy_DirectoryEntries_AreCreatedEvenWhenEmpty()
    {
        var archive = TarGzSamples.Create(("Mods/", null), ("Vintagestory", TarGzSamples.Text("x")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.Directory.Exists(fileSystem.Path.Combine(TargetDirectory, "Mods")).ShouldBeTrue();
    }

    [Fact]
    public async Task LinuxStrategy_AfterExtraction_PutsTheExecutableBitsBackOnEverything()
    {
        var fileSystem = WithArchive(RealLinuxArchive());
        var permissions = new RecordingUnixFilePermissions();
        var strategy = new LinuxGameInstallStrategy(fileSystem, permissions);

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        // Les chemins posés sont ceux de la cible normalisée : sur Windows, « /data/... » devient
        // « C:\data\... », d'où la comparaison à partir de GetFullPath plutôt que du chemin brut.
        // Ils sont aussi ceux d'APRÈS l'aplatissement : le dossier de transit ne doit rien laisser.
        var root = fileSystem.Path.GetFullPath(TargetDirectory);
        permissions.Modes.Values.ShouldAllBe(mode => mode == Mode755);
        permissions.Modes.Keys.ShouldContain(root);
        permissions.Modes.Keys.ShouldContain(fileSystem.Path.Combine(root, "Vintagestory"));
        permissions.Modes.Keys.ShouldContain(fileSystem.Path.Combine(root, "assets"));
        permissions.Modes.Keys.ShouldAllBe(path => !path.Contains(".prospect-flatten-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LinuxStrategy_ArchiveEntryEscapingTheTarget_IsIgnored()
    {
        var archive = TarGzSamples.Create(
            ("../piege.sh", TarGzSamples.Text("rm -rf")),
            ("Vintagestory", TarGzSamples.Text("ok")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.Exists("/data/prospect/versions/piege.sh").ShouldBeFalse();
        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "Vintagestory")).ShouldBeTrue();
    }

    /// <summary>
    /// L'entrée rejetée par le garde-fou de traversée ne compte pas dans la topologie : ce qui
    /// décide de l'aplatissement, c'est ce qui a été ÉCRIT. Une archive au vrai layout à laquelle
    /// on aurait greffé un piège reste donc aplatie, et le piège reste dehors.
    /// </summary>
    [Fact]
    public async Task LinuxStrategy_EscapingEntryAlongsideARootFolder_DoesNotBlockTheFlattening()
    {
        var archive = TarGzSamples.Create(
            ("../piege.sh", TarGzSamples.Text("rm -rf")),
            ("vintagestory/Vintagestory", TarGzSamples.Text("ok")));
        var fileSystem = WithArchive(archive);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        await strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.Exists("/data/prospect/versions/piege.sh").ShouldBeFalse();
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "Vintagestory")).ShouldBe("ok");
    }

    [Fact]
    public async Task LinuxStrategy_ArchiveThatIsNotGzip_FailsWithTheTypedInstallError()
    {
        var fileSystem = WithArchive([1, 2, 3, 4, 5, 6, 7, 8]);
        var strategy = new LinuxGameInstallStrategy(fileSystem, new RecordingUnixFilePermissions());

        var exception = await Should.ThrowAsync<GameInstallFailedException>(
            () => strategy.InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None));

        exception.InnerException.ShouldBeOfType<InvalidDataException>();
    }

    [Fact]
    public void LinuxStrategy_AsksTheCatalogForTheLinuxBuild()
        => new LinuxGameInstallStrategy(new MockFileSystem(), new RecordingUnixFilePermissions())
            .PlatformKeys.ShouldBe([GamePlatforms.Linux]);

    [Fact]
    public void MacStrategy_PrefersAppleSiliconAndFallsBackToIntel()
        => new MacOsGameInstallStrategy(new MockFileSystem(), new RecordingUnixFilePermissions())
            .PlatformKeys.ShouldBe([GamePlatforms.MacArm64, GamePlatforms.MacX64]);

    /// <summary>
    /// Le VRAI layout de l'archive mac, relevé le 2026-08-13 sur les 1.22.6 <c>osx-x64</c> et
    /// <c>osx-arm64</c> : un unique dossier racine <c>Vintage Story.app</c> (avec une espace) qui
    /// porte directement le binaire, pas de <c>Contents/MacOS/</c> pour le jeu. Aplati comme celui
    /// de Linux, il pose le binaire à la racine du dossier de version.
    /// </summary>
    [Fact]
    public async Task MacStrategy_RealTarGz_FlattensTheBundleFolderJustLikeLinux()
    {
        var archive = TarGzSamples.Create(
            ("Vintage Story.app/", null),
            ("Vintage Story.app/Info.plist", TarGzSamples.Text("<plist/>")),
            ("Vintage Story.app/Vintagestory", TarGzSamples.Text("mach-o")),
            ("Vintage Story.app/assets/version-1.22.6.txt", TarGzSamples.Empty),
            ("Vintage Story.app/assets/game/lang/fr.json", TarGzSamples.Text("{}")));
        var fileSystem = WithArchive(archive);
        var permissions = new RecordingUnixFilePermissions();

        await new MacOsGameInstallStrategy(fileSystem, permissions).InstallAsync(ArchivePath, TargetDirectory, cancellationToken: CancellationToken.None);

        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "Vintagestory")).ShouldBe("mach-o");
        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "Info.plist")).ShouldBeTrue();
        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "assets", "game", "lang", "fr.json")).ShouldBeTrue();
        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "assets", "version-1.22.6.txt")).ShouldBeTrue();
        fileSystem.Directory.Exists(fileSystem.Path.Combine(TargetDirectory, "Vintage Story.app")).ShouldBeFalse();
        permissions.Modes.Values.ShouldAllBe(mode => mode == Mode755);
    }

    /// <summary>
    /// La voie normale sous Windows : le contenu de l'installeur est extrait, et l'installeur
    /// lui-même n'est JAMAIS lancé. C'est ce qui fait disparaître sa boîte de dialogue, et c'est
    /// aussi ce qui empêche l'écriture de la clé de désinstallation qui l'armait pour la fois
    /// suivante.
    /// </summary>
    [Fact]
    public async Task WindowsStrategy_ReadableInstaller_ExtractsItWithoutRunningAnything()
    {
        var installer = new SyntheticInnoInstaller()
            .Add(@"{app}\Vintagestory.exe", "MZ"u8.ToArray())
            .Add(@"{app}\assets\version-1.22.6.txt", [])
            .Build();

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(WindowsInstallerPath, new MockFileData(installer));
        var runner = new FakeProcessRunner();
        var reports = new List<GameInstallProgress>();

        await new WindowsGameInstallStrategy(fileSystem, runner, NullAppLog.Instance)
            .InstallAsync(WindowsInstallerPath, TargetDirectory, new CollectingProgress(reports.Add), CancellationToken.None);

        runner.Requests.ShouldBeEmpty();
        fileSystem.File.ReadAllText(fileSystem.Path.Combine(TargetDirectory, "Vintagestory.exe")).ShouldBe("MZ");
        fileSystem.File.Exists(fileSystem.Path.Combine(TargetDirectory, "assets", "version-1.22.6.txt")).ShouldBeTrue();

        // Rien à annoncer : aucune fenêtre ne peut s'ouvrir, et l'avancement est mesuré, pas estimé.
        reports.ShouldNotBeEmpty();
        reports.ShouldAllBe(report => !report.RunsVendorInstaller && !report.IsEstimated);
    }

    /// <summary>
    /// Le repli : un installeur que le lecteur ne reconnaît pas est exécuté comme avant. Le jour où
    /// l'éditeur passera à un Inno Setup dont le format a changé, mieux vaut une installation avec
    /// une notice qu'une installation impossible.
    /// </summary>
    [Fact]
    public async Task WindowsStrategy_UnreadableInstaller_FallsBackToRunningItAndSaysSo()
    {
        var installer = new SyntheticInnoInstaller
        {
            DataVersion = "6.5.0",
        }
            .Add(@"{app}\Vintagestory.exe", "MZ"u8.ToArray())
            .Build();

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(WindowsInstallerPath, new MockFileData(installer));
        var runner = new FakeProcessRunner();
        var reports = new List<GameInstallProgress>();

        await new WindowsGameInstallStrategy(fileSystem, runner, NullAppLog.Instance)
            .InstallAsync(WindowsInstallerPath, TargetDirectory, new CollectingProgress(reports.Add), CancellationToken.None);

        runner.Requests.ShouldHaveSingleItem().FileName.ShouldBe(WindowsInstallerPath);

        // La notice doit être à l'écran AVANT que l'installeur ne démarre, pas après que sa boîte
        // se soit ouverte.
        reports.ShouldNotBeEmpty();
        reports[0].RunsVendorInstaller.ShouldBeTrue();
    }

    /// <summary>
    /// Une extraction qui échoue en cours de route a déjà posé des fichiers. Les laisser reviendrait
    /// à faire écrire l'installeur par-dessus, donc à mélanger deux états du jeu dans un dossier que
    /// la sentinelle de complétude déclarerait ensuite propre.
    /// </summary>
    [Fact]
    public async Task WindowsStrategy_ExtractionThatFailsHalfway_EmptiesTheTargetBeforeFallingBack()
    {
        // La première entrée s'extrait, la seconde porte une empreinte fausse et fait tout échouer.
        var installer = new SyntheticInnoInstaller()
            .Add(@"{app}\assets\game\ok.json", "{}"u8.ToArray())
            .Add(@"{app}\Vintagestory.exe", "MZ"u8.ToArray(), checksumOverride: new byte[32])
            .Build();

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(WindowsInstallerPath, new MockFileData(installer));
        var runner = new FakeProcessRunner();

        await new WindowsGameInstallStrategy(fileSystem, runner, NullAppLog.Instance)
            .InstallAsync(WindowsInstallerPath, TargetDirectory, cancellationToken: CancellationToken.None);

        runner.Requests.ShouldHaveSingleItem();
        fileSystem.Directory.GetFileSystemEntries(TargetDirectory).ShouldBeEmpty();
    }

    [Fact]
    public async Task WindowsStrategy_RunsTheInnoInstallerSilentlyIntoTheVersionFolder()
    {
        var fileSystem = new MockFileSystem();
        var runner = new FakeProcessRunner();
        const string installer = "/data/prospect/cache/downloads/vs_install_win-x64_1.22.6.exe";

        await new WindowsGameInstallStrategy(fileSystem, runner, NullAppLog.Instance).InstallAsync(installer, TargetDirectory, cancellationToken: CancellationToken.None);

        var request = runner.Requests.ShouldHaveSingleItem();
        request.FileName.ShouldBe(installer);
        request.Arguments.ShouldBe(
        [
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/NORESTART",
            "/CURRENTUSER",
            "/NOICONS",
            $"/DIR={TargetDirectory}",
        ]);
    }

    /// <summary>
    /// La forme exacte de <c>/DIR</c> pour un chemin à espaces, épinglée sur la ligne de commande
    /// RÉELLE, pas sur la liste d'arguments : c'est cette ligne-là que l'installeur re-découpe.
    /// L'aller-retour par le découpeur d'Inno est vérifié dans <c>ProcessCommandLineTests</c>.
    /// </summary>
    [Fact]
    public async Task WindowsStrategy_TargetWithSpaces_PassesTheDirectoryAsASingleQuotedToken()
    {
        const string Target = @"C:\Users\Jean Dupont\AppData\Roaming\Prospect\versions\1.22.6";
        var runner = new FakeProcessRunner();

        await new WindowsGameInstallStrategy(new MockFileSystem(), runner, NullAppLog.Instance)
            .InstallAsync("setup.exe", Target, cancellationToken: CancellationToken.None);

        var request = runner.Requests.ShouldHaveSingleItem();
        request.Arguments[^1].ShouldBe($"/DIR={Target}");
        ProcessCommandLine.Render(request).ShouldEndWith($@"""/DIR={Target}""");
    }

    /// <summary>
    /// Le séparateur final est retiré : gardé, il deviendrait un antislash DOUBLÉ une fois échappé
    /// pour Windows, et les deux découpeurs concernés n'en font pas la même lecture.
    /// </summary>
    [Fact]
    public async Task WindowsStrategy_TargetEndingWithASeparator_DropsItBeforeBuildingTheArgument()
    {
        var runner = new FakeProcessRunner();

        await new WindowsGameInstallStrategy(new MockFileSystem(), runner, NullAppLog.Instance)
            .InstallAsync("setup.exe", @"C:\Prospect\versions\1.22.6\", cancellationToken: CancellationToken.None);

        runner.Requests.ShouldHaveSingleItem().Arguments[^1].ShouldBe(@"/DIR=C:\Prospect\versions\1.22.6");
    }

    /// <summary>
    /// La ligne de commande exacte est journalisée AVANT le lancement : sans elle, un rapport de
    /// terrain ne permet pas de trancher entre « les arguments ne sont pas arrivés » et
    /// « l'installeur ne les a pas honorés ».
    /// </summary>
    [Fact]
    public async Task WindowsStrategy_LogsTheExactCommandLineItIsAboutToRun()
    {
        var log = new RecordingAppLog();

        await new WindowsGameInstallStrategy(new MockFileSystem(), new FakeProcessRunner(), log)
            .InstallAsync(@"C:\cache\vs_install_win-x64_1.22.6.exe", TargetDirectory, cancellationToken: CancellationToken.None);

        log.Lines.ShouldContain(line => line.Level == AppLogLevel.Info
            && line.Message.Contains("/VERYSILENT", StringComparison.Ordinal)
            && line.Message.Contains("/SUPPRESSMSGBOXES", StringComparison.Ordinal)
            && line.Message.Contains($"/DIR={TargetDirectory}", StringComparison.Ordinal)
            && line.Message.Contains("vs_install_win-x64_1.22.6.exe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WindowsStrategy_InstallerFails_LogsTheExitCode()
    {
        var log = new RecordingAppLog();
        var runner = new FakeProcessRunner { ExitCode = 2 };

        await Should.ThrowAsync<GameInstallFailedException>(
            () => new WindowsGameInstallStrategy(new MockFileSystem(), runner, log)
                .InstallAsync("setup.exe", TargetDirectory, cancellationToken: CancellationToken.None));

        log.Lines.ShouldContain(line => line.Level == AppLogLevel.Error && line.Message.Contains('2', StringComparison.Ordinal));
    }

    /// <summary>
    /// Chaque stratégie déclare ce que le LANCEMENT ira chercher (voir
    /// <c>Launching.IGameLaunchStrategy.ResolveExecutablePath</c>) : c'est ce qui rend la
    /// vérification post-installation vraie sur les trois OS, pas seulement sous Windows.
    /// </summary>
    [Fact]
    public void EveryStrategy_DeclaresTheExecutableItsOwnLaunchPathLooksFor()
    {
        var fileSystem = new MockFileSystem();
        var permissions = new RecordingUnixFilePermissions();

        new LinuxGameInstallStrategy(fileSystem, permissions).ExpectedExecutables
            .Select(location => location.ToString())
            .ShouldBe(["Vintagestory", "Vintagestory.exe"]);

        new WindowsGameInstallStrategy(fileSystem, new FakeProcessRunner(), NullAppLog.Instance).ExpectedExecutables
            .Select(location => location.ToString())
            .ShouldBe(["Vintagestory.exe"]);

        // Layout mac relevé le 2026-08-13 : le binaire nu, une fois le dossier racine aplati.
        new MacOsGameInstallStrategy(fileSystem, permissions).ExpectedExecutables
            .Select(location => location.ToString())
            .ShouldBe(["Vintagestory", "Vintage Story.app/Vintagestory"]);
    }

    /// <summary>Le chemin est assemblé par le système de fichiers, donc jamais de séparateur en dur.</summary>
    [Fact]
    public void ExecutableLocation_ResolvesThroughTheFileSystem()
    {
        var fileSystem = new MockFileSystem();
        var location = GameExecutableLocation.Of("Vintagestory.app", "Contents", "MacOS", "Vintagestory");

        location.ResolveIn(fileSystem, TargetDirectory)
            .ShouldBe(fileSystem.Path.Combine(TargetDirectory, "Vintagestory.app", "Contents", "MacOS", "Vintagestory"));
    }

    [Fact]
    public void ExecutableLocation_NullArguments_Throw()
    {
        Should.Throw<ArgumentNullException>(() => GameExecutableLocation.Of(null!));
        Should.Throw<ArgumentNullException>(() => GameExecutableLocation.Of("x").ResolveIn(null!, TargetDirectory));
        Should.Throw<ArgumentException>(() => GameExecutableLocation.Of("x").ResolveIn(new MockFileSystem(), string.Empty));
    }

    [Fact]
    public void WindowsStrategy_BuildDirectoryArgument_RejectsAnEmptyTarget()
        => Should.Throw<ArgumentException>(() => WindowsGameInstallStrategy.BuildDirectoryArgument(string.Empty));

    [Fact]
    public async Task WindowsStrategy_InstallerFails_SurfacesTheExitCodeAndItsOutput()
    {
        var runner = new FakeProcessRunner { ExitCode = 5, StandardError = "annulé par l'utilisateur" };

        var exception = await Should.ThrowAsync<GameInstallFailedException>(
            () => new WindowsGameInstallStrategy(new MockFileSystem(), runner, NullAppLog.Instance).InstallAsync("setup.exe", TargetDirectory, cancellationToken: CancellationToken.None));

        exception.Message.ShouldContain("5");
        exception.Message.ShouldContain("annulé par l'utilisateur");
    }

    [Fact]
    public void WindowsStrategy_AsksTheCatalogForTheWindowsInstaller()
        => new WindowsGameInstallStrategy(new MockFileSystem(), new FakeProcessRunner(), NullAppLog.Instance)
            .PlatformKeys.ShouldBe([GamePlatforms.Windows]);

    [Fact]
    public void WindowsStrategy_NullArguments_ThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new WindowsGameInstallStrategy(null!, new FakeProcessRunner(), NullAppLog.Instance));
        Should.Throw<ArgumentNullException>(() => new WindowsGameInstallStrategy(new MockFileSystem(), null!, NullAppLog.Instance));
    }

    [Fact]
    public void Selector_MapsEachOperatingSystemToItsOwnStrategy()
    {
        var fileSystem = new MockFileSystem();
        var permissions = new RecordingUnixFilePermissions();
        var linux = new LinuxGameInstallStrategy(fileSystem, permissions);
        var windows = new WindowsGameInstallStrategy(fileSystem, new FakeProcessRunner(), NullAppLog.Instance);
        var mac = new MacOsGameInstallStrategy(fileSystem, permissions);
        var selector = new GameInstallStrategySelector(linux, windows, mac);

        selector.Resolve(AppOperatingSystem.Linux).ShouldBeSameAs(linux);
        selector.Resolve(AppOperatingSystem.Windows).ShouldBeSameAs(windows);
        selector.Resolve(AppOperatingSystem.MacOs).ShouldBeSameAs(mac);
    }

    [Fact]
    public void Selector_UnknownOperatingSystem_Throws()
    {
        var fileSystem = new MockFileSystem();
        var permissions = new RecordingUnixFilePermissions();
        var selector = new GameInstallStrategySelector(
            new LinuxGameInstallStrategy(fileSystem, permissions),
            new WindowsGameInstallStrategy(fileSystem, new FakeProcessRunner(), NullAppLog.Instance),
            new MacOsGameInstallStrategy(fileSystem, permissions));

        Should.Throw<ArgumentOutOfRangeException>(() => selector.Resolve((AppOperatingSystem)99));
    }

    [Fact]
    public void Selector_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var permissions = new RecordingUnixFilePermissions();
        var linux = new LinuxGameInstallStrategy(fileSystem, permissions);
        var windows = new WindowsGameInstallStrategy(fileSystem, new FakeProcessRunner(), NullAppLog.Instance);
        var mac = new MacOsGameInstallStrategy(fileSystem, permissions);

        Should.Throw<ArgumentNullException>(() => new GameInstallStrategySelector(null!, windows, mac));
        Should.Throw<ArgumentNullException>(() => new GameInstallStrategySelector(linux, null!, mac));
        Should.Throw<ArgumentNullException>(() => new GameInstallStrategySelector(linux, windows, null!));
    }

    private sealed class CollectingProgress(Action<GameInstallProgress> report) : IProgress<GameInstallProgress>
    {
        public void Report(GameInstallProgress value) => report(value);
    }
}