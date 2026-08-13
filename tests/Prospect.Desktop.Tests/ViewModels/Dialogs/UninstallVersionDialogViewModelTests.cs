using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Dialogs;

public class UninstallVersionDialogViewModelTests
{
    private static UninstallVersionDialogViewModel Create(
        RecordingOverlayService overlay,
        IReadOnlyList<string> dependents,
        Func<IProgress<DirectoryDeleteProgress>, Task>? onConfirm = null)
        => new(
            "1.22.6",
            dependents,
            onConfirm ?? (_ => Task.CompletedTask),
            overlay,
            new ImmediateUiDispatcher());

    [Fact]
    public void NoDependentInstance_ShowsNoWarning()
    {
        var dialog = Create(new RecordingOverlayService(), []);

        dialog.HasDependents.ShouldBeFalse();
        dialog.DependentsMessage.ShouldBeNull();
        dialog.Title.ShouldContain("1.22.6");
        dialog.Message.ShouldContain("1.22.6");
    }

    [Fact]
    public void OneDependentInstance_IsNamedInTheSingular()
    {
        var dialog = Create(new RecordingOverlayService(), ["Homestead"]);

        dialog.HasDependents.ShouldBeTrue();
        dialog.DependentsMessage.ShouldBe("L'instance « Homestead » utilise cette version et ne pourra plus être lancée.");
    }

    [Fact]
    public void SeveralDependentInstances_AreAllNamed()
    {
        var dialog = Create(new RecordingOverlayService(), ["Homestead", "Bac à sable", "Test"]);

        dialog.DependentsMessage.ShouldBe(
            "Les instances « Homestead », « Bac à sable » et « Test » utilisent cette version et ne pourront plus être lancées.");
    }

    [Fact]
    public async Task Confirm_RunsTheUninstall()
    {
        var confirmed = false;
        var dialog = Create(new RecordingOverlayService(), [], _ =>
        {
            confirmed = true;

            return Task.CompletedTask;
        });

        await dialog.ConfirmCommand.ExecuteAsync(null);

        confirmed.ShouldBeTrue();
        dialog.IsBusy.ShouldBeFalse();
    }

    /// <summary>
    /// La barre suit ce que la suppression rapporte, et les boutons sont hors service tant qu'elle
    /// tourne : rien n'est annulable une fois commencé, donc rien ne doit prétendre l'être.
    /// </summary>
    [Fact]
    public async Task WhileUninstalling_TheBarFollowsTheCountAndTheButtonsAreOut()
    {
        var dialog = Create(new RecordingOverlayService(), [], progress =>
        {
            progress.Report(new DirectoryDeleteProgress(0, 200));
            progress.Report(new DirectoryDeleteProgress(50, 200));

            return Task.CompletedTask;
        });

        dialog.ConfirmCommand.CanExecute(null).ShouldBeTrue();
        dialog.CancelCommand.CanExecute(null).ShouldBeTrue();

        await dialog.ConfirmCommand.ExecuteAsync(null);

        dialog.ProgressPercent.ShouldBe(25d);
        dialog.ProgressText.ShouldBe("Suppression des fichiers (50/200)");
    }

    /// <summary>Échec partiel : le dialogue reste ouvert et NOMME le dossier où il reste des fichiers.</summary>
    [Fact]
    public async Task APartialFailure_LeavesAnHonestMessageAndKeepsTheDialogOpen()
    {
        var overlay = new RecordingOverlayService();
        var dialog = Create(overlay, [], _ => throw new DirectoryDeleteFailedException(
            "/data/prospect/versions/1.22.6",
            new IOException("fichier verrouillé")));
        overlay.Show(dialog);

        await dialog.ConfirmCommand.ExecuteAsync(null);

        overlay.Active.ShouldBe(dialog);
        dialog.ErrorMessage.ShouldNotBeNull();
        dialog.ErrorMessage.ShouldContain("/data/prospect/versions/1.22.6");
        dialog.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public void Cancel_ClosesTheOverlayWithoutUninstalling()
    {
        var overlay = new RecordingOverlayService();
        var confirmed = false;
        var dialog = Create(overlay, [], _ =>
        {
            confirmed = true;

            return Task.CompletedTask;
        });
        overlay.Show(dialog);

        dialog.CancelCommand.Execute(null);

        overlay.Active.ShouldBeNull();
        confirmed.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var overlay = new RecordingOverlayService();
        var dispatcher = new ImmediateUiDispatcher();

        Should.Throw<ArgumentNullException>(() => new UninstallVersionDialogViewModel("1.22.6", null!, _ => Task.CompletedTask, overlay, dispatcher));
        Should.Throw<ArgumentNullException>(() => new UninstallVersionDialogViewModel("1.22.6", [], null!, overlay, dispatcher));
        Should.Throw<ArgumentNullException>(() => new UninstallVersionDialogViewModel("1.22.6", [], _ => Task.CompletedTask, null!, dispatcher));
        Should.Throw<ArgumentNullException>(() => new UninstallVersionDialogViewModel("1.22.6", [], _ => Task.CompletedTask, overlay, null!));
    }
}