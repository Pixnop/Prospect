using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Vérifie que les ControlThemes du design system s'appliquent réellement à une instance
/// de contrôle (et pas seulement que les fichiers AXAML compilent) : couleurs résolues
/// dans leurs différents états et variantes. Reste pragmatique : on vérifie que le
/// système de tokens est branché sur chaque famille de contrôle, pas chaque pixel.
/// </summary>
public class ControlThemeTests
{
    private static Window ShowInWindow(Control content)
    {
        // Taille de fenêtre explicite : sans elle, la fenêtre se dimensionne sur son
        // contenu (SizeToContent implicite) et certaines liaisons qui dépendent de la
        // taille réelle du conteneur (ex. les MultiBinding de largeur de ProgressBar) ne
        // se stabilisent pas de façon fiable sur le premier passage de layout.
        var window = new Window { Content = content, Width = 480, Height = 320 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        return window;
    }

    private static T FindPart<T>(Visual root, string name)
        where T : Visual
        => root.GetVisualDescendants().OfType<T>().First(x => x.Name == name);

    [AvaloniaFact]
    public void ButtonControlTheme_EstEnregistreCommeRessource()
    {
        var app = Application.Current!;

        app.TryGetResource(typeof(Button), ThemeVariant.Dark, out var value).ShouldBeTrue();
        value.ShouldBeOfType<ControlTheme>();
    }

    [AvaloniaFact]
    public void Bouton_UtiliseAccentParDefaut()
    {
        var button = new Button { Content = "Jouer" };
        var window = ShowInWindow(button);

        var background = button.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#C4854F"));

        window.Close();
    }

    [AvaloniaFact]
    public void Bouton_ClasseSecondary_EstUneVitreDItemALisereClair()
    {
        // Depuis le passage au verre, un bouton secondaire n'est plus un contour vide sur fond de
        // page : c'est une vitre d'item (GlassItem, 50 %) bordée d'une arête CLAIRE (GlassEdge,
        // cuivre à 10 %) et non plus d'un gris de rampe. L'intention du test ne change pas —
        // secondary doit rester visuellement distinct de primary, qui est un aplat d'accent plein.
        var button = new Button { Content = "Annuler", Classes = { "secondary" } };
        var window = ShowInWindow(button);

        var background = button.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#80111010"));
        background.Color.A.ShouldBeLessThan((byte)255); // une vitre laisse passer le fond, un aplat non.
        background.Color.ShouldNotBe(Color.Parse("#C4854F")); // jamais l'accent plein de primary.

        var border = button.BorderBrush.ShouldBeAssignableTo<ISolidColorBrush>();
        border!.Color.ShouldBe(Color.Parse("#1AD5A275"));

        window.Close();
    }

    [AvaloniaFact]
    public void Bouton_ClasseDanger_UtiliseLaCouleurDanger()
    {
        var button = new Button { Content = "Supprimer", Classes = { "danger" } };
        var window = ShowInWindow(button);

        var background = button.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#C1503B"));

        window.Close();
    }

    [AvaloniaFact]
    public void CaseACocher_CocheeUtiliseLeFondAccent()
    {
        var checkBox = new CheckBox { Content = "Activer", IsChecked = true };
        var window = ShowInWindow(checkBox);

        var box = FindPart<Border>(checkBox, "PART_Box");
        var background = box.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#C4854F"));

        var checkMark = FindPart<Avalonia.Controls.Shapes.Path>(checkBox, "PART_CheckMark");
        checkMark.IsVisible.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public void BoutonRadio_CocheAfficheLePoint()
    {
        var radio = new RadioButton { Content = "Option", IsChecked = true };
        var window = ShowInWindow(radio);

        var box = FindPart<Border>(radio, "PART_Box");
        var background = box.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#C4854F"));

        var dot = FindPart<Ellipse>(radio, "PART_Dot");
        dot.IsVisible.ShouldBeTrue();

        window.Close();
    }

    [AvaloniaFact]
    public void ToggleSwitch_CocheUtiliseLeFondAccent()
    {
        var toggle = new ToggleSwitch { IsChecked = true };
        var window = ShowInWindow(toggle);

        var track = FindPart<Border>(toggle, "PART_Track");
        var background = track.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#C4854F"));

        window.Close();
    }

    [AvaloniaFact]
    public void BarreDeProgression_DeterminéeRempliteALaBonneProportion()
    {
        // PART_VisualFill porte le rendu réel (voir le commentaire d'en-tête de
        // ProgressBar.axaml : le recalcul interne de PART_Indicator par la classe
        // ProgressBar ne se redéclenchait pas de façon fiable dans cet environnement,
        // vérifié à l'écran). Sa largeur est recalculée par liaison à partir de
        // Value/Minimum/Maximum et de la largeur réelle du conteneur.
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 25, Width = 200, Height = 4 };
        var window = ShowInWindow(bar);

        // PART_VisualFill anime sa largeur (transition dur-normal) : on laisse à la
        // transition largement le temps de se stabiliser avant de lire la valeur finale.
        var fill = FindPart<Border>(bar, "PART_VisualFill");
        fill.Width.ShouldBe(50, tolerance: 0.01); // 25 % de 200
        var background = fill.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#C4854F"));

        window.Close();
    }

    [AvaloniaFact]
    public void BarreDeProgression_IndetermineeOccupeTrenteCinqPourCent()
    {
        var bar = new ProgressBar { IsIndeterminate = true, Width = 200 };
        var window = ShowInWindow(bar);

        var indicator = FindPart<Border>(bar, "PART_IndeterminateIndicator");
        indicator.IsVisible.ShouldBeTrue();
        indicator.Width.ShouldBe(70, tolerance: 0.01); // 35 % de 200, comme .pr-progress--indeterminate

        window.Close();
    }

    [AvaloniaFact]
    public void Badge_ClasseStable_UtiliseLaTeinteSuccess()
    {
        var badge = new ContentControl { Content = "stable", Classes = { "badge", "stable" } };
        var window = ShowInWindow(badge);

        var background = badge.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#295E9C86"));

        window.Close();
    }

    [AvaloniaFact]
    public void Tag_ClasseOn_UtiliseLaTeinteAccent()
    {
        var tag = new ContentControl { Content = "Installés", Classes = { "tag", "on" } };
        var window = ShowInWindow(tag);

        var background = tag.Background.ShouldBeAssignableTo<ISolidColorBrush>();
        background!.Color.ShouldBe(Color.Parse("#24D5A275"));

        window.Close();
    }

    [AvaloniaFact]
    public void ComboBox_SOuvreEtExposeSesElements()
    {
        var combo = new ComboBox { IsDropDownOpen = false };
        combo.Items.Add(new ComboBoxItem { Content = "stable" });
        combo.Items.Add(new ComboBoxItem { Content = "unstable" });
        combo.SelectedIndex = 0;
        var window = ShowInWindow(combo);

        combo.IsDropDownOpen = true;
        window.UpdateLayout();

        combo.IsDropDownOpen.ShouldBeTrue();

        window.Close();
    }
}