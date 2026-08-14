using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// Outillage commun aux parcours de bout en bout : faire vivre la fenêtre, lire ce qui est
/// RÉELLEMENT visible à l'écran, et relire la pile de toasts.
/// </summary>
/// <remarks>
/// La différence avec <see cref="HeadlessTestHelpers"/> est le degré d'exigence. Un test d'écran
/// vérifie qu'un ViewModel a la bonne valeur ; un parcours doit prouver que l'utilisateur VOIT
/// quelque chose, donc il interroge l'arbre visuel et ne se contente jamais d'un booléen de
/// ViewModel comme preuve d'affichage. C'est cette exigence-là qui débusque les états muets.
/// </remarks>
internal static class JourneyHarness
{
    /// <summary>Fait avancer la boucle, force un tick de rendu, puis remet la fenêtre en page.</summary>
    public static void Pump(this Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
    }

    /// <summary>Textes réellement visibles dans la fenêtre, dans l'ordre de l'arbre visuel.</summary>
    public static IReadOnlyList<string> VisibleTexts(this Window window)
    {
        window.Pump();

        return window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
            .Select(block => block.Text!)
            .ToArray();
    }

    /// <summary>
    /// Texte du dictionnaire de langue COURANT pour cette clé.
    /// </summary>
    /// <remarks>
    /// Les parcours interrogent l'écran par ses libellés ; les écrire en dur les rendrait faux dès
    /// qu'un autre test de l'assembly a basculé les dictionnaires en anglais (ils partagent une
    /// seule application Avalonia). Demander la ressource, c'est demander exactement ce que la vue
    /// a affiché.
    /// </remarks>
    public static string ResourceText(string key)
        => Avalonia.Application.Current!.FindResource(key) as string
            ?? throw new KeyNotFoundException($"Clé de texte introuvable : {key}");

    /// <summary>Vrai si un texte visible de la fenêtre contient <paramref name="fragment"/>.</summary>
    public static bool ShowsText(this Window window, string fragment)
        => window.VisibleTexts().Any(text => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>Boutons visibles et actifs, avec leur libellé aplati (contenu texte ou icône + texte).</summary>
    public static IReadOnlyList<Button> EnabledButtons(this Window window)
    {
        window.Pump();

        return window.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled)
            .ToArray();
    }

    /// <summary>Libellé aplati d'un bouton : son contenu texte, ou le premier texte de son contenu.</summary>
    public static string LabelOf(this Button button)
        => button.Content as string
            ?? button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text
            ?? string.Empty;

    /// <summary>Vrai si un bouton visible et actif porte ce libellé (comparaison insensible à la casse).</summary>
    public static bool HasEnabledButton(this Window window, string label)
        => window.EnabledButtons().Any(button => button.LabelOf().Contains(label, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Fait tourner la boucle jusqu'à ce que <paramref name="condition"/> soit vraie, ou jusqu'au
    /// délai imparti.
    /// </summary>
    /// <remarks>
    /// L'attente est RÉELLE (courtes pauses) et non une simple série de <c>Task.Yield</c> : les
    /// chemins d'erreur réseau passent par la politique de réessai du client ModDB, qui attend
    /// vraiment entre deux tentatives. Une boucle sans pause rendrait la main avant que le premier
    /// backoff ne soit écoulé et ferait passer un écran parfaitement bavard pour un écran muet.
    /// </remarks>
    public static async Task WaitUntilAsync(this Window window, Func<bool> condition, string what)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            window.Pump();
            await Task.Delay(20).ConfigureAwait(true);
        }

        window.Pump();
        condition().ShouldBeTrue(what);
    }

    /// <summary>Dernier toast affiché, ou <see langword="null"/> si la pile est vide.</summary>
    public static ToastViewModel? LastToast(this IToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(toasts);

        return toasts.Toasts.Count == 0 ? null : toasts.Toasts[^1];
    }

    /// <summary>Exige un dernier toast de la tonalité attendue et le rend.</summary>
    public static ToastViewModel ShouldHaveToast(this IToastService toasts, ToastTone tone)
    {
        var toast = toasts.LastToast().ShouldNotBeNull("une action aboutie ou échouée doit le dire, jamais se terminer en silence");
        toast.Tone.ShouldBe(tone);

        return toast;
    }
}