using Avalonia.Controls;
using Avalonia.Platform;

using Prospect.Core.Storage;

namespace Prospect.Desktop.Windowing;

/// <summary>
/// Les propriétés de décoration que la fenêtre applicative applique, décidées PAR SYSTÈME et
/// séparées de la fenêtre pour rester testables.
/// </summary>
/// <remarks>
/// <para>
/// La recette Windows a été calibrée sur le terrain (défaut n° 30 : deux barres de titre
/// superposées) et n'a pas bougé : zone client étendue, <see cref="ExtendClientAreaChromeHints.NoChrome"/>
/// pour que le système cesse de dessiner la sienne, hauteur de légende alignée sur la nôtre, et
/// <see cref="SystemDecorations.Full"/> pour garder le cadre non client sur lequel s'appuient
/// l'ombre, les poignées de redimensionnement et l'accrochage de fenêtre.
/// </para>
/// <para>
/// Sous Linux, la même recette donne LE MÊME défaut, pour une raison opposée : le hint de chrome
/// est un souhait adressé au gestionnaire de fenêtres, et KWin continue de dessiner sa décoration
/// serveur tant que <see cref="SystemDecorations"/> vaut <see cref="SystemDecorations.Full"/>.
/// D'où <see cref="SystemDecorations.None"/> ici : c'est la seule valeur qui retire la décoration
/// serveur, et elle est tenable parce que notre <c>TitlebarView</c> existe déjà et porte le
/// déplacement et les boutons de fenêtre.
/// </para>
/// <para>
/// Sa contrepartie est le redimensionnement : sans décoration serveur, le gestionnaire de fenêtres
/// n'a plus de bord à attraper, et c'est à l'application de le rendre (voir
/// <c>MainWindow.OnResizeGripPressed</c>, qui appelle <c>Window.BeginResizeDrag</c>). C'est ce que
/// <see cref="NeedsCustomResizeGrips"/> déclare, et c'est pourquoi les deux vont ensemble : retirer
/// la décoration sans poser les poignées donnerait une fenêtre non redimensionnable.
/// </para>
/// </remarks>
/// <param name="UseCustomTitlebar">Notre <c>TitlebarView</c> est visible et porte le déplacement.</param>
/// <param name="ExtendClientAreaToDecorations">La zone client remonte sous la barre de titre.</param>
/// <param name="ChromeHints">Ce qu'on demande à la plateforme de dessiner, ou pas.</param>
/// <param name="TitleBarHeightHint">Hauteur de la zone de légende, ou <c>-1</c> pour la valeur de la plateforme.</param>
/// <param name="SystemDecorations">Décoration native conservée.</param>
/// <param name="NeedsCustomResizeGrips">L'application doit poser ses propres poignées de bord.</param>
public sealed record WindowChromeSettings(
    bool UseCustomTitlebar,
    bool ExtendClientAreaToDecorations,
    ExtendClientAreaChromeHints ChromeHints,
    double TitleBarHeightHint,
    SystemDecorations SystemDecorations,
    bool NeedsCustomResizeGrips)
{
    /// <summary>Hauteur de notre barre de titre, en points. Doit rester égale au jeton <c>TitlebarH</c>.</summary>
    public const double CustomTitleBarHeight = 38d;

    /// <summary>Réglage à appliquer sur <paramref name="operatingSystem"/>.</summary>
    public static WindowChromeSettings For(AppOperatingSystem operatingSystem) => operatingSystem switch
    {
        // Recette validée sous Windows (défaut n° 30), inchangée. macOS la suit : le hint
        // OSXThickTitleBar n'apporte rien tant que nous dessinons toute la barre, et personne n'a
        // encore constaté de double chrome sur une vraie machine mac.
        AppOperatingSystem.Windows or AppOperatingSystem.MacOs => new WindowChromeSettings(
            UseCustomTitlebar: true,
            ExtendClientAreaToDecorations: true,
            ChromeHints: ExtendClientAreaChromeHints.NoChrome,
            TitleBarHeightHint: CustomTitleBarHeight,
            SystemDecorations: SystemDecorations.Full,
            NeedsCustomResizeGrips: false),

        AppOperatingSystem.Linux => new WindowChromeSettings(
            UseCustomTitlebar: true,
            ExtendClientAreaToDecorations: true,
            ChromeHints: ExtendClientAreaChromeHints.NoChrome,
            TitleBarHeightHint: CustomTitleBarHeight,
            SystemDecorations: SystemDecorations.None,
            NeedsCustomResizeGrips: true),

        _ => throw new ArgumentOutOfRangeException(nameof(operatingSystem), operatingSystem, "Système inconnu."),
    };

    /// <summary>Réglage des décorations natives, quand la barre de titre maison est désactivée.</summary>
    /// <remarks>
    /// Aucun système n'emprunte ce chemin aujourd'hui. Il reste écrit et testé parce que c'est le
    /// repli documenté (docs/architecture.md) si un environnement de bureau s'avérait hostile à la
    /// décoration côté client, et qu'un repli non testé n'en est pas un.
    /// </remarks>
    public static WindowChromeSettings Native { get; } = new(
        UseCustomTitlebar: false,
        ExtendClientAreaToDecorations: false,
        ChromeHints: ExtendClientAreaChromeHints.Default,
        TitleBarHeightHint: -1d,
        SystemDecorations: SystemDecorations.Full,
        NeedsCustomResizeGrips: false);
}