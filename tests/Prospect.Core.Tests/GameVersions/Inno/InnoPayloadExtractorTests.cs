using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;

using Prospect.Core.GameVersions;
using Prospect.Core.GameVersions.Inno;

using Shouldly;

namespace Prospect.Tests.GameVersions.Inno;

/// <summary>
/// L'extraction du jeu depuis un installeur Inno Setup, sans l'exécuter.
/// </summary>
/// <remarks>
/// Les fixtures sont des installeurs synthétiques au format binaire réel (voir
/// <see cref="SyntheticInnoInstaller"/>). Elles prouvent que le lecteur est cohérent avec une
/// écriture du format ; que cette écriture soit bien celle de l'outil officiel est prouvé ailleurs,
/// par l'étage live qui extrait l'installeur publié.
/// </remarks>
public sealed class InnoPayloadExtractorTests
{
    private const string InstallerPath = @"C:\cache\vs_install.exe";
    private const string TargetPath = @"C:\versions\1.22.6";

    [Fact]
    public async Task Extraction_WritesEveryFileTheScriptSendsToTheApplicationFolder()
    {
        var installer = new SyntheticInnoInstaller()
            .Add(@"{app}\Vintagestory.exe", Encoding.UTF8.GetBytes("client"))
            .Add(@"{app}\Lib\0Harmony.dll", Encoding.UTF8.GetBytes("harmony"))
            .Add(@"{app}\assets\version-1.22.6.txt", [])
            .Build();

        var fileSystem = FileSystemWith(installer);

        await Extract(fileSystem);

        fileSystem.File.ReadAllText(Path(@"Vintagestory.exe")).ShouldBe("client");
        fileSystem.File.ReadAllText(Path(@"Lib\0Harmony.dll")).ShouldBe("harmony");
        fileSystem.File.Exists(Path(@"assets\version-1.22.6.txt")).ShouldBeTrue();
    }

    /// <summary>
    /// Un fichier vide reste un fichier. Le jeu vérifie la PRÉSENCE de
    /// <c>assets/version-&lt;version&gt;.txt</c> au démarrage et se plaint d'une installation sale
    /// s'il manque, exactement comme pour l'extraction des archives tar.
    /// </summary>
    [Fact]
    public async Task Extraction_CreatesEmptyFilesRatherThanSkippingThem()
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .Add(@"{app}\Vintagestory.exe", [1])
            .Add(@"{app}\assets\version-1.22.6.txt", [])
            .Build());

        await Extract(fileSystem);

        fileSystem.File.ReadAllBytes(Path(@"assets\version-1.22.6.txt")).ShouldBeEmpty();
    }

    /// <summary>
    /// Un fichier vide partage son décalage avec celui qui le suit, puisqu'il n'occupe rien dans le
    /// flux. L'ordre de lecture doit le placer DEVANT son voisin, sans quoi la lecture semble
    /// revenir en arrière et l'installation entière échoue. L'installeur officiel en contient un.
    /// </summary>
    [Fact]
    public async Task Extraction_HandlesAnEmptyFileThatSharesItsOffsetWithTheNextOne()
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .Add(@"{app}\before.bin", [1, 2, 3])
            .Add(@"{app}\assets\version-1.22.6.txt", [])
            .Add(@"{app}\after.bin", [4, 5, 6])
            .Build());

        await Extract(fileSystem);

        fileSystem.File.ReadAllBytes(Path("before.bin")).ShouldBe([1, 2, 3]);
        fileSystem.File.ReadAllBytes(Path(@"assets\version-1.22.6.txt")).ShouldBeEmpty();
        fileSystem.File.ReadAllBytes(Path("after.bin")).ShouldBe([4, 5, 6]);
    }

    /// <summary>
    /// Rien n'est écrit hors du dossier de la version : ni les polices que le script pose dans le
    /// dossier système, ni ses fichiers temporaires. C'est le cœur du choix documenté dans
    /// docs/architecture.md, et la raison pour laquelle la boîte de dialogue disparaît.
    /// </summary>
    [Fact]
    public async Task Extraction_IgnoresEverythingDestinedOutsideTheApplicationFolder()
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .Add(@"{tmp}\netcorecheck_x64.exe", Encoding.UTF8.GetBytes("probe"))
            .Add(@"{autofonts}\Lora-Regular.ttf", Encoding.UTF8.GetBytes("font"))
            .Add(@"{app}\Vintagestory.exe", Encoding.UTF8.GetBytes("client"))
            .Build());

        await Extract(fileSystem);

        fileSystem.AllFiles.ShouldNotContain(f => f.Contains("netcorecheck", StringComparison.Ordinal));
        fileSystem.AllFiles.ShouldNotContain(f => f.Contains("Lora-Regular", StringComparison.Ordinal));
        fileSystem.File.ReadAllText(Path("Vintagestory.exe")).ShouldBe("client");
    }

    /// <summary>
    /// Une entrée sans données (le désinstalleur, que Setup fabrique lui-même) ne doit ni être
    /// écrite ni décaler la lecture de la table des emplacements.
    /// </summary>
    [Fact]
    public async Task Extraction_SkipsFileEntriesThatCarryNoData()
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .AddWithoutData(@"{app}\unins000.exe")
            .Add(@"{app}\Vintagestory.exe", Encoding.UTF8.GetBytes("client"))
            .Build());

        await Extract(fileSystem);

        fileSystem.File.Exists(Path("unins000.exe")).ShouldBeFalse();
        fileSystem.File.ReadAllText(Path("Vintagestory.exe")).ShouldBe("client");
    }

    /// <summary>
    /// Le filtre d'exécutable est vérifié sur des données qui contiennent vraiment des <c>0xE8</c>
    /// et des <c>0xE9</c>, à des positions choisies pour être piégeuses.
    /// </summary>
    [Fact]
    public async Task Extraction_UndoesTheCallInstructionFilterOnExecutables()
    {
        var executable = BuildExecutableLikeContent();

        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .Add(@"{app}\Vintagestory.exe", executable, callInstructionOptimized: true)
            .Build());

        await Extract(fileSystem);

        fileSystem.File.ReadAllBytes(Path("Vintagestory.exe")).ShouldBe(executable);
    }

    /// <summary>
    /// La preuve que le filtre n'est pas l'identité : sans lui, le contenu stocké diffère de
    /// l'original. Un test qui passerait avec un filtre vide ne prouverait rien.
    /// </summary>
    [Fact]
    public void TheFilter_ActuallyChangesTheBytesItIsAppliedTo()
    {
        var original = BuildExecutableLikeContent();

        SyntheticInnoInstaller.Filter(original).ShouldNotBe(original);
    }

    /// <summary>
    /// Une empreinte qui ne tombe pas juste arrête tout. C'est ce qui rend acceptable une lecture de
    /// format écrite à la main : une grille de lecture fausse ne produit pas un jeu discrètement
    /// abîmé, elle échoue franchement et l'installation repart sur l'installeur officiel.
    /// </summary>
    [Fact]
    public async Task Extraction_RefusesAFileWhoseChecksumDoesNotMatch()
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .Add(@"{app}\Vintagestory.exe", Encoding.UTF8.GetBytes("client"), checksumOverride: SHA256.HashData("autre chose"u8))
            .Build());

        var exception = await Should.ThrowAsync<InnoFormatException>(() => Extract(fileSystem));

        exception.Message.ShouldContain("SHA-256");
    }

    [Theory]
    [InlineData("6.5.0")]
    [InlineData("6.3.0")]
    [InlineData("5.6.1")]
    [InlineData("7.0.0")]
    public async Task Extraction_RefusesAFormatVersionItDoesNotClaimToRead(string dataVersion)
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller
        {
            DataVersion = dataVersion,
        }
            .Add(@"{app}\Vintagestory.exe", Encoding.UTF8.GetBytes("client"))
            .Build());

        var exception = await Should.ThrowAsync<InnoFormatException>(() => Extract(fileSystem));

        exception.Message.ShouldContain(dataVersion);
    }

    /// <summary>
    /// Les deux dispositions de la famille 6.4 se lisent : celle d'avant 6.4.2 (une chaîne d'en-tête
    /// de moins) et celle d'avant 6.4.3 (emplacement de fichier long, avec octet de signature).
    /// </summary>
    [Theory]
    [InlineData("6.4.0.1")]
    [InlineData("6.4.2")]
    [InlineData("6.4.3")]
    public async Task Extraction_ReadsEveryLayoutOfTheSupportedFamily(string dataVersion)
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller
        {
            DataVersion = dataVersion,
        }
            .Add(@"{app}\Vintagestory.exe", Encoding.UTF8.GetBytes("client"))
            .Build());

        await Extract(fileSystem);

        fileSystem.File.ReadAllText(Path("Vintagestory.exe")).ShouldBe("client");
    }

    /// <summary>
    /// La forme réelle : en-têtes en LZMA1 et données en LZMA2, comme les publie Vintage Story.
    /// </summary>
    [Fact]
    public async Task Extraction_ReadsCompressedHeadersAndACompressedPayload()
    {
        var content = Encoding.UTF8.GetBytes(new string('x', 200_000));

        var fileSystem = FileSystemWith(new SyntheticInnoInstaller
        {
            CompressHeaders = true,
            CompressPayload = true,
        }
            .Add(@"{app}\Vintagestory.exe", content)
            .Add(@"{app}\Lib\0Harmony.dll", Encoding.UTF8.GetBytes("harmony"))
            .Build());

        await Extract(fileSystem);

        fileSystem.File.ReadAllBytes(Path("Vintagestory.exe")).ShouldBe(content);
        fileSystem.File.ReadAllText(Path(@"Lib\0Harmony.dll")).ShouldBe("harmony");
    }

    /// <summary>
    /// Une destination qui remonterait hors du dossier de version n'est pas écrite. L'installeur
    /// est un fichier distant : il ne décide pas où Prospect écrit.
    /// </summary>
    [Fact]
    public async Task Extraction_RefusesADestinationThatWouldEscapeTheVersionFolder()
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .Add(@"{app}\..\..\Windows\System32\evil.dll", Encoding.UTF8.GetBytes("nope"))
            .Build());

        await Should.ThrowAsync<InnoFormatException>(() => Extract(fileSystem));

        fileSystem.AllFiles.ShouldNotContain(f => f.Contains("evil.dll", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Extraction_RefusesAFileThatIsNotAnInnoInstallerAtAll()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(InstallerPath, new MockFileData(new byte[4096]));

        await Should.ThrowAsync<InnoFormatException>(() => Extract(fileSystem));
    }

    /// <summary>
    /// L'avancement est MESURÉ et non estimé : toutes les tailles sont connues avant de commencer,
    /// donc le dénominateur est exact et la barre n'a pas à s'excuser d'un tilde.
    /// </summary>
    [Fact]
    public async Task Extraction_ReportsMeasuredProgressThatEndsFull()
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .Add(@"{app}\a.bin", new byte[50_000])
            .Add(@"{app}\b.bin", new byte[50_000])
            .Build());

        var reports = new List<GameInstallProgress>();

        await Extract(fileSystem, new Progress(reports.Add));

        reports.ShouldNotBeEmpty();
        reports.ShouldAllBe(r => r.Phase == GameInstallPhase.Installing);
        reports.ShouldAllBe(r => !r.IsEstimated);
        reports[^1].Ratio.ShouldBe(1d);
    }

    [Fact]
    public async Task Extraction_StopsWhenCancelled()
    {
        var fileSystem = FileSystemWith(new SyntheticInnoInstaller()
            .Add(@"{app}\Vintagestory.exe", Encoding.UTF8.GetBytes("client"))
            .Build());

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => new InnoPayloadExtractor(fileSystem).ExtractAsync(InstallerPath, TargetPath, null, cancellation.Token));
    }

    /// <summary>
    /// Contenu qui ressemble à du code x86 : des <c>0xE8</c> et <c>0xE9</c> semés à des endroits
    /// choisis, dont un juste avant une frontière de bloc de 64 Kio (que le compilateur laisse
    /// intact) et un dont les octets d'adresse contiennent eux-mêmes un <c>0xE8</c>.
    /// </summary>
    private static byte[] BuildExecutableLikeContent()
    {
        var data = new byte[0x10000 + 64];
        var random = new Random(1789);
        random.NextBytes(data);

        void Call(int at, byte opcode, byte b0, byte b1, byte b2, byte high)
        {
            data[at] = opcode;
            data[at + 1] = b0;
            data[at + 2] = b1;
            data[at + 3] = b2;
            data[at + 4] = high;
        }

        Call(0x40, 0xE8, 0x10, 0x20, 0x00, 0x00);
        Call(0x80, 0xE9, 0xF0, 0xFF, 0xFF, 0xFF);
        Call(0x100, 0xE8, 0xE8, 0x00, 0x80, 0x00);   // octet d'adresse qui ressemble à un CALL
        Call(0xFFFD, 0xE8, 0x01, 0x02, 0x03, 0x00);  // à cheval sur la frontière de bloc
        Call(0x10004, 0xE8, 0x11, 0x22, 0x00, 0x00);

        return data;
    }

    private static MockFileSystem FileSystemWith(byte[] installer)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(InstallerPath, new MockFileData(installer));

        return fileSystem;
    }

    private static Task Extract(MockFileSystem fileSystem, IProgress<GameInstallProgress>? progress = null)
        => new InnoPayloadExtractor(fileSystem).ExtractAsync(InstallerPath, TargetPath, progress, CancellationToken.None);

    // Les destinations d'un installeur Windows portent des antislashs ; l'extracteur les traduit
    // vers le séparateur de la plateforme, et la suite tourne aussi bien sous Linux.
    private static string Path(string relative)
        => System.IO.Path.Combine(TargetPath, relative.Replace('\\', System.IO.Path.DirectorySeparatorChar));

    private sealed class Progress(Action<GameInstallProgress> report) : IProgress<GameInstallProgress>
    {
        public void Report(GameInstallProgress value) => report(value);
    }
}
