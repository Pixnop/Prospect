namespace Prospect.Desktop.ViewModels;

/// <summary>
/// Une page qui entretient un travail de fond TANT QU'ELLE EST AFFICHÉE, et seulement à ce
/// moment-là. Le shell la démarre en y entrant et l'arrête en la quittant
/// (<c>ShellViewModel.Navigate</c>).
/// </summary>
/// <remarks>
/// <para>
/// L'interface existe parce que le shell n'avait aucune notion de « page devenue visible » ni de
/// « page devenue cachée ». L'entrée était traitée au cas par cas, chaque <c>ShowXxx</c> appelant à
/// la main la commande de chargement de sa page ; la sortie n'était traitée que pour les pages
/// jetables, via <see cref="IDisposable"/>.
/// </para>
/// <para>
/// <see cref="IDisposable"/> seul ne pouvait pas suffire, et ça mérite d'être écrit pour que
/// personne ne réessaie : les pages du shell sont des singletons du conteneur, donc la même
/// instance revient à chaque visite. Il faut un verbe pour REPRENDRE, que la fin de vie d'un objet
/// n'a pas. Une page vivante peut parfaitement être aussi jetable — <c>LogsViewModel</c> l'est,
/// parce qu'elle possède un jeton d'annulation — à la condition que sa disposition ne fasse rien de
/// plus que <see cref="StopLiveRefresh"/> et la laisse redémarrable.
/// </para>
/// </remarks>
internal interface ILivePage
{
    /// <summary>Démarre le travail de fond. Appelée à chaque entrée sur la page, y compris répétée.</summary>
    void StartLiveRefresh();

    /// <summary>Arrête le travail de fond. Appelée à chaque sortie, et sans effet si rien ne tourne.</summary>
    void StopLiveRefresh();
}