using System.Windows.Input;

namespace Prospect.Desktop.ViewModels.FirstRun;

/// <summary>
/// Une ligne de la checklist de <see cref="FirstRunScreenViewModel"/> (design :
/// design/ui_kits/launcher/screen-firstrun.jsx, une carte icône + titre + sous-titre + action par
/// ligne). Type immuable reconstruit à chaque rafraîchissement (voir
/// <see cref="FirstRunScreenViewModel.InitializeCommand"/>) plutôt que muté en place : les mêmes
/// raisons que <see cref="Home.InstanceCardViewModel"/> recréées à chaque scan, aucun état propre à
/// faire vivre entre deux lectures des vrais services.
/// </summary>
/// <param name="IconKey">Clé résolue par <see cref="Prospect.Desktop.Converters.IconKeyToGeometryConverter"/>.</param>
/// <param name="Title">Titre de la ligne.</param>
/// <param name="Subtitle">État réel observé (chemin, résumé de détection, etc.), affiché en mono comme la maquette.</param>
/// <param name="IsDone">
/// Vrai si l'étape est déjà satisfaite : la ligne montre alors une coche plutôt qu'un bouton
/// d'action (docs de la mission : « cochée si déjà faite, action directe sinon »).
/// </param>
/// <param name="ActionLabel">Libellé du bouton d'action, <see langword="null"/> quand <paramref name="IsDone"/> est vrai.</param>
/// <param name="ActionCommand">Commande du bouton d'action, <see langword="null"/> quand <paramref name="IsDone"/> est vrai.</param>
public sealed record FirstRunStepViewModel(
    string IconKey,
    string Title,
    string Subtitle,
    bool IsDone,
    string? ActionLabel = null,
    ICommand? ActionCommand = null)
{
    /// <summary>Vrai si la ligne porte un bouton d'action (jamais en même temps qu'<see cref="IsDone"/>).</summary>
    public bool HasAction => ActionCommand is not null;
}