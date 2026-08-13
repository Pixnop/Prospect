using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Diagnostics;
using Prospect.Core.ModDb;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>Une ligne du rapport du docteur d'instance, avec l'action éventuelle qu'elle propose.</summary>
public sealed class InstanceDoctorRowViewModel
{
    public InstanceDoctorRowViewModel(InstanceDoctorSeverity severity, string message, string? actionLabel = null, IRelayCommand? actionCommand = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        Severity = severity;
        Message = message;
        ActionLabel = actionLabel ?? string.Empty;
        ActionCommand = actionCommand;
    }

    public InstanceDoctorSeverity Severity { get; }

    public string Message { get; }

    /// <summary>Libellé du bouton d'action, vide quand <see cref="HasAction"/> est faux.</summary>
    public string ActionLabel { get; }

    public IRelayCommand? ActionCommand { get; }

    public bool HasAction => ActionCommand is not null;

    public bool IsError => Severity == InstanceDoctorSeverity.Error;
}

/// <summary>Un groupe du rapport (erreurs, puis avertissements), avec ses lignes.</summary>
public sealed class InstanceDoctorGroupViewModel
{
    public InstanceDoctorGroupViewModel(string title, IReadOnlyList<InstanceDoctorRowViewModel> rows)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(rows);

        Title = title;
        Rows = rows;
    }

    public string Title { get; }

    public IReadOnlyList<InstanceDoctorRowViewModel> Rows { get; }
}

/// <summary>
/// Dialogue de rapport du docteur d'instance (design : panneau modal groupé par sévérité, erreurs
/// d'abord, ouvert depuis le menu du header de la page de détail via
/// <c>InstanceDetailViewModel.CheckInstanceCommand</c>) : présente un <see cref="InstanceDoctorReport"/>
/// déjà calculé, jamais de diagnostic ici — c'est <see cref="InstanceDoctor"/>, entièrement local et
/// hors ligne, qui en a la charge (docs/architecture.md, séparation logique/interface).
/// </summary>
public sealed partial class InstanceDoctorDialogViewModel : ObservableObject
{
    private readonly IOverlayService _overlay;

    /// <summary>Construit le dialogue à partir d'un rapport déjà calculé.</summary>
    /// <param name="report">Rapport produit par <see cref="InstanceDoctor.DiagnoseAsync"/>.</param>
    /// <param name="navigateToVersions">Ferme ce dialogue et navigue vers l'écran Versions (action Installer/Réinstaller).</param>
    /// <param name="openModsTab">Ferme ce dialogue et sélectionne l'onglet Mods de la page de détail.</param>
    /// <param name="installMod">
    /// Ferme ce dialogue et ouvre le plan d'installation du mod dont on donne le <c>modid</c>.
    /// C'est ici, et seulement ici, que le réseau entre en jeu : le rapport lui-même a été produit
    /// hors ligne par <see cref="InstanceDoctor"/>, qui n'a aucun moyen d'appeler quoi que ce soit.
    /// </param>
    /// <param name="overlay">Panneau modal, pour se refermer (bouton Fermer).</param>
    public InstanceDoctorDialogViewModel(
        InstanceDoctorReport report,
        Action navigateToVersions,
        Action openModsTab,
        Func<string, Task> installMod,
        IOverlayService overlay)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(navigateToVersions);
        ArgumentNullException.ThrowIfNull(openModsTab);
        ArgumentNullException.ThrowIfNull(installMod);
        ArgumentNullException.ThrowIfNull(overlay);

        _overlay = overlay;

        IsAllClear = report.IsAllClear;
        Groups = BuildGroups(report, new RelayCommand(navigateToVersions), new RelayCommand(openModsTab), installMod);
    }

    /// <summary>Vrai si les cinq vérifications sont saines : l'état gratifiant (mais sobre) du dialogue.</summary>
    public bool IsAllClear { get; }

    /// <summary>Erreurs puis avertissements, chacun son groupe ; vide quand <see cref="IsAllClear"/> est vrai.</summary>
    public IReadOnlyList<InstanceDoctorGroupViewModel> Groups { get; }

    [RelayCommand]
    private void Close() => _overlay.Close();

    private static List<InstanceDoctorGroupViewModel> BuildGroups(
        InstanceDoctorReport report,
        IRelayCommand navigateToVersions,
        IRelayCommand openModsTab,
        Func<string, Task> installMod)
    {
        var rows = new List<InstanceDoctorRowViewModel>();

        AddGameVersionRow(rows, report.GameVersion, navigateToVersions);
        AddRuntimeRow(rows, report);
        AddModIssueRows(rows, report.ModIssues, openModsTab, installMod);
        AddCompatibilityRow(rows, report.ModCompatibility, report.GameVersion.Version.ToString(), openModsTab);
        AddDiskSpaceRow(rows, report.DiskSpace);

        // Un seul groupe par sévérité, omis s'il est vide : pas de section « Erreurs » qui
        // s'affiche vide quand tout va bien côté vérifications 1/2/5 mais qu'un mod traîne un
        // avertissement.
        var groups = new List<InstanceDoctorGroupViewModel>();
        AddGroup(groups, rows, InstanceDoctorSeverity.Error, UiText.Instance.Doctor.ErrorsGroupTitle);
        AddGroup(groups, rows, InstanceDoctorSeverity.Warning, UiText.Instance.Doctor.WarningsGroupTitle);

        return groups;
    }

    private static void AddGroup(
        List<InstanceDoctorGroupViewModel> groups,
        List<InstanceDoctorRowViewModel> rows,
        InstanceDoctorSeverity severity,
        Func<int, string> title)
    {
        var matching = rows.Where(row => row.Severity == severity).ToArray();
        if (matching.Length > 0)
        {
            groups.Add(new InstanceDoctorGroupViewModel(title(matching.Length), matching));
        }
    }

    private static void AddGameVersionRow(List<InstanceDoctorRowViewModel> rows, GameVersionDoctorResult result, IRelayCommand navigateToVersions)
    {
        if (result.Severity == InstanceDoctorSeverity.Ok)
        {
            return;
        }

        var actionLabel = result.Status == GameVersionDoctorStatus.Missing
            ? UiText.Instance.Doctor.InstallAction
            : UiText.Instance.Doctor.ReinstallAction;

        rows.Add(new InstanceDoctorRowViewModel(result.Severity, UiText.Instance.Doctor.GameVersionMessage(result), actionLabel, navigateToVersions));
    }

    // La sévérité vient de report.Findings plutôt que d'être recalculée ici : InstanceDoctorReport
    // (côté Core, testé) en est la seule source de vérité, ce docteur ne fait que la relire.
    private static void AddRuntimeRow(List<InstanceDoctorRowViewModel> rows, InstanceDoctorReport report)
    {
        var severity = report.Findings.First(finding => finding.Check == InstanceDoctorCheck.Runtime).Severity;
        if (severity == InstanceDoctorSeverity.Ok)
        {
            return;
        }

        rows.Add(new InstanceDoctorRowViewModel(severity, UiText.Instance.Doctor.RuntimeMessage(report.Runtime)));
    }

    /// <summary>
    /// Une ligne par constat, avec l'action qui répond VRAIMENT au constat.
    /// </summary>
    /// <remarks>
    /// « Voir les mods » sur une dépendance manquante renvoyait l'utilisateur regarder une liste où
    /// le mod manquant, par définition, n'est pas. Les deux cas où l'on peut agir tout de suite
    /// mènent donc au plan d'installation : la dépendance absente s'installe, celle qui est trop
    /// vieille se met à jour (même plan, la fiche remplace le fichier existant). Le cas
    /// « désactivée » garde « Voir les mods », qui est exactement l'endroit où la réactiver.
    /// </remarks>
    private static void AddModIssueRows(
        List<InstanceDoctorRowViewModel> rows,
        IReadOnlyList<ModDoctorIssue> issues,
        IRelayCommand openModsTab,
        Func<string, Task> installMod)
    {
        foreach (var issue in issues)
        {
            var (label, command) = ResolveModIssueAction(issue, openModsTab, installMod);
            rows.Add(new InstanceDoctorRowViewModel(issue.Severity, UiText.Instance.Doctor.ModIssueMessage(issue), label, command));
        }
    }

    private static (string Label, IRelayCommand Command) ResolveModIssueAction(
        ModDoctorIssue issue,
        IRelayCommand openModsTab,
        Func<string, Task> installMod)
    {
        if (issue.Dependency is not { } dependency)
        {
            return (UiText.Instance.Doctor.OpenModsAction, openModsTab);
        }

        return dependency.Status switch
        {
            ModDependencyStatus.Missing => (
                UiText.Instance.Doctor.InstallDependencyAction(dependency.ModIdString),
                new AsyncRelayCommand(() => installMod(dependency.ModIdString))),

            ModDependencyStatus.TooOld => (
                UiText.Instance.Doctor.UpdateDependencyAction(dependency.ModIdString),
                new AsyncRelayCommand(() => installMod(dependency.ModIdString))),

            _ => (UiText.Instance.Doctor.OpenModsAction, openModsTab),
        };
    }

    private static void AddCompatibilityRow(
        List<InstanceDoctorRowViewModel> rows,
        ModCompatibilityDoctorResult compatibility,
        string gameVersionText,
        IRelayCommand openModsTab)
    {
        if (compatibility.Severity == InstanceDoctorSeverity.Ok)
        {
            return;
        }

        rows.Add(new InstanceDoctorRowViewModel(
            compatibility.Severity,
            UiText.Instance.Doctor.CompatibilityMessage(compatibility, gameVersionText),
            UiText.Instance.Doctor.OpenModsAction,
            openModsTab));
    }

    private static void AddDiskSpaceRow(List<InstanceDoctorRowViewModel> rows, DiskSpaceDoctorResult diskSpace)
    {
        if (diskSpace.Severity == InstanceDoctorSeverity.Ok)
        {
            return;
        }

        rows.Add(new InstanceDoctorRowViewModel(
            diskSpace.Severity,
            UiText.Instance.Doctor.DiskSpaceLow(ByteSizeFormatter.Format(diskSpace.AvailableBytes))));
    }
}