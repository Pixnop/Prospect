using Avalonia;
using Avalonia.Headless;

using Prospect.Desktop;
using Prospect.Desktop.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Prospect.Desktop.Tests;

/// <summary>
/// Point d'entrée requis par Avalonia.Headless.XUnit : construit l'<see cref="AppBuilder"/>
/// utilisé pour tous les tests <c>[AvaloniaFact]</c> de cet assembly, avec le rendu logiciel
/// headless (aucune fenêtre système réelle n'est créée).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}