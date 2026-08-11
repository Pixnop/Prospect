using Prospect.Core.Instances;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.ViewModels.Home;

/// <summary>
/// Une ligne de la zone discrète des instances cassées de l'Accueil (dossier + raison en
/// français, jamais la trace technique de <see cref="BrokenInstance.Detail"/> qui est réservée
/// aux journaux, voir sa docstring).
/// </summary>
public sealed class BrokenInstanceRowViewModel(BrokenInstance broken)
{
    public string FolderName { get; } = System.IO.Path.GetFileName(broken.DirectoryPath.TrimEnd('/', '\\'));

    public string Reason { get; } = UiText.BrokenInstances.Reason(broken.Reason);
}