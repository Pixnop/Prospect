using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Dialogs;

/// <summary>
/// <see cref="DuplicateDialogViewModel.Dispose"/> doit rester appelable après Cancel/fermeture,
/// et plusieurs fois de suite sans lever : c'est ce que garantit désormais
/// <see cref="OverlayService"/> en disposant le panneau actif à la fermeture (voir
/// OverlayServiceTests), donc ce ViewModel peut se retrouver disposé une première fois par
/// l'overlay lui-même avant qu'un appelant n'y touche à nouveau.
/// </summary>
public class DuplicateDialogViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private static DuplicateDialogViewModel CreateViewModel(IOverlayService overlay)
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(repository, fileSystem, new FakeClock(Now));
        return new DuplicateDialogViewModel("homestead", "Homestead", service, overlay, () => Task.CompletedTask);
    }

    [Fact]
    public void Dispose_NeverConfirmed_DoesNotThrow()
    {
        var viewModel = CreateViewModel(new RecordingOverlayService());

        Should.NotThrow(viewModel.Dispose);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var viewModel = CreateViewModel(new RecordingOverlayService());
        viewModel.Dispose();

        Should.NotThrow(viewModel.Dispose);
    }

    [Fact]
    public void Cancel_ThenManualDispose_DoesNotThrow()
    {
        var overlay = new RecordingOverlayService();
        var viewModel = CreateViewModel(overlay);

        viewModel.CancelCommand.Execute(null);

        Should.NotThrow(viewModel.Dispose);
    }

    [Fact]
    public void Cancel_ClosesRealOverlayAndDisposesViewModel_SecondManualDisposeStaysSafe()
    {
        var overlay = new OverlayService();
        var viewModel = CreateViewModel(overlay);
        overlay.Show(viewModel);

        viewModel.CancelCommand.Execute(null);

        overlay.Active.ShouldBeNull();
        Should.NotThrow(viewModel.Dispose);
    }
}