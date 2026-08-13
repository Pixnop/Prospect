using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Prospect.Desktop.Controls;

/// <summary>
/// Traduit la molette VERTICALE en défilement HORIZONTAL sur un
/// <see cref="ScrollViewer"/> qui ne défile que dans ce sens.
/// </summary>
/// <remarks>
/// <para>
/// Une souris n'a qu'une molette, et elle est verticale. Un <see cref="ScrollViewer"/> dont le
/// défilement vertical est désactivé n'en fait donc rien : la rangée de catégories du navigateur de
/// mods, longue de plus de deux cents boutons, ne pouvait s'atteindre qu'en attrapant sa barre à la
/// souris. Ce n'est pas un manque de confort, c'est une partie du vocabulaire inatteignable pour qui
/// n'a pas de trackpad horizontal.
/// </para>
/// <para>
/// C'est un COMPORTEMENT ATTACHÉ et non un code-behind, contrairement aux deux dérogations
/// documentées de docs/architecture.md (<c>TitlebarView</c> et <c>ModBrowserView</c>). La raison est
/// que ces deux-là portent une décision propre à LEUR vue — quelles primitives natives de fenêtre
/// appeler, quand demander la tranche suivante de résultats — là où celle-ci n'en porte aucune :
/// c'est une règle d'entrée générique, vraie de n'importe quelle rangée horizontale, qui se déclare
/// dans l'AXAML de la vue et ne laisse pas une ligne de C# derrière elle. Un troisième code-behind
/// aurait fait de l'exception une habitude.
/// </para>
/// <para>
/// L'abonnement est en TUNNEL, c'est-à-dire avant que le <see cref="ScrollViewer"/> ne voie
/// l'évènement. C'est nécessaire : la molette est traitée par le présentateur de contenu, et
/// s'inscrire après lui reviendrait à réagir à un évènement déjà consommé ou déjà sans effet.
/// </para>
/// </remarks>
public static class WheelScroll
{
    /// <summary>
    /// Points de défilement par cran de molette. Un cran vaut 1,0 en delta, et cette valeur donne le
    /// même ordre de grandeur qu'un défilement vertical ordinaire : trois à quatre pastilles de
    /// catégorie par cran.
    /// </summary>
    private const double StepPixels = 64d;

    /// <summary>
    /// Posée à vrai sur un <see cref="ScrollViewer"/>, fait défiler son contenu horizontalement à la
    /// molette.
    /// </summary>
    public static readonly AttachedProperty<bool> HorizontalProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Horizontal", typeof(WheelScroll));

    static WheelScroll()
        => HorizontalProperty.Changed.AddClassHandler<ScrollViewer, bool>(OnHorizontalChanged);

    /// <summary>Lit la déclaration portée par <paramref name="scrollViewer"/>.</summary>
    public static bool GetHorizontal(ScrollViewer scrollViewer)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);

        return scrollViewer.GetValue(HorizontalProperty);
    }

    /// <summary>Pose la déclaration sur <paramref name="scrollViewer"/>.</summary>
    public static void SetHorizontal(ScrollViewer scrollViewer, bool value)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);

        scrollViewer.SetValue(HorizontalProperty, value);
    }

    private static void OnHorizontalChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs<bool> args)
    {
        // Un retrait franc avant toute pose : la propriété peut être réécrite, et deux abonnements
        // feraient défiler deux fois par cran.
        scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);

        if (args.NewValue.GetValueOrDefault())
        {
            scrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        }
    }

    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        // Un trackpad horizontal publie déjà du X : on le laisse passer tel quel plutôt que de le
        // convertir, sans quoi un geste latéral serait compté deux fois.
        var delta = args.Delta.Y != 0 ? args.Delta.Y : args.Delta.X;
        if (delta == 0)
        {
            return;
        }

        var travel = scrollViewer.Extent.Width - scrollViewer.Viewport.Width;
        if (travel <= 0)
        {
            // Tout tient à l'écran : il n'y a rien à faire défiler, et surtout rien à consommer.
            return;
        }

        // Molette vers le HAUT (delta positif) va vers la GAUCHE, comme elle remonte une liste
        // verticale : c'est le même geste, appliqué à l'axe qui existe.
        var offsetX = Math.Clamp(scrollViewer.Offset.X - (delta * StepPixels), 0d, travel);
        if (Math.Abs(offsetX - scrollViewer.Offset.X) < double.Epsilon)
        {
            // Déjà en butée : ne pas marquer l'évènement traité laisse la page défiler à la place,
            // ce qui est exactement ce qu'on attend d'une rangée arrivée au bout.
            return;
        }

        scrollViewer.Offset = scrollViewer.Offset.WithX(offsetX);
        args.Handled = true;
    }
}