using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Settings;
using Prospect.Desktop.Layout;
using Prospect.Desktop.Services;
using Prospect.Desktop.Views.Dialogs;
using Prospect.Desktop.Views.FirstRun;
using Prospect.Desktop.Views.Migration;
using Prospect.Desktop.Views.Mods;
using Prospect.Desktop.Views.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Contrat du système de verre (Styles/Tokens/Glass.axaml). Ce que ces tests protègent n'est pas
/// une teinte précise mais la MÉCANIQUE : chaque profondeur existe dans les deux variantes, aucune
/// n'est opaque (sans quoi il n'y aurait plus de verre du tout), la hiérarchie des cinq
/// profondeurs est respectée, et l'empilement du fond de fenêtre reste celui qu'on a déclaré.
///
/// Ils existent parce que la panne typique de ce système est silencieuse : une entrée oubliée dans
/// la variante claire ne casse rien au démarrage, elle donne un panneau sans fond une fois
/// l'application basculée — exactement la classe de défaut qu'avait trouvée la QA visuelle du
/// thème clair (voir <see cref="LightVariantRegressionTests"/>), mais sur les surfaces cette fois.
/// </summary>
public sealed class GlassTokensTests
{
    /// <summary>Les vitres, de la plus fine à la plus dense.</summary>
    private static readonly string[] Depths = ["GlassPane", "GlassChrome", "GlassItem", "GlassMenu", "GlassDialog"];

    private static readonly string[] Brushes =
    [
        "GlassVeil", "GlassPane", "GlassChrome", "GlassItem", "GlassMenu", "GlassDialog",
        "GlassEdge", "GlassEdgeStrong", "GlassEdgeHot", "GlassRowOdd", "GlassRowEven",
    ];

    private static readonly string[] Shadows =
    [
        "GlassShadow", "GlassShadowLg", "GlassSheen", "GlassSheenStrong", "GlassSheenHot",
        "GlassElevItem", "GlassElevMenu",
    ];

    private static Color Resolve(string key, ThemeVariant variant)
    {
        Application.Current!.TryGetResource(key, variant, out var value).ShouldBeTrue($"{key} manque en {variant}");

        return value.ShouldBeOfType<SolidColorBrush>().Color;
    }

    [AvaloniaFact]
    public void ChaqueRessourceDeVerre_ExisteDansLesDeuxVariantes()
    {
        var app = Application.Current!;

        foreach (var key in Brushes.Concat(Shadows).Append("GlassTextShadow"))
        {
            app.TryGetResource(key, ThemeVariant.Dark, out var dark).ShouldBeTrue($"{key} manque en sombre");
            app.TryGetResource(key, ThemeVariant.Light, out var light).ShouldBeTrue($"{key} manque en clair");
            dark.ShouldNotBeNull();
            light.ShouldNotBeNull();
        }
    }

    [AvaloniaFact]
    public void ChaqueVitre_LaissePasserLeFond_DansLesDeuxVariantes()
    {
        foreach (var key in Brushes)
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                var color = Resolve(key, variant);
                color.A.ShouldBeLessThan((byte)255, $"{key} en {variant} est opaque : ce n'est plus du verre");
                color.A.ShouldBeGreaterThan((byte)0, $"{key} en {variant} est invisible");
            }
        }
    }

    [AvaloniaFact]
    public void LesCinqProfondeurs_SontOrdonneesDeLaPlusFineALaPlusDense()
    {
        // pane (conteneur de page) < chrome (sidebar, titlebar) < item (carte, champ) <
        // menu (dropdown, popover, infobulle) < dialog (panneau modal). C'est cet ordre qui fait la
        // lecture en profondeur : un dialogue doit toujours masquer davantage le fond que la page
        // qui le porte, et davantage qu'un menu qui ne fait que se poser dessus.
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var alphas = Depths.Select(key => Resolve(key, variant).A).ToList();
            alphas.ShouldBe(alphas.OrderBy(a => a).ToList(), $"profondeurs désordonnées en {variant}");
            alphas.Distinct().Count().ShouldBe(alphas.Count, $"deux profondeurs confondues en {variant}");
        }
    }

    /// <summary>
    /// La cinquième profondeur, celle qui n'existe pas dans le CSS de référence. Ce test épingle la
    /// décision produit : un dialogue modal recouvre de la LECTURE, un menu ne fait que se poser
    /// brièvement sur quelques lignes, donc les deux ne peuvent pas partager la même vitre. Le
    /// plancher retenu vient du critère visuel — aucun texte de la page ne doit se deviner à
    /// travers, même sur le fond clair le plus chargé — et il est plus haut en clair, où la vitre
    /// et le fond ont des luminances voisines.
    /// </summary>
    [AvaloniaFact]
    public void LaVitreDesDialogues_MasquePlusQueCelleDesMenus_DansLesDeuxVariantes()
    {
        foreach (var (variant, floor) in new[] { (ThemeVariant.Dark, 0.94), (ThemeVariant.Light, 0.96) })
        {
            var menu = Resolve("GlassMenu", variant).A;
            var dialog = Resolve("GlassDialog", variant).A;

            dialog.ShouldBeGreaterThan(menu, $"un dialogue doit masquer plus qu'un menu en {variant}");
            (dialog / 255d).ShouldBeGreaterThanOrEqualTo(floor, $"la vitre de dialogue est trop fine en {variant}");
            dialog.ShouldBeLessThan((byte)255, $"la vitre de dialogue est opaque en {variant} : ce n'est plus du verre");
        }
    }

    /// <summary>
    /// Et les dialogues la portent RÉELLEMENT. Le contrat précédent ne vérifie que les valeurs du
    /// dictionnaire : une vue oubliée resterait sur GlassMenu sans que rien ne le signale, puisqu'un
    /// panneau un peu trop transparent se rend parfaitement. Le sens de lecture importe : ce test
    /// interroge la vue montée, pas l'AXAML.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(typeof(RenameDialogView))]
    [InlineData(typeof(DeleteInstanceDialogView))]
    [InlineData(typeof(DuplicateDialogView))]
    [InlineData(typeof(StopInstanceDialogView))]
    [InlineData(typeof(SignOutDialogView))]
    [InlineData(typeof(UninstallVersionDialogView))]
    [InlineData(typeof(RestoreInstanceBackupDialogView))]
    [InlineData(typeof(DeleteInstanceBackupDialogView))]
    [InlineData(typeof(InstanceDoctorDialogView))]
    [InlineData(typeof(ModDetailDialogView))]
    [InlineData(typeof(ModInstallPlanDialogView))]
    [InlineData(typeof(ModUpdatePlanDialogView))]
    [InlineData(typeof(UninstallModDialogView))]
    [InlineData(typeof(AdoptVslView))]
    [InlineData(typeof(FirstRunScreenView))]
    [InlineData(typeof(WizardView))]
    public void ChaquePanneauModal_EstPoseSurLaVitreDesDialogues(Type viewType)
    {
        var view = (UserControl)Activator.CreateInstance(viewType)!;
        var window = new Window { Content = view };
        window.Show();
        window.Settle();

        var panel = view.GetVisualDescendants().OfType<Border>()
            .First(border => border.Background is SolidColorBrush);

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            window.RequestedThemeVariant = variant;
            window.Settle();

            panel.Background.ShouldBeOfType<SolidColorBrush>().Color
                .ShouldBe(Resolve("GlassDialog", variant), $"{viewType.Name} n'est pas sur GlassDialog en {variant}");
        }

        window.Close();
    }

    [AvaloniaFact]
    public void LeLisere_EstClairEtNonSombre_DansLesDeuxVariantes()
    {
        // Le détail porteur du verre : l'arête attrape la lumière, elle ne cerne pas la vitre.
        // En sombre elle est plus claire que la vitre qu'elle borde, en clair elle est cuivrée
        // (une arête neutre grise ferait retomber le panneau en boîte pleine).
        Resolve("GlassEdge", ThemeVariant.Dark).ShouldBe(Color.Parse("#1AD5A275")); // copper-400 à 10 %.
        Resolve("GlassEdgeStrong", ThemeVariant.Dark).A
            .ShouldBeGreaterThan(Resolve("GlassEdge", ThemeVariant.Dark).A);

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var edge = Resolve("GlassEdge", variant);
            edge.R.ShouldBeGreaterThan(edge.B, $"l'arête doit rester cuivrée en {variant}");
        }
    }

    /// <summary>
    /// Les trois crans d'arête sont ordonnés, dans les deux variantes : filet au repos, arête
    /// marquée sur un contrôle survolé, contour franc sur une surface qui doit CRIER qu'elle est
    /// cliquable. Le défaut que ce test empêche est celui qui a été remonté : un survol trop
    /// discret pour être vu, donc une carte d'accueil que personne ne pense à cliquer.
    /// </summary>
    [AvaloniaFact]
    public void LesTroisCransDArete_SontOrdonnesDansLesDeuxVariantes()
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var edge = Resolve("GlassEdge", variant);
            var strong = Resolve("GlassEdgeStrong", variant);
            var hot = Resolve("GlassEdgeHot", variant);

            strong.A.ShouldBeGreaterThan(edge.A, $"GlassEdgeStrong doit dépasser GlassEdge en {variant}");
            hot.A.ShouldBeGreaterThan(strong.A, $"GlassEdgeHot doit dépasser GlassEdgeStrong en {variant}");
            hot.R.ShouldBeGreaterThan(hot.B, $"l'arête chaude reste cuivrée en {variant}");
        }
    }

    [AvaloniaFact]
    public void LeVoile_BasculeDeSombreAClair()
    {
        // Seule couche du fond qui dépend du thème : le flou et la désaturation sont pré-composés
        // dans l'asset, le voile est ce qui reste vivant.
        var dark = Resolve("GlassVeil", ThemeVariant.Dark);
        var light = Resolve("GlassVeil", ThemeVariant.Light);

        dark.ShouldNotBe(light);
        (dark.R + dark.G + dark.B).ShouldBeLessThan(light.R + light.G + light.B);
    }

    [AvaloniaFact]
    public void LesElevationsDeVerre_EmbarquentLeSheenInterieur()
    {
        // GlassElev* = ombre portée + arête spéculaire interne en une seule valeur, faute de
        // pouvoir composer deux BoxShadows par référence. Si le sheen disparaît, les vitres
        // perdent leur épaisseur sans que rien d'autre ne le signale.
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            foreach (var key in new[] { "GlassElevItem", "GlassElevMenu" })
            {
                Application.Current!.TryGetResource(key, variant, out var value).ShouldBeTrue();
                var shadows = value.ShouldBeOfType<BoxShadows>();
                shadows.Count.ShouldBe(2);
                var inset = Enumerable.Range(0, shadows.Count).Any(index => shadows[index].IsInset);
                inset.ShouldBeTrue($"{key} en {variant} a perdu son sheen");
            }
        }
    }

    [AvaloniaFact]
    public void LesAssetsDeFond_SontEmbarquesEtRestentLegers()
    {
        // Les fonds sont pré-floutés hors ligne (voir MainWindow.axaml pour la commande) : ce test
        // garde à la fois leur présence — un asset manquant ne se voit qu'à l'exécution, la vue
        // affichant alors un vide — et leur poids, la raison d'être du pré-calcul. Depuis que le
        // fond se choisit, la garde porte sur les ONZE : c'est le total qui compte pour la taille
        // du binaire livré, pas la première image de la liste.
        var total = 0L;

        foreach (var key in BackdropCatalog.Keys)
        {
            var uri = BackdropService.AssetUriFor(key);
            AssetLoader.Exists(uri).ShouldBeTrue($"le fond « {key} » n'est pas embarqué");

            using var stream = AssetLoader.Open(uri);
            stream.Length.ShouldBeLessThan(500 * 1024, $"le fond « {key} » est trop lourd");
            total += stream.Length;

            var bitmap = new Bitmap(AssetLoader.Open(uri));
            bitmap.PixelSize.Width.ShouldBe(1920, $"le fond « {key} » n'est pas au gabarit composé");
            bitmap.PixelSize.Height.ShouldBe(1080, $"le fond « {key} » n'est pas au gabarit composé");
        }

        total.ShouldBeLessThan(2 * 1024 * 1024);

        AssetLoader.Exists(new Uri("avares://Prospect.Desktop/Assets/grain.png")).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void LeFondDeFenetre_EmpileImageVoileEtGrainSousLeShell()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        window.Settle();

        var root = window.GetVisualDescendants().OfType<Panel>()
            .First(panel => panel.GetVisualChildren().OfType<Image>().Any());

        var layers = root.GetVisualChildren().OfType<Control>().ToList();
        layers[0].ShouldBeOfType<Image>();
        layers[1].ShouldBeOfType<Rectangle>();
        layers[2].ShouldBeOfType<Rectangle>().Classes.ShouldContain("grain");

        // Le recouvrement des trois couches est l'empilement lui-même : il doit rester DÉCLARÉ,
        // sinon l'arpenteur d'invariants (LayoutInvariantWalker) le compte comme un défaut — et
        // s'il était déclaré sur le panneau plutôt que sur chaque couche, il dispenserait aussi
        // le shell de la vérification, ce qu'on ne veut pas.
        LayoutContract.GetAllowsOverlap(root).ShouldBeFalse();
        foreach (var layer in layers.Take(3))
        {
            LayoutContract.GetAllowsOverlap(layer).ShouldBeTrue();
        }

        window.Close();
    }

    [AvaloniaFact]
    public void LesTitresDePage_PortentLaProtectionDeTexte()
    {
        // Protection sélective : seuls les titres posés sur la vitre la plus fine la portent.
        // Un DropShadowEffect global coûterait cher pour rien et salirait le texte des dialogues.
        var title = new TextBlock { Text = "Instances", Classes = { "onGlass" } };
        var plain = new TextBlock { Text = "Instances" };
        var window = new Window { Content = new StackPanel { Children = { title, plain } } };
        window.Show();
        window.Settle();

        title.Effect.ShouldBeOfType<DropShadowEffect>();
        plain.Effect.ShouldBeNull();

        window.Close();
    }
}