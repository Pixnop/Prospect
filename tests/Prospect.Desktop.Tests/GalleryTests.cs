using System.Linq;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// La galerie de vérification (<see cref="MainWindow"/>) est un contenu de développement
/// provisoire (voir son commentaire d'en-tête) : ce test prouve qu'elle s'instancie et se
/// met en page sans erreur, avec les contrôles de chaque famille effectivement présents.
/// </summary>
public class GalleryTests
{
    [AvaloniaFact]
    public void Galerie_SInstancieAvecSesControlesPrincipaux()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();

        window.IsVisible.ShouldBeTrue();

        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        buttons.ShouldNotBeEmpty();

        window.GetVisualDescendants().OfType<TextBox>().ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<ComboBox>().ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<CheckBox>().ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<RadioButton>().ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<ToggleSwitch>().ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<ProgressBar>().ShouldNotBeEmpty();
        window.GetVisualDescendants()
            .OfType<ContentControl>()
            .Count(c => c.Classes.Contains("badge"))
            .ShouldBeGreaterThan(0);

        window.Close();
    }
}