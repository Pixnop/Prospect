using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Un fond proposé par la grille de vignettes de Réglages &gt; Général : sa clé stable
/// (<see cref="Prospect.Core.Settings.BackdropCatalog"/>), son nom traduit, son état de sélection
/// et sa vignette.
/// </summary>
/// <remarks>
/// <para>
/// Observable, à la différence de <see cref="Prospect.Desktop.ViewModels.Wizard.IconChoiceOption"/>
/// qui est immuable et dont le wizard reconstruit la liste entière à chaque clic. La raison est le
/// clavier : reconstruire la source d'éléments détruit les boutons, donc le focus, et une grille
/// qu'on ne peut plus parcourir à la flèche après la première sélection ne serait navigable qu'à la
/// souris. Ici seuls deux <see cref="IsSelected"/> basculent, l'arbre visuel ne bouge pas.
/// </para>
/// <para>
/// La vignette est décodée à la PREMIÈRE lecture, jamais à la construction : ce ViewModel doit
/// rester constructible sans application graphique (docs/architecture.md, MVVM strict), or décoder
/// une image demande la plateforme de rendu. Les tests qui n'affichent rien ne la touchent jamais.
/// </para>
/// </remarks>
public sealed partial class BackdropChoiceOption : ObservableObject
{
    private readonly Func<string, IImage> _thumbnailFactory;

    private IImage? _thumbnail;

    public BackdropChoiceOption(
        string key,
        string label,
        bool isSelected,
        Func<string, IImage> thumbnailFactory,
        Action<string> select)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(thumbnailFactory);
        ArgumentNullException.ThrowIfNull(select);

        Key = key;
        Label = label;
        _isSelected = isSelected;
        _thumbnailFactory = thumbnailFactory;
        SelectCommand = new RelayCommand(() => select(key));
    }

    /// <summary>Clé stable persistée dans <c>prospect.json</c>.</summary>
    public string Key { get; }

    /// <summary>Nom affiché sous la vignette, traduit (voir <c>UiText.Settings.BackdropLabel</c>).</summary>
    public string Label { get; }

    /// <summary>Vignette réduite du fond, décodée à la demande par <c>BackdropService</c>.</summary>
    public IImage Thumbnail => _thumbnail ??= _thumbnailFactory(Key);

    /// <summary>Sélectionne ce fond (vignette cliquée, ou activée au clavier).</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>Vrai pour le fond en vigueur : c'est lui que la vue marque du liseré cuivre.</summary>
    [ObservableProperty]
    private bool _isSelected;
}