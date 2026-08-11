using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

using Shouldly;

namespace Prospect.Desktop.Tests;

/// <summary>
/// Vérifie que le système de tokens (couleurs, typographie, icônes) est correctement
/// branché dans les ressources de l'application : présence des clés attendues, bascule
/// de thème effective, résolution réelle des polices embarquées.
/// </summary>
public class DesignTokensTests
{
    [AvaloniaFact]
    public void Copper500_EstUneCouleurCopperAttendue()
    {
        var app = Application.Current!;

        app.TryGetResource("Copper500", ThemeVariant.Dark, out var value).ShouldBeTrue();
        value.ShouldBe(Color.Parse("#C4854F"));
    }

    [AvaloniaFact]
    public void BgSurface_ResoutEnBrushDansLeThemeSombre()
    {
        var app = Application.Current!;

        app.TryGetResource("BgSurface", ThemeVariant.Dark, out var value).ShouldBeTrue();
        var brush = value.ShouldBeOfType<SolidColorBrush>();
        brush.Color.ShouldBe(Color.Parse("#171513"));
    }

    [AvaloniaFact]
    public void ChannelStable_EstPresentDansLesDeuxThemes()
    {
        var app = Application.Current!;

        app.TryGetResource("ChannelStable", ThemeVariant.Dark, out var dark).ShouldBeTrue();
        app.TryGetResource("ChannelStable", ThemeVariant.Light, out var light).ShouldBeTrue();
        dark.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Color.Parse("#5E9C86"));
        light.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Color.Parse("#5E9C86"));
    }

    [AvaloniaFact]
    public void BasculeDeTheme_ChangeReellementUneBrushSemantique()
    {
        var app = Application.Current!;

        app.TryGetResource("BgWindow", ThemeVariant.Dark, out var dark).ShouldBeTrue();
        app.TryGetResource("BgWindow", ThemeVariant.Light, out var light).ShouldBeTrue();

        var darkBrush = dark.ShouldBeOfType<SolidColorBrush>();
        var lightBrush = light.ShouldBeOfType<SolidColorBrush>();

        darkBrush.Color.ShouldBe(Color.Parse("#111010"));
        lightBrush.Color.ShouldBe(Color.Parse("#EFE9E1"));
        darkBrush.Color.ShouldNotBe(lightBrush.Color);
    }

    [AvaloniaFact]
    public void IconPlay_EstUneGeometrieValide()
    {
        var app = Application.Current!;

        app.TryFindResource("Icon.play", out var value).ShouldBeTrue();
        var geometry = value.ShouldBeOfType<StreamGeometry>();
        geometry.Bounds.Width.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void FontSans_SeResoutSurLaPoliceEmbarqueeEtPasSurUnRepli()
    {
        var app = Application.Current!;

        app.TryFindResource("FontSans", out var value).ShouldBeTrue();
        var family = value.ShouldBeOfType<FontFamily>();

        var typeface = new Typeface(family);
        FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface).ShouldBeTrue();
        glyphTypeface.ShouldNotBeNull();
        glyphTypeface.FamilyName.ShouldBe("IBM Plex Sans");
    }

    [AvaloniaFact]
    public void FontMono_SeResoutSurLaPoliceEmbarquee()
    {
        var app = Application.Current!;

        app.TryFindResource("FontMono", out var value).ShouldBeTrue();
        var family = value.ShouldBeOfType<FontFamily>();

        var typeface = new Typeface(family);
        FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface).ShouldBeTrue();
        glyphTypeface.ShouldNotBeNull();
        glyphTypeface.FamilyName.ShouldBe("IBM Plex Mono");
    }

    [AvaloniaFact]
    public void FontDisplay_SeResoutSurLaPoliceEmbarquee()
    {
        var app = Application.Current!;

        app.TryFindResource("FontDisplay", out var value).ShouldBeTrue();
        var family = value.ShouldBeOfType<FontFamily>();

        // Bold est le seul poids Space Grotesk dont le nom interne ("Space Grotesk", sans
        // suffixe) permet une égalité stricte ; Medium et SemiBold portent le nom composé
        // hérité du modèle RIBBI historique ("Space Grotesk Medium"/"...SemiBold"), comme
        // le fait déjà le fichier Medium officiel du dépôt source.
        var typeface = new Typeface(family, weight: FontWeight.Bold);
        FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface).ShouldBeTrue();
        glyphTypeface.ShouldNotBeNull();
        glyphTypeface.FamilyName.ShouldBe("Space Grotesk");
    }
}