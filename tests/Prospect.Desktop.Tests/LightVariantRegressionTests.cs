using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Wizard;
using Prospect.Desktop.Views.Instance;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Garde anti-régression du thème clair : jusqu'ici, seules les entrées de ThemeDictionaries
/// étaient vérifiées (<see cref="DesignTokensTests"/>) et les ControlThemes n'étaient exercés
/// qu'en thème sombre, celui par défaut (<see cref="ControlThemeTests"/>) — rien ne montait un
/// contrôle réel puis ne basculait <see cref="Application.RequestedThemeVariant"/> à chaud pour
/// vérifier que le rendu suivait. Née d'une QA visuelle par capture headless qui a trouvé le nom
/// de carte d'instance et sa ligne « dernière session » invisibles en thème clair, ainsi que des
/// pastilles neutres au fond figé.
///
/// L'investigation a révélé deux causes distinctes, toutes deux couvertes ici :
///
/// 1. La vraie cause du défaut le plus visible n'était pas une couleur figée mais un rognage :
///    Button.axaml ".cardHit" héritait Height="{StaticResource ControlHMd}" (32px) et
///    ClipToBounds=True (défaut de tout TemplatedControl côté Avalonia) du ControlTheme Button
///    générique, sans les recouvrir. Son contenu réel (bande d'art 96px + nom + dernière session,
///    ~150px) dépassait donc largement les 32px alloués et se faisait purement rogner hors du
///    rendu — dans les DEUX thèmes, pas seulement le clair (vérifié par capture avant/après
///    correctif dans les deux variantes). Le vide résultant se voit à peine sur un fond de carte
///    resté sombre, mais saute aux yeux sur un fond devenu clair, d'où un défaut signalé comme
///    « propre au clair » alors que le rognage touchait tout autant le sombre.
/// 2. Plusieurs ressources de couleur bien réelles, elles, étaient effectivement figées : le fond
///    neutre des badges (pastilles « universel »/« manuel », badges de côté du navigateur ModDB),
///    le séparateur « · » de trois lignes meta, la piste au repos du switch et de la barre de
///    progression, et le fond de l'infobulle — ce dernier particulièrement sévère puisque son
///    Foreground (Text1) suit le thème pendant que son fond restait figé en sombre, ce qui donne
///    du texte sombre sur fond sombre une fois l'application basculée en clair.
/// </summary>
public sealed class LightVariantRegressionTests
{
    private static async Task CreateInstanceViaWizardAsync(ShellViewModel shell, HomeViewModel home, string name, string version)
    {
        home.NewInstanceCommand.Execute(null);
        var wizard = shell.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        await wizard.LoadVersionsCommand.ExecuteAsync(null);
        wizard.Name = name;
        wizard.NextCommand.Execute(null);
        wizard.VersionChoices.First(choice => choice.VersionText == version).SelectCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        await wizard.CreateCommand.ExecuteAsync(null);
    }

    private static Window ShowInWindow(Control content)
    {
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

    /// <summary>
    /// Bascule le thème à chaud sur une fenêtre déjà affichée, sans la recréer : c'est le scénario
    /// qui a le plus de chances de reproduire une capture QA (l'appli tourne, seule la variante
    /// change), et le plus exigeant pour DynamicResource (une ressource qui ne se réévaluerait
    /// qu'à la construction initiale du contrôle laisserait ce test au rouge). Utilisé par les
    /// tests montés sur le conteneur DI réel (fenêtre unique, plusieurs pages) ; les tests de
    /// contrôle isolé plus bas préfèrent reconstruire une fenêtre neuve par thème (voir leur
    /// commentaire) pour rester robustes à l'ordre d'exécution de la suite.
    /// </summary>
    private static void SwitchToLightTheme(Window window)
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
    }

    /// <summary>
    /// Compte les couleurs RGB distinctes peintes sur une bande horizontale au milieu de
    /// <paramref name="target"/>, en lisant le vrai bitmap rendu (UseHeadlessDrawing=false, voir
    /// TestAppBuilder). Un aplat de fond (rien de peint) ne donne qu'une seule couleur ; un glyphe
    /// de texte anticrénelé en peint plusieurs (mélange progressif encre/fond). C'est la vérification
    /// la plus fidèle à une capture d'écran réelle : elle aurait détecté le rognage du bug n°1
    /// ci-dessus alors même que Foreground, lui, avait toujours la bonne valeur (rien à voir avec
    /// une histoire de couleur, tout avec de la mise en page).
    /// </summary>
    private static int CountDistinctColorsOnRow(Window window, Visual target, double sampleWidth = 120)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = (WriteableBitmap)window.CaptureRenderedFrame()!;
        var bounds = target.Bounds;
        var origin = target.TranslatePoint(new Point(2, bounds.Height / 2), window) ?? default;

        using var locked = frame.Lock();
        var bytes = new byte[locked.RowBytes * frame.PixelSize.Height];
        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var y = (int)origin.Y;
        var maxX = (int)System.Math.Min(origin.X + sampleWidth, frame.PixelSize.Width);
        var colors = new HashSet<int>();
        for (var x = (int)origin.X; x < maxX; x++)
        {
            var offset = y * locked.RowBytes + x * 4;
            if (offset < 0 || offset + 3 >= bytes.Length) continue;
            colors.Add(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16));
        }
        return colors.Count;
    }

    /// <summary>
    /// Construit <paramref name="build"/> deux fois, une fois par thème (plutôt que de basculer à
    /// chaud une seule instance), et compare la propriété lue par <paramref name="read"/>. Pour un
    /// contrôle isolé (pas de fenêtre DI derrière), c'est plus robuste que la bascule à chaud :
    /// repéré sur ToggleSwitch, dont le PART_Track pouvait rater le rafraîchissement de
    /// DynamicResource selon ce qui tournait juste avant dans le même run (suite complète), alors
    /// que la même ressource, elle, était toujours correcte à la résolution directe
    /// (TryGetResource) — reconstruire neuf sous le thème cible contourne entièrement la question.
    /// </summary>
    private static (Color Dark, Color Light) BuildUnderEachTheme<T>(Func<T> build, Func<T, Color> read)
        where T : Control
    {
        var darkControl = build();
        var darkWindow = ShowInWindow(darkControl);
        var dark = read(darkControl);
        darkWindow.Close();

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var lightControl = build();
        var lightWindow = ShowInWindow(lightControl);
        var light = read(lightControl);
        lightWindow.Close();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        return (dark, light);
    }

    [AvaloniaFact]
    public async Task InstanceCard_NameAndLastPlayed_AreNotClippedByCardHitButton()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        await CreateInstanceViaWizardAsync(shell, home, "Vintage Survival", "1.20.4");
        window.Settle();

        var cardHitButton = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("cardHit"));
        var nameText = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Vintage Survival");

        // Recouvrement direct des deux propriétés héritées du ControlTheme Button générique qui
        // causaient le rognage (voir Button.axaml ".cardHit").
        cardHitButton.ClipToBounds.ShouldBeFalse();
        cardHitButton.Bounds.Height.ShouldBeGreaterThan(130); // bande d'art (96) + nom/dernière session, pas les 32px d'un Button ordinaire.

        // Et la vérification « à la QA » : le nom est vraiment peint, pas juste présent dans l'arbre visuel.
        CountDistinctColorsOnRow(window, nameText).ShouldBeGreaterThan(1);

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceCard_NameForeground_SwitchesFromLinenToDarkTextInLightTheme()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        await CreateInstanceViaWizardAsync(shell, home, "Vintage Survival", "1.20.4");
        window.Settle();

        var nameText = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Vintage Survival");

        var darkForeground = nameText.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        darkForeground.ShouldBe(Color.Parse("#EDE6DD")); // Text1 sombre (stone-100), inchangé par ce correctif.

        SwitchToLightTheme(window);

        var lightForeground = nameText.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        lightForeground.ShouldBe(Color.Parse("#231E1A")); // Text1 clair.
        lightForeground.ShouldNotBe(darkForeground);

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceCard_VersionBadgeOnArtBand_ForegroundStaysPinnedAcrossThemes()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        await CreateInstanceViaWizardAsync(shell, home, "Vintage Survival", "1.20.4");
        window.Settle();

        var versionBadge = window.GetVisualDescendants().OfType<ContentControl>()
            .First(c => c.Classes.Contains("badge") && c.Classes.Contains("pinned"));
        versionBadge.Classes.ShouldContain("stable"); // 1.20.4 n'a pas de suffixe -rc/-pre : canal stable (ChannelBadgePresentation).

        var darkForeground = versionBadge.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        darkForeground.ShouldBe(Color.Parse("#7DBBA4")); // verdigris-400, SuccessText sombre.

        SwitchToLightTheme(window);

        var lightForeground = versionBadge.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        lightForeground.ShouldBe(darkForeground); // épinglé par arbitrage : la bande d'art ne bouge jamais, donc son texte non plus.

        window.Close();
    }

    [AvaloniaFact]
    public async Task InstanceDetailHeader_VersionBadgeAndSeparatorDot_StillFollowTheTheme()
    {
        // Contre-test de l'arbitrage : ce badge de version-ci n'est PAS posé sur la plaque sombre
        // de l'en-tête (colonne séparée du Border de la vignette, voir InstanceDetailView.axaml),
        // donc il doit rester réactif au thème comme n'importe quel badge ordinaire — garde-fou
        // contre un futur "pinned" mal placé ici. Vérifie aussi au passage le séparateur « · » de
        // la ligne meta (Text4, plus le littéral Stone600 figé trouvé au balayage).
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);
        window.Settle();
        await CreateInstanceViaWizardAsync(shell, home, "Vintage Survival", "1.20.4");
        window.Settle();

        home.Instances[0].OpenCommand.Execute(null);
        window.Settle();
        shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        window.GetVisualDescendants().OfType<InstanceDetailView>().ShouldNotBeEmpty();

        var versionBadge = window.GetVisualDescendants().OfType<ContentControl>()
            .First(c => c.Classes.Contains("badge") && !c.Classes.Contains("pinned"));
        var separatorDot = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "·");

        var darkBadgeForeground = versionBadge.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        var darkDotForeground = separatorDot.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        darkDotForeground.ShouldBe(Color.Parse("#8A8177")); // Text4 sombre (stone-375, remonté pour le contraste) — plus jamais Stone600 fixe (#443D36).

        SwitchToLightTheme(window);

        var lightBadgeForeground = versionBadge.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;
        var lightDotForeground = separatorDot.Foreground.ShouldBeAssignableTo<ISolidColorBrush>()!.Color;

        lightBadgeForeground.ShouldNotBe(darkBadgeForeground);
        lightDotForeground.ShouldBe(Color.Parse("#6E655B")); // Text4 clair (stone-450, descendu pour le contraste).
        lightDotForeground.ShouldNotBe(darkDotForeground);

        window.Close();
    }

    [AvaloniaFact]
    public void Badge_NeutralVariant_BackgroundSwitchesWithTheme()
    {
        // Couvre toute la famille des pastilles sans ton de couleur : « universel »/« manuel » de
        // l'onglet Mods du détail d'instance, badges de côté du navigateur ModDB — tous des
        // ContentControl Classes="badge" nus, donc sur le style de base dont le fond était figé en
        // #24211E (stone-800) avant ce correctif.
        var (dark, light) = BuildUnderEachTheme(
            () => new ContentControl { Content = "manuel", Classes = { "badge" } },
            badge => badge.Background.ShouldBeAssignableTo<ISolidColorBrush>()!.Color);

        // Le fond de pastille neutre est devenu une vitre d'item (50 %) avec le rethème verre :
        // l'ancien aplat stone-800 (#24211E) n'existe plus, mais l'invariant que ce test protège
        // est inchangé — le fond doit BOUGER d'une variante à l'autre, sinon une pastille sombre
        // se retrouve posée sur une carte claire avec son texte devenu illisible.
        dark.ShouldBe(Color.Parse("#80111010"));
        light.ShouldNotBe(dark);
    }

    [AvaloniaFact]
    public void ToggleSwitch_OffTrack_BackgroundSwitchesWithTheme()
    {
        var (dark, light) = BuildUnderEachTheme(
            () => new ToggleSwitch { IsChecked = false },
            toggle => FindPart<Border>(toggle, "PART_Track").Background.ShouldBeAssignableTo<ISolidColorBrush>()!.Color);

        // Piste au repos devenue vitre d'item (50 %) avec le rethème verre ; même invariant
        // qu'avant : elle doit suivre le thème, faute de quoi la piste reste presque noire sur
        // une carte claire pendant que sa bordure et son pouce, eux, ont basculé.
        dark.ShouldBe(Color.Parse("#80111010"));
        light.ShouldNotBe(dark);
    }

    [AvaloniaFact]
    public void ProgressBar_TrackBackground_SwitchesWithTheme()
    {
        var (dark, light) = BuildUnderEachTheme(
            () => new ProgressBar { Minimum = 0, Maximum = 100, Value = 50 },
            bar => bar.Background.ShouldBeAssignableTo<ISolidColorBrush>()!.Color);

        dark.ShouldBe(Color.Parse("#2B2724")); // valeur historique, inchangée en sombre.
        light.ShouldNotBe(dark);
    }

    [AvaloniaFact]
    public void TooltipBg_Token_SwitchesWithThemeAndNeverCollidesWithText1()
    {
        // Le plus sévère des défauts trouvés au balayage : avant ce correctif, le fond de
        // l'infobulle restait figé en #2B2724 (stone-750) pendant que son Foreground (Text1)
        // suivait le thème — en clair, Text1 devient sombre (#231E1A) sur un fond resté sombre.
        // Déclenger une vraie Popup ToolTip en environnement headless est fragile (minuteries de
        // survol) ; la résolution de ressource par variante explicite est la vérification directe
        // de ce même mécanisme, dans le même style que DesignTokensTests.
        var app = Application.Current!;

        app.TryGetResource("TooltipBg", ThemeVariant.Dark, out var darkBg).ShouldBeTrue();
        app.TryGetResource("TooltipBg", ThemeVariant.Light, out var lightBg).ShouldBeTrue();
        app.TryGetResource("Text1", ThemeVariant.Light, out var lightText).ShouldBeTrue();

        var darkBgColor = darkBg.ShouldBeOfType<SolidColorBrush>().Color;
        var lightBgColor = lightBg.ShouldBeOfType<SolidColorBrush>().Color;
        var lightTextColor = lightText.ShouldBeOfType<SolidColorBrush>().Color;

        // La bulle est passée à la profondeur MENU du verre (91 % depuis la passe de lisibilité,
        // voir Glass.axaml) : l'aplat stone-750 historique a disparu, l'invariant non — le fond
        // suit le thème et ne coïncide jamais avec l'encre.
        darkBgColor.ShouldBe(Color.Parse("#E8111010"));
        lightBgColor.ShouldNotBe(darkBgColor);
        lightBgColor.ShouldNotBe(lightTextColor); // fond et texte de la bulle ne doivent jamais coïncider.
        // Une bulle translucide ne doit pas l'être au point de laisser lire à travers : à 91 %,
        // le fond qui transparaît ne peut plus faire basculer le contraste de son texte.
        darkBgColor.A.ShouldBeGreaterThan((byte)180);
    }
}