using Avalonia.Headless.XUnit;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Test de fumée : prouve que la chaîne Avalonia headless fonctionne de bout en bout. Le
/// simple fait qu'un test <c>[AvaloniaFact]</c> s'exécute prouve déjà que <see cref="App"/> se
/// construit (chargement de App.axaml compris) ; ce test vérifie en plus qu'une fenêtre
/// s'ouvre réellement.
/// </summary>
public class AppSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_SOuvreAvecLeTitreAttendu()
    {
        var window = new MainWindow();

        window.Show();

        window.IsVisible.ShouldBeTrue();
        window.Title.ShouldBe("Prospect");
    }
}