using Avalonia.Headless.XUnit;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Test de fumée : prouve que la chaîne Avalonia headless et la composition root DI fonctionnent
/// de bout en bout. Le simple fait qu'un test <c>[AvaloniaFact]</c> s'exécute prouve déjà que
/// <see cref="App"/> se construit (chargement de App.axaml compris) ; ce test vérifie en plus
/// qu'une fenêtre résolue par le conteneur réel (sur un système de fichiers factice) s'ouvre.
/// </summary>
public class AppSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_SOuvreAvecLeTitreAttendu()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();

        window.Show();

        window.IsVisible.ShouldBeTrue();
        window.Title.ShouldBe("Prospect");

        window.Close();
    }
}