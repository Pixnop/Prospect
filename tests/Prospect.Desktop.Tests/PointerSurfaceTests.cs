using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Un contrôle n'est cliquable QUE là où un visuel est testé au pointeur, et un fond nul ne l'est
/// pas — contrairement à un fond <c>Transparent</c>, qui est peint (invisible) et donc touché. La
/// nuance ne se voit ni à la compilation, ni au rendu, ni dans un test de thème qui lit des
/// couleurs : elle ne se manifeste qu'au doigt, sur la moitié morte d'un contrôle. La session de
/// test Windows l'a remontée sur les listes déroulantes (« seul le texte répond ») ; ces gardes
/// injectent de vrais clics dans les zones NON textuelles.
/// </summary>
public sealed class PointerSurfaceTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 480, Height = 320 };
        window.Show();
        Settle(window);

        return window;
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
    }

    private static void Click(Window window, Visual target, Point offset)
    {
        var point = target.TranslatePoint(offset, window).ShouldNotBeNull();
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Le cœur du défaut : une ligne NON sélectionnée d'une liste ouverte. Elle n'a de fond ni par
    /// <c>:selected</c> ni par <c>:pointerover</c> (lequel exige déjà d'être détecté pour
    /// s'appliquer), donc sans fond au repos elle ne répondait que sur ses glyphes.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void ComboBoxDropDown_ClickingAnUnselectedRowAwayFromItsText_SelectsIt(string variant)
    {
        var combo = new ComboBox
        {
            Width = 220,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            ItemsSource = new[] { "Dernier lancement", "Nom", "Autre" },
            SelectedIndex = 0,
        };
        var window = Show(new Panel { Children = { combo } });
        window.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Settle(window);

        combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, true);
        Settle(window);

        var row = combo.ContainerFromIndex(2).ShouldBeOfType<ComboBoxItem>();
        row.IsSelected.ShouldBeFalse();

        // Bord droit de la ligne : hors du texte « Autre », dans la largeur que la ligne occupe.
        Click(window, row, new Point(row.Bounds.Width - 6, row.Bounds.Height / 2));

        combo.SelectedIndex.ShouldBe(2, $"la ligne doit répondre sur toute sa surface, pas seulement sur son texte (variante {variant})");
        window.Close();
    }

    /// <summary>
    /// Le padding horizontal d'une ligne (12 de chaque côté) fait partie de la ligne : le clic y
    /// tombe visuellement « dans » la ligne survolée.
    /// </summary>
    [AvaloniaFact]
    public void ComboBoxDropDown_ClickingInTheRowPadding_SelectsIt()
    {
        var combo = new ComboBox
        {
            Width = 220,
            ItemsSource = new[] { "Dernier lancement", "Nom", "Autre" },
            SelectedIndex = 0,
        };
        var window = Show(new Panel { Children = { combo } });

        combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, true);
        Settle(window);

        var row = combo.ContainerFromIndex(1).ShouldBeOfType<ComboBoxItem>();
        Click(window, row, new Point(4, row.Bounds.Height / 2));

        combo.SelectedIndex.ShouldBe(1);
        window.Close();
    }

    /// <summary>
    /// Le fond transparent posé au repos ne doit pas AVALER les états : sélection et survol se
    /// peignent toujours, sinon la correction de surface aurait coûté la lisibilité de la liste.
    /// </summary>
    [AvaloniaFact]
    public void ComboBoxDropDown_SelectedRow_StillPaintsItsSelectedBackground()
    {
        var combo = new ComboBox
        {
            Width = 220,
            ItemsSource = new[] { "Dernier lancement", "Nom" },
            SelectedIndex = 0,
        };
        var window = Show(new Panel { Children = { combo } });

        combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, true);
        Settle(window);

        var selected = combo.ContainerFromIndex(0).ShouldBeOfType<ComboBoxItem>();
        var unselected = combo.ContainerFromIndex(1).ShouldBeOfType<ComboBoxItem>();

        BackgroundOf(selected).ShouldBe(Application.Current!.FindResource(ThemeVariant.Dark, "BgSelected"));

        // La ligne au repos est bien peinte, mais en transparent : présente pour le pointeur,
        // absente à l'œil.
        var resting = BackgroundOf(unselected).ShouldBeAssignableTo<ISolidColorBrush>();
        resting!.Color.A.ShouldBe((byte)0);

        window.Close();
    }

    /// <summary>
    /// La case à cocher : l'espace entre le carré et son libellé, et la largeur qui suit le
    /// libellé, appartiennent au contrôle — le curseur y affiche déjà la main.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(20d)]
    [InlineData(-2d)]
    public void CheckBox_ClickingBesideTheBoxAndTheLabel_Toggles(double offsetX)
    {
        var check = new CheckBox
        {
            Content = "Afficher unstable et pre",
            Width = 260,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = Show(new Panel { Children = { check } });
        check.IsChecked.ShouldBe(false);

        var x = offsetX >= 0 ? offsetX : check.Bounds.Width + offsetX;
        Click(window, check, new Point(x, check.Bounds.Height / 2));

        check.IsChecked.ShouldBe(true, $"la case doit répondre à x={x}, dans sa propre surface");
        window.Close();
    }

    /// <summary>Le bouton radio partage le template de la case : même surface, même garde.</summary>
    [AvaloniaFact]
    public void RadioButton_ClickingBetweenTheCircleAndItsLabel_Checks()
    {
        var radio = new RadioButton
        {
            Content = "Version stable",
            Width = 260,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = Show(new Panel { Children = { radio } });

        Click(window, radio, new Point(20, radio.Bounds.Height / 2));

        radio.IsChecked.ShouldBe(true);
        window.Close();
    }

    /// <summary>
    /// Le déclencheur de la liste, lui, tient son fond de la vitre d'ITEM depuis le rethème verre :
    /// cette garde fige le fait que toute sa surface (padding et zone du chevron comprises) ouvre
    /// la liste, dans les deux variantes.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void ComboBoxTrigger_ClickingTheChevronZone_OpensTheDropDown(string variant)
    {
        var combo = new ComboBox
        {
            Width = 180,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            ItemsSource = new[] { "un", "deux" },
            SelectedIndex = 0,
        };
        var window = Show(new Panel { Children = { combo } });
        window.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Settle(window);

        Click(window, combo, new Point(combo.Bounds.Width - 8, combo.Bounds.Height / 2));

        combo.IsDropDownOpen.ShouldBeTrue($"variante {variant}");
        combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
        window.Close();
    }

    private static IBrush? BackgroundOf(ComboBoxItem item)
        => item.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .First(presenter => presenter.Name == "PART_ContentPresenter")
            .Background;
}