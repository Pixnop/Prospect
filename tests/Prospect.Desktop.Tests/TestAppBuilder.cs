using Avalonia;
using Avalonia.Headless;

using Prospect.Core.Settings;
using Prospect.Desktop;
using Prospect.Desktop.Resources;
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
/// <remarks>
/// La langue de l'interface est ÉPINGLÉE sur le français ici, pour tout l'assembly. Sans cet
/// épinglage explicite, la suite dépendrait du défaut compilé de <see cref="UiText"/> : vrai
/// aujourd'hui, silencieusement faux le jour où ce défaut change. Les quelques tests qui exercent
/// l'anglais rebasculent eux-mêmes (<see cref="UiText.ResetForTests"/>) et rétablissent le
/// français, exactement comme les tests de thème rétablissent la variante sombre. La culture de la
/// machine n'entre jamais en jeu : la CI tourne en <c>en-US</c>, un poste de dev français en
/// <c>fr-FR</c>, et aucune assertion de libellé ne doit en dépendre.
/// </remarks>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(_ => UiText.ResetForTests(ProspectSettings.French));
}