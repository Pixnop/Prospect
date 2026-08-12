using System.IO.Abstractions.TestingHelpers;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;

using Shouldly;

namespace Prospect.Desktop.Tests.Services;

/// <summary>
/// <see cref="ThemeService"/> : application au démarrage selon le réglage persisté, bascule à
/// chaud sur changement de réglage, résolution de <see cref="ThemePreference.System"/> via
/// <c>Application.PlatformSettings</c>, et surtout l'invariant documenté sur la classe — la
/// construire ne doit JAMAIS, à elle seule, changer <see cref="Application.RequestedThemeVariant"/>
/// (voir sa remarque sur <c>ResponsiveScenario.ShowWindow</c>).
/// </summary>
public sealed class ThemeServiceTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    private static SettingsService CreateSettings()
    {
        var fileSystem = new MockFileSystem();

        return new SettingsService(fileSystem, Paths, new JsonFileStore(fileSystem), new SettingsMigrationPipeline([]));
    }

    [AvaloniaFact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var settings = CreateSettings();

        Should.Throw<ArgumentNullException>(() => new ThemeService(null!, Application.Current!));
        Should.Throw<ArgumentNullException>(() => new ThemeService(settings, null!));
    }

    [AvaloniaFact]
    public void Constructor_NeverMutatesRequestedThemeVariantByItself()
    {
        // L'invariant central de la classe (voir sa docstring) : construire ThemeService — comme le
        // fait tout test qui résout ShellViewModel/SettingsViewModel via le conteneur DI — ne doit
        // jamais, à lui seul, écraser une variante déjà posée explicitement (ResponsiveScenario.ShowWindow
        // la pose AVANT de résoudre MainWindow).
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var settings = CreateSettings();

        _ = new ThemeService(settings, Application.Current!);

        Application.Current!.RequestedThemeVariant.ShouldBe(ThemeVariant.Light);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
    }

    [AvaloniaFact]
    public void ApplyStartupTheme_Dark_SetsRequestedThemeVariantToDark()
    {
        var settings = CreateSettings();
        var themeService = new ThemeService(settings, Application.Current!);
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        themeService.ApplyStartupTheme();

        Application.Current!.RequestedThemeVariant.ShouldBe(ThemeVariant.Dark);
    }

    [AvaloniaFact]
    public async Task ApplyStartupTheme_Light_SetsRequestedThemeVariantToLight()
    {
        var settings = CreateSettings();
        await settings.UpdateAsync(current => current with { Theme = ThemePreference.Light });
        var themeService = new ThemeService(settings, Application.Current!);
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        themeService.ApplyStartupTheme();

        Application.Current!.RequestedThemeVariant.ShouldBe(ThemeVariant.Light);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
    }

    [AvaloniaFact]
    public async Task ApplyStartupTheme_System_ResolvesThroughPlatformSettings()
    {
        var settings = CreateSettings();
        await settings.UpdateAsync(current => current with { Theme = ThemePreference.System });
        var themeService = new ThemeService(settings, Application.Current!);

        themeService.ApplyStartupTheme();

        // La plateforme headless de test rapporte une préférence stable (voir AvaloniaHeadlessPlatformOptions
        // dans TestAppBuilder) : la garde exacte est que System se résout bien EN CETTE VALEUR-LÀ,
        // pas la valeur elle-même, dont ce test n'a pas la responsabilité.
        var expected = (ThemeVariant)Application.Current!.PlatformSettings!.GetColorValues().ThemeVariant;
        Application.Current!.RequestedThemeVariant.ShouldBe(expected);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
    }

    [AvaloniaFact]
    public async Task SettingsChanged_AfterConstruction_AppliesTheNewThemeImmediately()
    {
        // Le cycle complet côté bascule à chaud : Settings (section Général) appelle
        // SettingsService.UpdateAsync, ThemeService (déjà construit et abonné) réagit tout seul,
        // sans que qui que ce soit n'ait besoin de rappeler ApplyStartupTheme.
        var settings = CreateSettings();
        _ = new ThemeService(settings, Application.Current!);
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        await settings.UpdateAsync(current => current with { Theme = ThemePreference.Light });

        Application.Current!.RequestedThemeVariant.ShouldBe(ThemeVariant.Light);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
    }

    [AvaloniaFact]
    public async Task SettingsChanged_ToDark_SwitchesBackImmediately()
    {
        var settings = CreateSettings();
        await settings.UpdateAsync(current => current with { Theme = ThemePreference.Light });
        var themeService = new ThemeService(settings, Application.Current!);
        themeService.ApplyStartupTheme();
        Application.Current!.RequestedThemeVariant.ShouldBe(ThemeVariant.Light);

        await settings.UpdateAsync(current => current with { Theme = ThemePreference.Dark });

        Application.Current!.RequestedThemeVariant.ShouldBe(ThemeVariant.Dark);
    }
}