using Avalonia.Headless.XUnit;

using Prospect.Core.ModDb;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Mods;

/// <summary>
/// La fermeture d'une fiche de mod, montée sur le VRAI <see cref="OverlayService"/> et non sur un
/// double : c'est lui qui possède le cycle de vie de ce qu'il affiche, et c'est ce détail-là qui
/// avait échappé aux tests de la refonte fiche/navigateur.
/// </summary>
/// <remarks>
/// Le défaut réel : la fiche appelait <c>Dispose</c> APRÈS <c>Close</c>, alors que <c>Close</c>
/// venait déjà de la disposer. Le second passage rappelait <see cref="CancellationTokenSource.Cancel"/>
/// sur une source déjà libérée, ce qui lève une <see cref="ObjectDisposedException"/> — remontée
/// depuis un gestionnaire de commande, donc fatale pour l'application.
/// </remarks>
public sealed class ModDetailDialogViewModelTests
{
    private const string DescriptionWithImage =
        "<p>Un mod <strong>riche</strong>.</p><p><img src=\"https://example.invalid/capture.png\" alt=\"capture\"></p>";

    private static ModDbModDetail Detail(string descriptionHtml = DescriptionWithImage, bool withLogo = true) => new()
    {
        ModId = 42,
        AssetId = 42,
        Name = "Config lib",
        Author = "Quelqu'un",
        DescriptionHtml = descriptionHtml,
        LogoUrl = withLogo ? new Uri("https://example.invalid/logo.png") : null,
        PageUrl = new Uri("https://mods.vintagestory.at/configlib"),
    };

    private static ModDetailDialogViewModel Open(OverlayService overlay, ModDbModDetail? detail = null)
    {
        var dialog = new ModDetailDialogViewModel(
            detail ?? Detail(),
            target: null,
            new FakeExternalUrlOpener(),
            overlay,
            new FakeModLogoCache(hangUntilCanceled: true),
            () => Task.CompletedTask);

        overlay.Show(dialog);

        return dialog;
    }

    [AvaloniaFact]
    public void Close_WithImagesInFlight_DoesNotThrow()
    {
        var overlay = new OverlayService();
        var dialog = Open(overlay);

        Should.NotThrow(() => dialog.CloseCommand.Execute(null));

        overlay.Active.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Close_LogoWithoutDescription_DoesNotThrow()
    {
        var overlay = new OverlayService();
        var dialog = Open(overlay, Detail(descriptionHtml: string.Empty));

        Should.NotThrow(() => dialog.CloseCommand.Execute(null));

        overlay.Active.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Install_ClosesThenRunsTheInstall_WithoutThrowing()
    {
        var overlay = new OverlayService();
        var installed = false;
        var dialog = new ModDetailDialogViewModel(
            Detail(),
            target: null,
            new FakeExternalUrlOpener(),
            overlay,
            new FakeModLogoCache(hangUntilCanceled: true),
            () =>
            {
                installed = true;

                return Task.CompletedTask;
            });
        overlay.Show(dialog);

        await dialog.InstallCommand.ExecuteAsync(null);

        installed.ShouldBeTrue();
        overlay.Active.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Overlay_ReplacedByAnotherPanel_DisposesTheDetailExactlyOnce()
    {
        var overlay = new OverlayService();
        var dialog = Open(overlay);

        Should.NotThrow(() => overlay.Show(new object()));

        // Une deuxième demande explicite ne doit rien casser non plus : le contrat de Dispose est
        // idempotent, sinon toute frappe deux fois sur « fermer » redevient fatale.
        Should.NotThrow(dialog.Dispose);
    }

    [AvaloniaFact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var document = new RichTextDocumentViewModel(
            HtmlRichTextParser.Parse(DescriptionWithImage),
            new FakeExternalUrlOpener(),
            new FakeModLogoCache(hangUntilCanceled: true),
            imageWidth: 640);

        document.Dispose();

        Should.NotThrow(document.Dispose);
    }
}