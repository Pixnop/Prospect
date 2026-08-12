using Avalonia;
using Avalonia.Controls;

namespace Prospect.Desktop.Layout;

/// <summary>
/// Registre des superpositions volontaires de la mise en page. Par défaut, deux enfants d'un même
/// panneau qui se recouvrent sont un défaut : c'est exactement la classe de bug signalée en test
/// réel (« des textes dépassent, des chevauchements »), et l'arpenteur d'invariants des tests
/// (<c>LayoutInvariantWalker</c>) la refuse écran par écran, à chaque taille de fenêtre.
///
/// Quelques superpositions sont pourtant l'intention même du dessin : un calque modal posé sur la
/// page, un badge posé sur une bande d'art, une icône posée dans un champ de saisie. Elles se
/// déclarent ici, dans l'AXAML, avec un commentaire d'une ligne qui dit pourquoi — de sorte que
/// la liste des recouvrements admis soit lisible dans le code de la vue plutôt que devinée par
/// une heuristique dans le harnais de test.
///
/// Deux portées, selon ce qui est le plus juste à dire :
/// <list type="bullet">
///   <item>posée sur le PANNEAU, elle admet que tous ses enfants se recouvrent entre eux (calque
///   d'empilement assumé, par exemple la racine du shell) ;</item>
///   <item>posée sur un ENFANT, elle n'admet que les recouvrements impliquant cet enfant-là (le
///   reste de la fratrie continue d'être vérifié).</item>
/// </list>
/// </summary>
public static class LayoutContract
{
    /// <summary>
    /// Déclare que ce contrôle (ou, posée sur un panneau, sa descendance directe) a le droit de
    /// recouvrir ses frères. Voir la documentation de <see cref="LayoutContract"/> pour la portée.
    /// </summary>
    public static readonly AttachedProperty<bool> AllowsOverlapProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("AllowsOverlap", typeof(LayoutContract));

    /// <summary>Lit la déclaration de superposition portée par <paramref name="control"/>.</summary>
    public static bool GetAllowsOverlap(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return control.GetValue(AllowsOverlapProperty);
    }

    /// <summary>Pose la déclaration de superposition sur <paramref name="control"/>.</summary>
    public static void SetAllowsOverlap(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);

        control.SetValue(AllowsOverlapProperty, value);
    }
}