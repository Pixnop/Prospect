using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Launching;

using Shouldly;

namespace Prospect.Core.Tests.Launching;

/// <summary>
/// L'option <c>mesa_glthread</c> : parité de FONCTION avec VS Launcher, qui l'offre par
/// installation, mais posée par la seule stratégie de lancement Linux et sous le nom que Mesa lit
/// réellement.
/// </summary>
public sealed class MesaGlThreadTests
{
    private const string InstallDirectory = "/data/prospect/versions/1.22.6";

    private static InstanceLaunchSettings Settings(bool mesaGlThread, params (string Key, string Value)[] env) => new()
    {
        MesaGlThread = mesaGlThread,
        Env = env.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
    };

    [Fact]
    public void LinuxStrategy_OptionEnabled_AddsTheVariableMesaActuallyReads()
    {
        var environment = new LinuxGameLaunchStrategy(new MockFileSystem()).BuildEnvironment(Settings(mesaGlThread: true));

        environment.ShouldContainKeyAndValue("mesa_glthread", "true");

        // Surtout PAS la variante en majuscules : c'est celle que VS Launcher écrivait, et Mesa ne
        // la lit pas. Une option de driconf se surcharge sous le nom exact de l'option.
        environment.ShouldNotContainKey("MESA_GLTHREAD");
    }

    [Fact]
    public void LinuxStrategy_OptionDisabled_AddsNothingAtAll()
        => new LinuxGameLaunchStrategy(new MockFileSystem())
            .BuildEnvironment(Settings(mesaGlThread: false))
            .ShouldBeEmpty();

    [Fact]
    public void LinuxStrategy_OptionEnabled_KeepsTheInstanceOwnVariables()
    {
        var environment = new LinuxGameLaunchStrategy(new MockFileSystem())
            .BuildEnvironment(Settings(mesaGlThread: true, ("DXVK_HUD", "fps")));

        environment.Count.ShouldBe(2);
        environment.ShouldContainKeyAndValue("DXVK_HUD", "fps");
    }

    /// <summary>Ce que l'utilisateur a écrit lui-même l'emporte : c'est sa machine, la case n'est qu'un raccourci.</summary>
    [Fact]
    public void LinuxStrategy_UserWroteTheVariableByHand_HisValueWins()
    {
        var environment = new LinuxGameLaunchStrategy(new MockFileSystem())
            .BuildEnvironment(Settings(mesaGlThread: true, ("mesa_glthread", "false")));

        environment.ShouldContainKeyAndValue("mesa_glthread", "false");
    }

    /// <summary>Les autres systèmes ignorent l'option : elle n'a de sens que pour les pilotes Mesa.</summary>
    [Fact]
    public void OtherStrategies_IgnoreTheOptionEntirely()
    {
        var settings = Settings(mesaGlThread: true, ("DXVK_HUD", "fps"));

        var windows = new WindowsGameLaunchStrategy(new MockFileSystem()).BuildEnvironment(settings);
        windows.ShouldContainKeyAndValue("DXVK_HUD", "fps");
        windows.ShouldNotContainKey("mesa_glthread");
        windows.ShouldNotContainKey("MESA_GLTHREAD");

        var mac = new MacGameLaunchStrategy().BuildEnvironment(settings);
        mac.ShouldNotContainKey("mesa_glthread");
    }

    [Fact]
    public void EveryStrategy_RejectsNullSettings()
    {
        Should.Throw<ArgumentNullException>(() => new LinuxGameLaunchStrategy(new MockFileSystem()).BuildEnvironment(null!));
        Should.Throw<ArgumentNullException>(() => new WindowsGameLaunchStrategy(new MockFileSystem()).BuildEnvironment(null!));
        Should.Throw<ArgumentNullException>(() => new MacGameLaunchStrategy().BuildEnvironment(null!));
    }

    [Fact]
    public void LinuxStrategy_StillResolvesTheNativeBinary()
        => new LinuxGameLaunchStrategy(new MockFileSystem()).ResolveExecutablePath(InstallDirectory)
            .ShouldEndWith("Vintagestory");

    // ── Migration v2 → v3 ────────────────────────────────────────────────────────────

    private static JsonObject Document(string launchJson)
        => JsonNode.Parse($$"""
        {
          "schemaVersion": 2,
          "id": "0c9c1f57-8b2e-4f2a-9c41-3d8a12f7b6e0",
          "name": "Homestead",
          "gameVersion": "1.22.6",
          "launch": {{launchJson}}
        }
        """)!.AsObject();

    [Fact]
    public void Migration_LegacyVariable_BecomesTheTypedOptionAndLeavesEnv()
    {
        var document = new InstanceMetadataV2ToV3Migration()
            .Migrate(Document("""{ "extraArgs": [], "env": { "MESA_GLTHREAD": "true", "DXVK_HUD": "fps" } }"""));

        var launch = document["launch"]!.AsObject();
        launch["mesaGlThread"]!.GetValue<bool>().ShouldBeTrue();
        launch["env"]!.AsObject().ContainsKey("MESA_GLTHREAD").ShouldBeFalse();
        launch["env"]!.AsObject()["DXVK_HUD"]!.GetValue<string>().ShouldBe("fps");
    }

    /// <summary>La casse n'est pas supposée : un fichier édité à la main peut écrire n'importe quelle variante.</summary>
    [Theory]
    [InlineData("MESA_GLTHREAD")]
    [InlineData("mesa_glthread")]
    [InlineData("Mesa_GlThread")]
    public void Migration_AnyCasingOfTheLegacyKey_IsRecognised(string key)
    {
        var document = new InstanceMetadataV2ToV3Migration()
            .Migrate(Document($$"""{ "extraArgs": [], "env": { "{{key}}": "true" } }"""));

        document["launch"]!["mesaGlThread"]!.GetValue<bool>().ShouldBeTrue();
        document["launch"]!["env"]!.AsObject().Count.ShouldBe(0);
    }

    [Fact]
    public void Migration_NoLegacyVariable_LeavesTheOptionOff()
    {
        var document = new InstanceMetadataV2ToV3Migration()
            .Migrate(Document("""{ "extraArgs": [], "env": { "DXVK_HUD": "fps" } }"""));

        document["launch"]!["mesaGlThread"]!.GetValue<bool>().ShouldBeFalse();
    }

    /// <summary>Une entrée explicitement à « false » signifiait déjà désactivé.</summary>
    [Fact]
    public void Migration_LegacyVariableSetToFalse_StaysOff()
    {
        var document = new InstanceMetadataV2ToV3Migration()
            .Migrate(Document("""{ "extraArgs": [], "env": { "MESA_GLTHREAD": "false" } }"""));

        document["launch"]!["mesaGlThread"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Migration_DocumentWithoutALaunchBlock_IsLeftAlone()
    {
        var document = JsonNode.Parse("""{ "schemaVersion": 2, "name": "Homestead" }""")!.AsObject();

        new InstanceMetadataV2ToV3Migration().Migrate(document)["launch"].ShouldBeNull();
    }

    [Fact]
    public void Migration_AnnouncesTheSchemaItUpgradesFrom_AndRejectsNull()
    {
        new InstanceMetadataV2ToV3Migration().FromSchemaVersion.ShouldBe(2);
        Should.Throw<ArgumentNullException>(() => new InstanceMetadataV2ToV3Migration().Migrate(null!));
    }
}