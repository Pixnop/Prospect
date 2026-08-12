#if PROSPECT_CONFORMANCE_ENGINE
using System.IO.Abstractions;

using Atlas.XUnit;

using Prospect.Core.ModDb;
using Prospect.GameConformance.Tests.Support;

namespace Prospect.GameConformance.Tests;

/// <summary>
/// Construit le mod minimal partagé par <see cref="ModEnabledConformanceTests"/> et
/// <see cref="ModDisabledConformanceTests"/>, et calcule le nom du fichier désactivé via
/// <see cref="IModStateConvention"/> — jamais en codant <c>".disabled"</c> en dur ici, pour que ce
/// test suive automatiquement la convention si elle change.
/// </summary>
internal static class DisabledConventionFixture
{
    /// <summary>Identifiant du mod, choisi valide pour le vrai moteur (lettre minuscule initiale,
    /// uniquement lettres/chiffres minuscules — <c>Vintagestory.API.Common.ModInfo.IsValidModID</c>).</summary>
    public const string ModId = "prospectconformancemarker";

    /// <summary>Nom de fichier à l'état activé : c'est la partie NON hypothétique de la convention
    /// (n'importe quel mod .zip nommé ainsi), donc la seule qu'on peut se permettre de fixer ici.</summary>
    public const string EnabledFileName = ModId + ".zip";

    private static readonly string ModInfoJson = $$"""
        {
          "type": "Content",
          "modid": "{{ModId}}",
          "name": "Prospect Conformance Marker",
          "version": "1.0.0",
          "description": "Mod minimal généré par la suite de conformité Prospect pour valider la convention d'activation (voir IModStateConvention).",
          "authors": ["Prospect Conformance Suite"],
          "side": "Universal"
        }
        """;

    private static string TestAssemblyDirectory
        => Path.GetDirectoryName(typeof(DisabledConventionFixture).Assembly.Location)!;

    /// <summary>Nom de fichier à l'état désactivé, calculé par la même implémentation
    /// d'<see cref="IModStateConvention"/> que <c>Prospect.Desktop.CompositionRoot</c> enregistre
    /// en production (<c>services.AddSingleton&lt;IModStateConvention, DisabledSuffixModStateConvention&gt;()</c>).
    /// Si cette implémentation change, cette valeur — et donc ce que le test seede réellement dans
    /// <c>Mods/</c> — change avec elle, sans qu'une seule ligne de ce fichier n'ait à bouger.</summary>
    public static string DisabledFileName
    {
        get
        {
            var fileSystem = new FileSystem();
            var convention = new DisabledSuffixModStateConvention();

            // Chemin synthétique : seul le NOM en sort (ResolveTargetPath ne lit jamais le disque),
            // aucun fichier n'a besoin d'exister à cet emplacement pour ce calcul.
            var syntheticEnabledPath = fileSystem.Path.Combine("Mods", EnabledFileName);
            var disabledPath = convention.ResolveTargetPath(fileSystem, "Mods", syntheticEnabledPath, enabled: false);

            return fileSystem.Path.GetFileName(disabledPath);
        }
    }

    public static void WriteEnabledFixture()
    {
        var directory = Path.Combine(TestAssemblyDirectory, "Fixtures", "DisabledConvention", "Enabled");
        ModFixtureZip.WriteSingleFixture(Path.Combine(directory, EnabledFileName), ModInfoJson);
    }

    public static void WriteDisabledFixture()
    {
        var directory = Path.Combine(TestAssemblyDirectory, "Fixtures", "DisabledConvention", "Disabled");
        ModFixtureZip.WriteSingleFixture(Path.Combine(directory, DisabledFileName), ModInfoJson);
    }
}

/// <summary>
/// PREUVE DE L'HYPOTHÈSE (moitié « activé ») : un mod valide, nommé selon la convention normale
/// (<c>&lt;nom&gt;.zip</c>), est bien chargé par le vrai <c>ModLoader</c> du moteur quand il est
/// déposé dans <c>Mods/</c> du dataPath — sans qu'aucun <c>--addModPath</c> ne soit passé, exactement
/// comme <c>Prospect.Core.Launching.GameLauncher.LaunchAsync</c> lance le jeu en production (ses
/// arguments se limitent à <c>--dataPath</c> plus les extras de l'instance, jamais un chemin de mod
/// explicite : voir GameLauncher.cs). Sert de témoin : si CE test échoue, le problème n'est pas la
/// convention de désactivation mais la convention de chargement elle-même.
/// </summary>
[Trait("Category", "Conformance")]
[AtlasDataFiles("Fixtures/DisabledConvention/Enabled", TargetPath = "Mods")]
public sealed class ModEnabledConformanceTests : AtlasScenarioBase
{
    static ModEnabledConformanceTests() => DisabledConventionFixture.WriteEnabledFixture();

    [ConformanceFact]
    public async Task EnabledMod_Should_AppearInModLoader_When_FileNameIsPlainZip()
    {
        await World.Ticks(5);

        var loaded = World.Api.ModLoader.Mods.FirstOrDefault(
            mod => string.Equals(mod.Info?.ModID, DisabledConventionFixture.ModId, StringComparison.Ordinal));

        Assert.True(
            loaded is not null,
            $"Le mod « {DisabledConventionFixture.EnabledFileName} » n'a pas été chargé par le ModLoader réel " +
            $"alors qu'il est présent, valide et nommé selon la convention normale. Le témoin lui-même échoue : " +
            "le problème n'est pas la désactivation mais le chargement de base (chemin, casse du dossier Mods, " +
            "ou modinfo.json rejeté — voir server-main.log dans le scratch Atlas conservé pour ce test en échec).");
    }
}

/// <summary>
/// PREUVE DE L'HYPOTHÈSE (moitié « désactivé », LA PLUS IMPORTANTE — l'hypothèse laissée ouverte
/// depuis la PR 7, voir la remarque d'<see cref="IModStateConvention"/>) : le MÊME mod, renommé
/// selon la convention de désactivation calculée par <see cref="IModStateConvention"/> — jamais
/// codée en dur ici — N'est PAS chargé par le vrai moteur.
/// </summary>
[Trait("Category", "Conformance")]
[AtlasDataFiles("Fixtures/DisabledConvention/Disabled", TargetPath = "Mods")]
public sealed class ModDisabledConformanceTests : AtlasScenarioBase
{
    static ModDisabledConformanceTests() => DisabledConventionFixture.WriteDisabledFixture();

    [ConformanceFact]
    public async Task DisabledMod_Should_BeAbsentFromModLoader_When_FileNameFollowsDisabledConvention()
    {
        await World.Ticks(5);

        var stillLoaded = World.Api.ModLoader.Mods.Any(
            mod => string.Equals(mod.Info?.ModID, DisabledConventionFixture.ModId, StringComparison.Ordinal));

        Assert.False(
            stillLoaded,
            "L'HYPOTHÈSE DE LA CONVENTION « .disabled » EST INVALIDÉE : le moteur réel a quand même chargé " +
            $"« {DisabledConventionFixture.DisabledFileName} ». DisabledSuffixModStateConvention repose sur l'idée que " +
            "Vintage Story ignore tout fichier de Mods/ dont le nom ne se termine pas par DisabledSuffixModStateConvention." +
            "ArchiveExtension (\".zip\") ; ce n'est manifestement pas le cas. Il faut basculer IModStateConvention sur une " +
            "implémentation qui déplace le fichier vers un dossier séparé, HORS du dataPath, plutôt que de le renommer " +
            "sur place (voir la remarque de IModStateConvention.cs pour le plan de repli déjà prévu).");
    }
}
#endif