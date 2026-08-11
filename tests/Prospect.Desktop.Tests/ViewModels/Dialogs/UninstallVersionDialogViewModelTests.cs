using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Dialogs;

public class UninstallVersionDialogViewModelTests
{
    private static UninstallVersionDialogViewModel Create(
        RecordingOverlayService overlay,
        IReadOnlyList<string> dependents,
        Func<Task>? onConfirm = null)
        => new("1.22.6", dependents, onConfirm ?? (() => Task.CompletedTask), overlay);

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
        var dialog = Create(new RecordingOverlayService(), [], () =>
        {
            confirmed = true;

            return Task.CompletedTask;
        });

        await dialog.ConfirmCommand.ExecuteAsync(null);

        confirmed.ShouldBeTrue();
        dialog.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public void Cancel_ClosesTheOverlayWithoutUninstalling()
    {
        var overlay = new RecordingOverlayService();
        var confirmed = false;
        var dialog = Create(overlay, [], () =>
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

        Should.Throw<ArgumentNullException>(() => new UninstallVersionDialogViewModel("1.22.6", null!, () => Task.CompletedTask, overlay));
        Should.Throw<ArgumentNullException>(() => new UninstallVersionDialogViewModel("1.22.6", [], null!, overlay));
        Should.Throw<ArgumentNullException>(() => new UninstallVersionDialogViewModel("1.22.6", [], () => Task.CompletedTask, null!));
    }
}