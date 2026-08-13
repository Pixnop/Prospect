using System.Globalization;

using Prospect.Core.Diagnostics;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Core.Runtime;
using Prospect.Core.Settings;

namespace Prospect.Desktop.Resources;

/// <summary>
/// Table anglaise. Écrite comme un ORIGINAL et non comme un décalque du français : mêmes règles de
/// voix (adresse directe au joueur, casse de phrase, aucun jargon technique dans un message
/// destiné à être lu), mais des tournures anglaises. Les guillemets suivent la convention
/// anglaise (« … » devient “ … ”), les nombres la convention anglaise (1,234 et non 1 234), les
/// dates absolues l'ordre anglais. Ne se traduisent pas : noms propres (Prospect, Vintage Story,
/// ModDB, Anego Studios), chemins, noms de fichiers et formats techniques.
/// </summary>
internal sealed class EnglishUiText : UiTextTable
{
    private readonly EnglishModsText _mods = new();

    public EnglishUiText() => Instance = new EnglishInstanceText(new EnglishInstanceBackupsText(), new EnglishDoctorText(_mods));

    internal override string Language => ProspectSettings.English;

    internal override ShellText Shell { get; } = new EnglishShellText();

    internal override WizardText Wizard { get; } = new EnglishWizardText();

    internal override DialogsText Dialogs { get; } = new EnglishDialogsText();

    internal override ToastsText Toasts { get; } = new EnglishToastsText();

    internal override HomeText Home { get; } = new EnglishHomeText();

    internal override DownloadsText Downloads { get; } = new EnglishDownloadsText();

    internal override VersionsText Versions { get; } = new EnglishVersionsText();

    internal override BrokenInstancesText BrokenInstances { get; } = new EnglishBrokenInstancesText();

    internal override InstanceText Instance { get; }

    internal override AccountText Account { get; } = new EnglishAccountText();

    internal override ModsText Mods => _mods;


    internal override MigrationText Migration { get; } = new EnglishMigrationText();

    internal override SettingsText Settings { get; } = new EnglishSettingsText();

    internal override FirstRunText FirstRun { get; } = new EnglishFirstRunText();

    internal override TimeText Time { get; } = new EnglishTimeText();

    internal override LogsText Logs { get; } = new EnglishLogsText();
}

internal sealed class EnglishShellText : ShellText
{
    internal override string NavHome => "Home";

    internal override string NavMods => "Mods";

    internal override string NavVersions => "Versions";

    internal override string NavLogs => "Logs";

    internal override string NavSettings => "Settings";
}

internal sealed class EnglishWizardText : WizardText
{
    internal override string NameRequired => "The instance needs a name.";

    internal override string VersionInstalled => "installed";

    internal override string InstallCanceled => "Install canceled. The instance was not created.";

    internal override string SummaryNoVersion => "Choose a game version at the previous step.";

    internal override string NameBeingDeleted
        => "An instance with this name is being deleted. Wait for it to finish, or pick another name.";

    internal override IReadOnlyList<string> StepLabels { get; } = ["Name", "Version", "Icon", "Summary"];

    internal override string IconLabel(string iconChoiceKey) => iconChoiceKey switch
    {
        "package" => "Crate",
        "star" => "Star",
        "hard-drive" => "Drive",
        "image" => "Image",
        _ => "Default",
    };

    internal override string VersionToDownload(string displaySize) => $"{displaySize} to download";

    internal override string SummaryAlreadyInstalled(string version)
        => $"Version {version} is already installed, nothing to download. The instance will be ready right away.";

    internal override string SummaryWillDownload(string version)
        => $"Version {version} will be downloaded and installed before the instance is created.";
}

internal sealed class EnglishDialogsText : DialogsText
{
    internal override string RenameEmptyError => "The instance needs a name.";

    internal override string DuplicateEmptyError => "The copy needs a name.";

    internal override string DeleteBackupMessage
        => "This backup will be deleted for good. The instance's other backups are left alone.";

    internal override string DuplicateSuggestedName(string sourceName) => $"{sourceName} (copy)";

    internal override string DuplicateProgressLabel(int filesCopied, int totalFiles)
        => totalFiles == 0 ? "Getting the copy ready…" : $"Copying files ({filesCopied}/{totalFiles})";

    internal override string DeleteTitle(string instanceName) => $"Delete “{instanceName}”?";

    internal override string DeleteMessage(string instanceName)
        => $"Everything in “{instanceName}” will be deleted for good, worlds and mods included. This cannot be undone.";

    internal override string DeleteInProgress => "Deleting… This can take a while on an instance with large worlds.";

    internal override string DeleteProgress(int deletedFiles, int totalFiles)
        => totalFiles == 0 ? DeleteInProgress : $"Deleting files ({deletedFiles}/{totalFiles})";

    internal override string DeletePartialFailure(string directory)
        => $"The deletion could not finish: files are left in “{directory}”. Close the game if it is still running, then try again.";

    internal override string RestoreBackupTitle(string instanceName) => $"Restore “{instanceName}”?";

    internal override string RestoreBackupMessage(string instanceName, string dateText)
        => $"The current state of “{instanceName}” will be backed up first, to be safe, then replaced by the backup from {dateText}. Worlds, configs and mods will go back to exactly that state.";

    internal override string DeleteBackupTitle(string dateText) => $"Delete the backup from {dateText}?";
}

internal sealed class EnglishToastsText : ToastsText
{
    internal override string InstanceCreatedTitle => "Instance created";

    internal override string InstanceRenamedTitle => "Instance renamed";

    internal override string InstanceDuplicatedTitle => "Instance duplicated";

    internal override string InstanceDeletedTitle => "Instance deleted";

    internal override string VersionInstalledTitle => "Version installed";

    internal override string VersionUninstalledTitle => "Version uninstalled";

    internal override string LaunchSettingsSavedTitle => "Launch settings saved";


    internal override string LogsExportedTitle => "Logs exported";

    internal override string BackupCreatedTitle => "Backup created";

    internal override string BackupRestoredTitle => "Backup restored";

    internal override string BackupDeletedTitle => "Backup deleted";

    internal override string AutoBackupFailedTitle => "Automatic backup failed";

    internal override string AutoBackupFailedMessage
        => "The launch carries on, but nothing was backed up first. Check how much disk space is left.";
}

internal sealed class EnglishHomeText : HomeText
{
    internal override string NoSearchResults(string query) => $"No instance matches “{query}”.";

    internal override string UpdatesBadge(int count) => count == 1 ? "1 update" : $"{count} updates";
}

internal sealed class EnglishLogsText : LogsText
{
    internal override string Subtitle(int shownLines, int fileCount)
    {
        var lines = shownLines switch
        {
            0 => "no lines",
            1 => "1 line shown",
            _ => $"last {shownLines} lines",
        };

        var files = fileCount switch
        {
            0 => "no log to export",
            1 => "1 log to export",
            _ => $"{fileCount} logs to export",
        };

        return $"{lines} · {files}";
    }

    internal override string ExportPickerTitle => "Export logs";

    internal override string ExportFileName => "prospect-logs.zip";

    internal override string ExportedToastDescription(int fileCount) => fileCount switch
    {
        0 => "No log to take along: the archive is empty.",
        1 => "1 log in the archive.",
        _ => $"{fileCount} logs in the archive.",
    };
}

internal sealed class EnglishDownloadsText : DownloadsText
{
    internal override string Queued => "queued";

    internal override string Verifying => "checking the checksum";

    internal override string GenericFailure => "The download failed.";

    internal override string Summary(int running, int queued) => (running, queued) switch
    {
        (0, 0) => string.Empty,
        (_, 0) => $"{running} running",
        (0, _) => $"{queued} queued",
        _ => $"{running} running · {queued} queued",
    };

    internal override string OutcomeCompleted => "done";

    internal override string OutcomeFailed => "failed";

    internal override string OutcomeCanceled => "canceled";
}

internal sealed class EnglishVersionsText : VersionsText
{
    internal override string StaleCatalog
        => "The catalog could not be refreshed. The versions shown come from the last reading.";

    internal override string UnavailableCatalog
        => "The catalog is unreachable. Only versions already installed are shown.";

    internal override string InstallLandedElsewhere(string targetDirectory)
        => $"The installer finished without an error, but the game is not in “{targetDirectory}”. "
            + "It was most likely installed over an existing Vintage Story install. "
            + "Nothing was marked as installed.";

    internal override string ArchiveMissingExecutable(string targetDirectory)
        => "The game archive was extracted without an error, but it did not carry the expected executable "
            + $"where it was expected in “{targetDirectory}”. Nothing was marked as installed.";

    internal override string Subtitle(int installedCount, string totalSize) => installedCount switch
    {
        0 => "No version installed · folder shared by every instance",
        1 => $"1 installed · {totalSize} · folder shared by every instance",
        _ => $"{installedCount} installed · {totalSize} · folder shared by every instance",
    };

    internal override string InstalledOn(DateTimeOffset installedUtc)
        => $"installed on {installedUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    internal override string PhaseLabel(GameInstallPhase phase) => phase switch
    {
        GameInstallPhase.Downloading => "Downloading",
        GameInstallPhase.Verifying => "Checking",
        GameInstallPhase.Installing => "Installing",
        GameInstallPhase.Completed => "Done",
        _ => string.Empty,
    };

    // Le séparateur et le format du pourcentage suivent DownloadDetail, dans les deux langues ;
    // seul l'espace avant le signe change, parce que le français en met un et pas l'anglais.
    internal override string InstallDetail(int percent)
        => $"extracting · {percent.ToString(CultureInfo.InvariantCulture)}%";

    internal override string InstallEstimateDetail(int percent)
        => $"installing · ~{percent.ToString(CultureInfo.InvariantCulture)}%";

    internal override string InstallerPromptNotice
        => "The game's own installer may open a window offering to uninstall an older version. Answer no: Prospect installs into its own folder and leaves your existing game alone.";

    internal override string BrokenReason(GameInstallBrokenReason reason) => reason switch
    {
        GameInstallBrokenReason.MissingCompletionMarker => "install cut short, needs reinstalling",
        GameInstallBrokenReason.UnreadableVersionName => "unreadable folder name",
        _ => "unknown reason",
    };

    internal override string UninstallTitle(string version) => $"Uninstall version {version}?";

    internal override string UninstallMessage(string version)
        => $"The files of version {version} will be removed from the shared folder. You can install it again from the catalog.";

    internal override string UninstallInProgress => "Uninstalling…";

    internal override string UninstallProgress(int deletedFiles, int totalFiles)
        => totalFiles == 0 ? UninstallInProgress : $"Deleting files ({deletedFiles}/{totalFiles})";

    internal override string UninstallPartialFailure(string directory)
        => $"The uninstall could not finish: files are left in “{directory}”. Close the game if it is still running, then try again.";

    internal override string UninstallDependents(IReadOnlyList<string> instanceNames)
    {
        var quoted = instanceNames.Select(name => $"“{name}”").ToArray();
        var joined = quoted.Length == 1
            ? quoted[0]
            : $"{string.Join(", ", quoted[..^1])} and {quoted[^1]}";

        return quoted.Length == 1
            ? $"Instance {joined} uses this version and will no longer start."
            : $"Instances {joined} use this version and will no longer start.";
    }
}

internal sealed class EnglishBrokenInstancesText : BrokenInstancesText
{
    internal override string Reason(InstanceBrokenReason reason) => reason switch
    {
        InstanceBrokenReason.MissingMetadataFile => "instance.json missing",
        InstanceBrokenReason.CorruptedMetadataFile => "instance.json unreadable",
        InstanceBrokenReason.UnsupportedSchemaVersion => "schema version not supported",
        _ => "unknown reason",
    };
}

internal sealed class EnglishInstanceText(InstanceBackupsText backups, DoctorText doctor) : InstanceText(backups, doctor)
{
    internal override string VersionNotInstalledTitle => "Version not installed";

    internal override string RuntimeMissingTitle => ".NET runtime missing";

    internal override string MacNotSupportedTitle => "macOS not supported";

    internal override string AlreadyRunningTitle => "Session already running";

    internal override string GenericLaunchErrorTitle => "Cannot launch";

    internal override string EnvVarsInvalidLine => "Every line must read KEY=value.";

    internal override string StopConfirmTitle(string instanceName) => $"Stop “{instanceName}”?";

    internal override string StopConfirmMessage(string instanceName)
        => $"The game running for “{instanceName}” will stop right away. Any unsaved progress will be lost.";
}

internal sealed class EnglishInstanceBackupsText : InstanceBackupsText
{
    internal override string CreateFailedTitle => "Cannot back up";

    internal override string KeepCountChoiceLabel(int count)
        => count == 1 ? "1 backup kept" : $"{count} backups kept";

    internal override string CreateProgress(int filesProcessed, int totalFiles)
        => totalFiles == 0 ? "Getting the backup ready…" : $"Backing up ({filesProcessed}/{totalFiles})";

    internal override string AutoBackupProgress(int filesProcessed, int totalFiles)
        => totalFiles == 0 ? "Automatic backup…" : $"Automatic backup ({filesProcessed}/{totalFiles})…";
}

internal sealed class EnglishDoctorText(ModsText mods) : DoctorText(mods)
{
    internal override string InstallAction => "Install";

    internal override string ReinstallAction => "Reinstall";

    internal override string OpenModsAction => "See the mods";

    internal override string InstallDependencyAction(string modIdString) => $"Install “{modIdString}”…";

    internal override string UpdateDependencyAction(string modIdString) => $"Update “{modIdString}”…";

    internal override string AllClearTitle => "All clear";

    internal override string AllClearDescription => "Nothing wrong across the five local checks.";

    internal override string CompatibilityUnknown
        => "Game version compatibility unknown: check for mod updates to find out more.";

    internal override string RuntimeIndeterminate
        => "The runtime this version needs could not be worked out. Launching will confirm it if need be.";

    internal override string ErrorsGroupTitle(int count) => count == 1 ? "1 issue to fix" : $"{count} issues to fix";

    internal override string WarningsGroupTitle(int count) => count == 1 ? "1 issue to watch" : $"{count} issues to watch";

    internal override string GameVersionMessage(GameVersionDoctorResult result) => result.Status switch
    {
        GameVersionDoctorStatus.Missing => $"Version {result.Version} is not installed.",
        GameVersionDoctorStatus.Incomplete
            => $"The install of version {result.Version} is incomplete, most likely cut short along the way.",
        _ => string.Empty,
    };

    internal override string RuntimeMessage(RuntimeCheckResult runtime) => runtime.Availability switch
    {
        RuntimeAvailability.Missing
            => $"The .NET runtime {runtime.Requirement.FrameworkName} {runtime.Requirement.Version} is required but is not installed.",
        RuntimeAvailability.Indeterminate => RuntimeIndeterminate,
        _ => string.Empty,
    };

    internal override string CompatibilityMessage(ModCompatibilityDoctorResult compatibility, string gameVersionText)
    {
        if (compatibility.Severity != InstanceDoctorSeverity.Warning)
        {
            return string.Empty;
        }

        if (compatibility.IsWhollyUnknown)
        {
            return CompatibilityUnknown;
        }

        var uncertain = compatibility.ApproximateCount + compatibility.UnknownCount;

        return uncertain == 1
            ? $"1 mod whose compatibility with {gameVersionText} is not confirmed. Check for mod updates to find out more."
            : $"{uncertain} mods whose compatibility with {gameVersionText} is not confirmed. Check for mod updates to find out more.";
    }

    internal override string DiskSpaceLow(string availableText)
        => $"Low disk space: {availableText} left on the volume Prospect lives on.";

    protected override string UnidentifiedMessage(string modDisplayName, ModInfoProblem problem)
        => $"“{modDisplayName}” could not be identified ({Mods.UnidentifiedReason(problem)}).";

    protected override string DependencyIssueMessage(string modDisplayName, ModDependencyIssue dependency) => dependency.Status switch
    {
        ModDependencyStatus.Missing => $"“{modDisplayName}” needs {dependency.ModIdString}, which is not in the instance.",
        ModDependencyStatus.Disabled => $"“{modDisplayName}” needs {dependency.ModIdString}, which is there but disabled.",
        ModDependencyStatus.TooOld
            => $"“{modDisplayName}” needs {dependency.ModIdString} {dependency.Requirement} at the very least, and the installed version is older.",
        _ => $"“{modDisplayName}” needs {dependency.ModIdString}.",
    };
}

internal sealed class EnglishAccountText : AccountText
{
    internal override string InvalidCredentials => "Wrong email or password.";

    internal override string InvalidTwoFactorCode
        => "That code is not the right one. It changes every thirty seconds, type in the latest one shown.";

    internal override string Refused
        => "The sign-in was refused. Check your account on vintagestory.at, then try again.";

    internal override string ServiceUnavailable
        => "The Vintage Story account service is not answering. Check your connection and try again.";

    internal override string UnknownPlayerName => "this account";

    internal override string SignOutConfirmMessage
        => "Your session will be erased from this computer. The game will start without multiplayer until you sign in again.";

    internal override string SignOutConfirmTitle(string playerName) => $"Sign “{playerName}” out?";

    internal override string SignedInSubtitle(string playerName) => $"Signed in as {playerName}.";
}

internal sealed class EnglishModsText : ModsText
{
    internal override string AllVersions => "All versions";

    internal override string UnknownVersion => "unknown version";

    internal override string ProvenanceManual => "manual";

    internal override string EmptyResultsTitle => "No mod matches";

    internal override string EnabledTitle => "Mod enabled";

    internal override string DisabledTitle => "Mod disabled";

    internal override string UninstalledTitle => "Mod removed";

    internal override string FileGoneTitle => "File not found";

    internal override string InstallFailedTitle => "Cannot install";

    internal override string NoCompatibleReleaseTitle => "No compatible release";

    internal override string DetailUnavailableTitle => "Page unavailable";

    internal override string PickInstanceTitle => "Choose an instance";

    internal override string PickInstanceMessage => "Pick the instance to install into before you add a mod.";

    internal override string InstallAction => "Install";

    internal override string ManageAction => "Manage";

    internal override string InstalledBadge(string? version)
        => string.IsNullOrWhiteSpace(version) ? "Installed" : $"Installed · {version}";

    internal override string StaleCatalog
        => "The index could not be refreshed. The mods shown come from the last reading.";

    internal override string OfflineEmptyTitle => "No offline results";

    internal override string OfflineEmptyDescription
        => "The mod index is cached locally, but that cache has expired. Reconnect to browse ModDB.";

    internal override string CheckUpdatesFailedTitle => "Cannot check";

    internal override string UpdateFailedTitle => "Cannot update";

    internal override string LinkSourceCode => "Source code";

    internal override string LinkIssues => "Issues";

    internal override string LinkWiki => "Wiki";

    internal override string LinkHomepage => "Mod website";

    internal override string ApproximateReleaseTag => "assumed compatibility";

    internal override string IncompatibleReleaseTag => "not declared compatible";

    internal override string ShowAllReleases => "Show all versions";

    internal override string ShowCompatibleReleasesOnly => "Show compatible ones only";

    internal override string ReleaseChoiceCount(int count) => count switch
    {
        0 => "no compatible version",
        1 => "1 compatible version",
        _ => $"{FormatCount(count)} compatible versions",
    };

    internal override string IncompatibleReleaseWarning(string gameVersion, IReadOnlyList<string> taggedVersions)
        => taggedVersions.Count == 0
            ? $"The author declared this release compatible with no game version at all. It may well run on {gameVersion}, but nothing says so."
            : $"The author declared this release for {CompatibleVersions(taggedVersions)}, not for {gameVersion}. Those boxes are ticked by hand and fall behind, so it may work — at your own risk.";

    internal override string InstallAnywayReason(IReadOnlyList<string> taggedVersions)
        => taggedVersions.Count == 0
            ? "no declared game version"
            : $"declared for {CompatibleVersions(taggedVersions)}";

    protected override CultureInfo NumberCulture { get; } = CultureInfo.GetCultureInfo("en-US");

    internal override string Subtitle(int indexedCount) => indexedCount switch
    {
        0 => "Official ModDB",
        1 => "Official ModDB · 1 mod indexed",
        _ => $"Official ModDB · {FormatCount(indexedCount)} mods indexed",
    };

    internal override string ShownCount(int shown, int total)
        => shown >= total
            ? total switch
            {
                0 => string.Empty,
                1 => "1 mod",
                _ => $"{FormatCount(total)} mods",
            }
            : $"{FormatCount(shown)} of {FormatCount(total)} mods shown";

    internal override string ByAuthor(string author)
        => string.IsNullOrWhiteSpace(author) ? "unknown author" : $"by {author}";

    internal override string EmptyResultsDescription(string query)
        => string.IsNullOrWhiteSpace(query)
            ? "No mod matches the active filters."
            : $"No mod matches “{query.Trim()}”.";

    internal override string DetailMeta(string author, int downloads)
        => $"{ByAuthor(author)} · {FormatCount(downloads)} downloads";

    internal override string CompatibleVersions(IReadOnlyList<string> tags) => tags.Count switch
    {
        0 => "no game version declared",
        1 => tags[0],
        <= 3 => string.Join(", ", tags),
        _ => $"{string.Join(", ", tags.Take(3))} and {tags.Count - 3} more",
    };

    internal override string SideLabel(ModDbSide side) => side switch
    {
        ModDbSide.Client => "client",
        ModDbSide.Server => "server",
        ModDbSide.Both => "client and server",
        _ => string.Empty,
    };

    internal override string SideLabel(ModSide? side) => side switch
    {
        ModSide.Client => "client",
        ModSide.Server => "server",
        ModSide.Universal => "universal",
        _ => string.Empty,
    };

    internal override string RowAuthor(IReadOnlyList<string> authors) => authors.Count switch
    {
        0 => "unknown author",
        1 => $"by {authors[0]}",
        _ => $"by {authors[0]} and {authors.Count - 1} other{(authors.Count > 2 ? "s" : string.Empty)}",
    };

    internal override string UnidentifiedReason(ModInfoProblem problem) => problem switch
    {
        ModInfoProblem.MissingModInfo => "no modinfo.json in the archive",
        ModInfoProblem.MalformedJson => "modinfo.json unreadable",
        ModInfoProblem.MissingIdentity => "modinfo.json with neither id nor name",
        ModInfoProblem.UnreadableArchive => "unreadable archive",
        _ => string.Empty,
    };

    internal override string InstalledSummary(int total, int enabled) => total switch
    {
        0 => string.Empty,
        1 => enabled == 1 ? "1 mod installed" : "1 mod installed, disabled",
        _ => enabled == total ? $"{total} mods installed" : $"{total} mods installed · {total - enabled} disabled",
    };

    internal override string PlanTitle(string modName) => $"Install “{modName}”?";

    internal override string PlanMessage(string version, string instanceName)
        => $"Version {version} will be added to “{instanceName}”.";

    internal override string ReplacePlanTitle(string modName) => $"Replace “{modName}”?";

    internal override string ReplacePlanMessage(string currentVersion, string version, string instanceName)
        => string.IsNullOrEmpty(currentVersion)
            ? $"The copy already installed in “{instanceName}” will be replaced by version {version}. Whether it is enabled or disabled is kept."
            : $"Version {currentVersion} installed in “{instanceName}” will be replaced by version {version}. Whether it is enabled or disabled is kept.";

    internal override string ApproximateWarning(string gameVersion)
        => $"No release declares itself compatible with {gameVersion}: this one is offered because it targets the same series. The author has not confirmed it, so it may not work.";

    internal override string DependencyReason(ModDependencyIssue? issue) => issue?.Status switch
    {
        ModDependencyStatus.Missing when issue.ReportedByModDb && issue.Requirement.IsAny => "reported by ModDB",
        ModDependencyStatus.Missing => "not in the instance",
        ModDependencyStatus.TooOld => $"installed version too old, {issue.Requirement} at the very least",
        _ => string.Empty,
    };

    internal override string DependenciesNotOnModDb(IReadOnlyList<string> identifiers)
    {
        if (identifiers.Count == 0)
        {
            return string.Empty;
        }

        var pronoun = identifiers.Count > 1 ? "them" : "it";

        return $"Not found on ModDB: {string.Join(", ", identifiers)}. Install {pronoun} by hand if the mod turns out to need {pronoun}.";
    }

    internal override string DependenciesWithoutCompatibleRelease(IReadOnlyList<string> names, string gameVersion)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        var author = names.Count > 1 ? "Their authors have" : "Its author has";

        return $"On ModDB, but with no release for Vintage Story {gameVersion}: {string.Join(", ", names)}. "
            + $"{author} published nothing for this game version. Those compatibility boxes are ticked by hand and fall behind, "
            + "so you can still install the latest published release, knowing what you are doing.";
    }

    internal override string DisabledDependencies(IReadOnlyList<string> identifiers)
    {
        if (identifiers.Count == 0)
        {
            return string.Empty;
        }

        var pronoun = identifiers.Count > 1 ? "them" : "it";

        return $"There but disabled: {string.Join(", ", identifiers)}. Turn {pronoun} back on from the instance's Mods tab.";
    }

    internal override string InstalledTitle(string modName) => $"{modName} installed";

    internal override string InstalledMessage(int count, string instanceName)
        => count > 1 ? $"{count} mods added to “{instanceName}”" : $"Added to “{instanceName}”";

    internal override string UninstallTitle(string modName) => $"Remove “{modName}”?";

    internal override string UninstallMessage(string fileName)
        => $"The file {fileName} will be deleted from the instance's Mods folder. You can install it again from ModDB.";

    internal override string UninstallDependents(IReadOnlyList<string> modNames)
    {
        if (modNames.Count == 0)
        {
            return string.Empty;
        }

        var quoted = modNames.Select(name => $"“{name}”").ToArray();
        var joined = quoted.Length == 1
            ? quoted[0]
            : $"{string.Join(", ", quoted[..^1])} and {quoted[^1]}";

        return quoted.Length == 1
            ? $"The mod {joined} depends on it and may stop working."
            : $"The mods {joined} depend on it and may stop working.";
    }

    internal override string LastCheckedLabel(string relativeCheckedText) => $"Last checked: {relativeCheckedText}";

    internal override string UpdatesAvailableTitle(int count) => count switch
    {
        0 => string.Empty,
        1 => "1 update available",
        _ => $"{count} updates available",
    };

    internal override string CheckVerdict(int updateCount, int undeclaredCount, int modCount)
    {
        if (modCount == 0)
        {
            return "No mod to check.";
        }

        var found = (updateCount, undeclaredCount) switch
        {
            (0, 0) => "everything is up to date",
            (0, 1) => "1 newer release exists, not declared for your game version",
            (0, _) => $"{undeclaredCount} newer releases exist, not declared for your game version",
            (1, 0) => "1 update available",
            (_, 0) => $"{updateCount} updates available",
            (1, 1) => "1 update available, and 1 newer release not declared",
            (1, _) => $"1 update available, and {undeclaredCount} newer releases not declared",
            (_, 1) => $"{updateCount} updates available, and 1 newer release not declared",
            _ => $"{updateCount} updates available, and {undeclaredCount} newer releases not declared",
        };

        return modCount == 1 ? $"1 mod checked: {found}." : $"{modCount} mods checked: {found}.";
    }

    internal override string UndeclaredUpdateReason(string version, IReadOnlyList<string> taggedVersions)
        => $"{version} is published, declared for {CompatibleVersions(taggedVersions)}";

    internal override string UpdatePlanTitle(string modName) => $"Update “{modName}”?";

    internal override string UpdatePlanMessage(string currentVersion, string targetVersion)
        => $"Version {currentVersion} will be replaced by {targetVersion}.";

    internal override string UpdateDependentsNote(IReadOnlyList<string> modNames)
    {
        if (modNames.Count == 0)
        {
            return string.Empty;
        }

        var quoted = modNames.Select(name => $"“{name}”").ToArray();
        var joined = quoted.Length == 1
            ? quoted[0]
            : $"{string.Join(", ", quoted[..^1])} and {quoted[^1]}";

        return quoted.Length == 1
            ? $"{joined} depends on this mod."
            : $"{joined} depend on this mod.";
    }

    internal override string UpdatedTitle(string modName) => $"{modName} updated";

    internal override string UpdatedMessage(string targetVersion) => $"Version {targetVersion} installed.";

    internal override string BulkUpdateDoneTitle(int count) => count switch
    {
        0 => "No update applied",
        1 => "1 mod updated",
        _ => $"{count} mods updated",
    };

    internal override string BulkUpdateFailures(IReadOnlyList<BulkUpdateFailure> failures)
        => failures.Count == 0
            ? string.Empty
            : $"Failed for {string.Join(", ", failures.Select(failure => failure.ModName))}.";
}


internal sealed class EnglishMigrationText : MigrationText
{
    internal override string Starting => "Getting ready…";

    internal override string CompletedToastTitle => "Adoption done";

    internal override string ModCount(int count) => count switch
    {
        0 => "no mods",
        1 => "1 mod",
        _ => $"{count} mods",
    };

    internal override string DetectionSummary(int installationCount, int gameVersionCount)
    {
        var installations = installationCount switch
        {
            0 => "no installs",
            1 => "1 install",
            _ => $"{installationCount} installs",
        };

        var engines = gameVersionCount switch
        {
            0 => "no engines",
            1 => "1 engine",
            _ => $"{gameVersionCount} engines",
        };

        return $"{installations} and {engines} found";
    }

    internal override string AdoptingInstallationsPhase(int completedItems, int totalItems, string? currentItemLabel)
        => string.IsNullOrEmpty(currentItemLabel)
            ? $"Installs {completedItems}/{totalItems}"
            : $"Installs {completedItems}/{totalItems} · {currentItemLabel}";

    internal override string AdoptingEnginesPhase(int completedItems, int totalItems, string? currentItemLabel)
        => string.IsNullOrEmpty(currentItemLabel)
            ? $"Engines {completedItems}/{totalItems}"
            : $"Engines {completedItems}/{totalItems} · {currentItemLabel}";

    internal override string FilesCopied(int filesCopied, int totalFiles) => $"{filesCopied}/{totalFiles} files";

    internal override string CompletedToastDescription(int adoptedInstallations, int adoptedEngines)
        => (adoptedInstallations, adoptedEngines) switch
        {
            (0, 0) => "Nothing was adopted, see the report.",
            (_, 0) => adoptedInstallations == 1 ? "1 instance created." : $"{adoptedInstallations} instances created.",
            (0, _) => adoptedEngines == 1 ? "1 engine adopted." : $"{adoptedEngines} engines adopted.",
            _ => $"{adoptedInstallations} instance(s) created, {adoptedEngines} engine(s) adopted.",
        };

    internal override string InstallationsAdoptedGroupTitle(int count)
        => count == 1 ? "1 instance created" : $"{count} instances created";

    internal override string InstallationsSkippedGroupTitle(int count)
        => count == 1 ? "1 install skipped" : $"{count} installs skipped";

    internal override string InstallationsFailedGroupTitle(int count)
        => count == 1 ? "1 install failed" : $"{count} installs failed";

    internal override string EnginesAdoptedGroupTitle(int count)
        => count == 1 ? "1 engine adopted" : $"{count} engines adopted";

    internal override string EnginesSkippedGroupTitle(int count)
        => count == 1 ? "1 engine skipped" : $"{count} engines skipped";

    internal override string EnginesFailedGroupTitle(int count)
        => count == 1 ? "1 engine failed" : $"{count} engines failed";
}

internal sealed class EnglishSettingsText : SettingsText
{
    internal override string PickFolderTitle => "VS Launcher folder";

    internal override string VslNotDetected => "Nothing usable was found at that location.";

    internal override string ConcurrencyChoiceLabel(int count)
        => count == 1 ? "1 download at a time" : $"{count} downloads at once";

    internal override string BackdropLabel(string backdropKey) => backdropKey switch
    {
        "ruins-clearing" => "Ruin clearing",
        "sunlit-hills" => "Sunlit hills",
        "village-lane" => "Village lane",
        "lake-sail" => "Lake sail",
        "dusk-reeds" => "Reeds at dusk",
        "misty-yard" => "Misty yard",
        "reading-room" => "Reading room",
        "stone-cellar" => "Stone cellar",
        "overgrown-ruin" => "Overgrown ruin",
        "crystal-vein" => "Crystal vein",
        _ => "Turquoise pools",
    };
}

internal sealed class EnglishFirstRunText : FirstRunText
{
    internal override string DataFolderTitle => "Data folder";

    internal override string GameVersionTitle => "Game version";

    internal override string VslDetectedTitle => "VS Launcher installs found";

    internal override string AccountTitle => "Vintage Story account";

    internal override string AccountSignedOut => "Optional: only useful for playing multiplayer.";

    internal override string AccountSignInAction => "Sign in";

    internal override string InstallVersionAction => "Install";

    internal override string AdoptAction => "Adopt";

    internal override string NoVersionInstalled => "none installed";

    internal override string InstalledVersionsSummary(int count, string mostRecentVersion) => count switch
    {
        1 => $"{mostRecentVersion} installed",
        _ => $"{count} versions installed, {mostRecentVersion} among them",
    };
}

internal sealed class EnglishTimeText : TimeText
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    internal override string Never => "never";

    internal override string Today => "today";

    internal override string Yesterday => "yesterday";

    internal override string NeverPlayed => "never played";

    internal override string PlayedUnderAnHour => "played < 1 h";

    internal override string DaysAgo(int days) => $"{days} days ago";

    internal override string JustNow => "just now";

    internal override string MinutesAgo(int minutes) => minutes <= 1 ? "1 minute ago" : $"{minutes} minutes ago";

    internal override string HoursAgo(int hours) => hours <= 1 ? "1 hour ago" : $"{hours} hours ago";

    internal override string AbsoluteDate(DateTime utcValue) => utcValue.ToString("MMMM d, yyyy", English);

    internal override string PlayedHours(long hours) => $"played {hours} h";
}