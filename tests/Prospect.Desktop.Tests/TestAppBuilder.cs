using Avalonia;
using Avalonia.Headless;

using Prospect.Desktop;
using Prospect.Desktop.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Prospect.Desktop.Tests;

/// <summary>
/// Point d'entrée requis par Avalonia.Headless.XUnit : construit l'<see cref="AppBuilder"/>
/// utilisé pour tous les tests <c>[AvaloniaFact]</c> de cet assembly (aucune fenêtre système
/// réelle n'est créée). <c>UseHeadlessDrawing=false</c> conserve le vrai moteur Skia derrière
/// la fenêtre headless : le stub par défaut (<c>HeadlessFontManagerStub</c>) ne sait pas
/// charger de polices réelles, ce qui rend impossible toute vérification sérieuse des
/// polices embarquées du design system. Le rendu logiciel Skia n'a besoin d'aucun serveur
/// d'affichage, donc ce choix fonctionne aussi bien en CI que sur un poste de dev.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}