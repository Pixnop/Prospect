using Avalonia.Controls;
using Avalonia.Platform;

using Prospect.Core.Storage;
using Prospect.Desktop.Windowing;

using Shouldly;

namespace Prospect.Desktop.Tests.Windowing;

/// <summary>
/// La stratégie de décoration de fenêtre par système. Elle est extraite de la fenêtre justement
/// pour être vérifiable ici : aucun test headless ne peut voir DEUX barres de titre, faute de
/// gestionnaire de fenêtres, donc ce qui se teste est la RÈGLE, et la vérification visuelle reste
/// humaine sur chaque bureau.
/// </summary>
public sealed class WindowChromeSettingsTests
{
    /// <summary>
    /// Le point commun des trois : nous dessinons la barre de titre, et nous demandons à la
    /// plateforme de ne pas dessiner la sienne par-dessus. C'est l'oubli de ce hint qui donnait
    /// deux barres superposées sous Windows (défaut n° 30).
    /// </summary>
    [Theory]
    [InlineData(AppOperatingSystem.Windows)]
    [InlineData(AppOperatingSystem.MacOs)]
    [InlineData(AppOperatingSystem.Linux)]
    public void EveryOperatingSystem_DrawsItsOwnTitlebarAndAsksForNoSystemChrome(AppOperatingSystem operatingSystem)
    {
        var chrome = WindowChromeSettings.For(operatingSystem);

        chrome.UseCustomTitlebar.ShouldBeTrue();
        chrome.ExtendClientAreaToDecorations.ShouldBeTrue();
        chrome.ChromeHints.ShouldBe(ExtendClientAreaChromeHints.NoChrome);
        chrome.TitleBarHeightHint.ShouldBe(WindowChromeSettings.CustomTitleBarHeight);
    }

    /// <summary>
    /// La recette Windows, calibrée sur le terrain, ne bouge pas : Full garde le cadre non client
    /// sur lequel s'appuient l'ombre, les poignées de redimensionnement et l'accrochage. macOS la
    /// suit tant qu'aucun retour d'une vraie machine ne dit le contraire.
    /// </summary>
    [Theory]
    [InlineData(AppOperatingSystem.Windows)]
    [InlineData(AppOperatingSystem.MacOs)]
    public void WindowsAndMacOs_KeepTheirNativeFrameAndItsResizeBehaviour(AppOperatingSystem operatingSystem)
    {
        var chrome = WindowChromeSettings.For(operatingSystem);

        chrome.SystemDecorations.ShouldBe(SystemDecorations.Full);
        chrome.NeedsCustomResizeGrips.ShouldBeFalse();
    }

    /// <summary>
    /// Le correctif Linux. Sous KWin, le hint de chrome n'est qu'un souhait : la décoration serveur
    /// continue d'être dessinée tant qu'il reste un cadre à décorer, d'où la seconde barre de titre
    /// rapportée sur Manjaro/KDE. None la retire, et impose du même coup de rendre nous-mêmes le
    /// redimensionnement.
    /// </summary>
    [Fact]
    public void Linux_DropsTheServerSideDecorationAndTakesOverTheResizing()
    {
        var chrome = WindowChromeSettings.For(AppOperatingSystem.Linux);

        chrome.SystemDecorations.ShouldBe(SystemDecorations.None);
        chrome.NeedsCustomResizeGrips.ShouldBeTrue();
    }

    /// <summary>
    /// Les deux vont ENSEMBLE, et c'est l'invariant qui compte : retirer la décoration serveur sans
    /// poser de poignées donnerait une fenêtre qu'on ne peut plus redimensionner.
    /// </summary>
    [Theory]
    [InlineData(AppOperatingSystem.Windows)]
    [InlineData(AppOperatingSystem.MacOs)]
    [InlineData(AppOperatingSystem.Linux)]
    public void DroppingTheNativeFrame_AlwaysComesWithOurOwnGrips(AppOperatingSystem operatingSystem)
    {
        var chrome = WindowChromeSettings.For(operatingSystem);

        chrome.NeedsCustomResizeGrips.ShouldBe(chrome.SystemDecorations == SystemDecorations.None);
    }

    [Fact]
    public void AnUnknownOperatingSystem_IsRejectedRatherThanGuessed()
        => Should.Throw<ArgumentOutOfRangeException>(() => WindowChromeSettings.For((AppOperatingSystem)99));

    /// <summary>Le repli documenté vers les décorations natives, écrit et testé bien que personne ne l'emprunte.</summary>
    [Fact]
    public void TheNativeFallback_HandsEverythingBackToThePlatform()
    {
        WindowChromeSettings.Native.UseCustomTitlebar.ShouldBeFalse();
        WindowChromeSettings.Native.ExtendClientAreaToDecorations.ShouldBeFalse();
        WindowChromeSettings.Native.ChromeHints.ShouldBe(ExtendClientAreaChromeHints.Default);
        WindowChromeSettings.Native.TitleBarHeightHint.ShouldBe(-1d);
        WindowChromeSettings.Native.SystemDecorations.ShouldBe(SystemDecorations.Full);
        WindowChromeSettings.Native.NeedsCustomResizeGrips.ShouldBeFalse();
    }
}