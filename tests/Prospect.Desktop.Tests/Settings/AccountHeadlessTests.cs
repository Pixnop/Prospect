using System.Text.Json.Nodes;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Auth;
using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Settings;
using Prospect.Desktop.ViewModels.Shell;

using Shouldly;

namespace Prospect.Desktop.Tests.Settings;

/// <summary>
/// Le compte Vintage Story de bout en bout sur le graphe DI réel : se connecter depuis les
/// Réglages, traverser la double authentification, voir la session écrite sur le disque factice
/// avec des permissions restrictives, la retrouver injectée dans le <c>clientsettings.json</c> de
/// l'instance au lancement, puis se déconnecter.
/// </summary>
/// <remarks>
/// Le service de compte est un faux serveur adossé au gestionnaire HTTP du conteneur de test
/// (<see cref="FakeCatalogHandler.Auth"/>) : aucune requête ne quitte la machine, aucun identifiant
/// n'atteint <c>auth3.vintagestory.at</c>. Les identifiants employés ici sont des valeurs de test
/// sur un domaine réservé (<c>.invalid</c>), jamais un vrai compte. La validation en conditions
/// réelles revient au propriétaire du projet, avec son propre compte, dans l'application.
/// </remarks>
public sealed class AccountHeadlessTests
{
    private const string Email = "joueuse@example.invalid";
    private const string Password = "mot-de-passe-de-test";

    private static SettingsAccountsViewModel OpenAccountsTab(ServiceProvider provider, Window window)
    {
        var shell = provider.GetRequiredService<ShellViewModel>();
        shell.SettingsNavItem.SelectCommand.Execute(null);
        shell.Settings.SelectTabCommand.Execute(SettingsTab.Accounts);
        window.Settle();

        return shell.Settings.Accounts;
    }

    [AvaloniaFact]
    public async Task SignIn_SinglePass_ShowsTheConnectedStateAndStoresTheSessionWithOwnerOnlyPermissions()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var handler);
        handler.Auth.Responses = [FakeVsAuthHandler.SuccessJson];
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        var accounts = OpenAccountsTab(provider, window);

        accounts.Email = Email;
        accounts.Password = Password;
        await accounts.SignInCommand.ExecuteAsync(null);
        window.Settle();

        accounts.IsSignedIn.ShouldBeTrue();
        accounts.PlayerName.ShouldBe("Sylve");
        window.GetVisualDescendants().OfType<TextBlock>()
            .ShouldContain(text => text.Text == "Sylve" && text.IsEffectivelyVisible);

        // Le secret est bien passé par le vrai FileSecretStore du conteneur, dans son fichier à
        // part, avec les permissions restrictives posées sur le temporaire comme sur la cible.
        var paths = provider.GetRequiredService<AppPaths>();
        fileSystem.File.Exists(paths.SessionFilePath).ShouldBeTrue();
        var stored = JsonNode.Parse(fileSystem.File.ReadAllText(paths.SessionFilePath))!.AsObject();
        stored["sessionKey"]!.GetValue<string>().ShouldBe("cle-de-session");
        var permissions = provider.GetRequiredService<IUnixFilePermissions>().ShouldBeOfType<RecordingUnixFilePermissions>();
        permissions.Modes[paths.SessionFilePath].ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        permissions.Modes[paths.SessionFilePath + JsonFileStore.TempFileSuffix].ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);

        window.Close();
    }

    [AvaloniaFact]
    public async Task SignIn_TwoPasses_SendsTheContractFieldsAndEndsConnected()
    {
        using var provider = TestServiceProviderFactory.Create(out _, out var handler);
        handler.Auth.Responses = [FakeVsAuthHandler.TwoFactorJson, FakeVsAuthHandler.SuccessJson];
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        var accounts = OpenAccountsTab(provider, window);

        accounts.Email = Email;
        accounts.Password = Password;
        await accounts.SignInCommand.ExecuteAsync(null);
        window.Settle();
        accounts.IsTwoFactorPending.ShouldBeTrue();

        accounts.TwoFactorCode = "123456";
        await accounts.SubmitTwoFactorCommand.ExecuteAsync(null);
        window.Settle();

        accounts.IsSignedIn.ShouldBeTrue();
        accounts.IsTwoFactorPending.ShouldBeFalse();
        handler.Auth.PostedBodies.Count.ShouldBe(2);
        handler.Auth.PostedBodies[0].ShouldContain("totpcode=&");
        handler.Auth.PostedBodies[0].ShouldContain("prelogintoken=");
        handler.Auth.PostedBodies[1].ShouldContain("totpcode=123456");
        handler.Auth.PostedBodies[1].ShouldContain("prelogintoken=jeton-de-pre-connexion");

        window.Close();
    }

    [AvaloniaFact]
    public async Task SignIn_Refused_ShowsANamedMessageAndStoresNothing()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var handler);
        handler.Auth.Responses = [FakeVsAuthHandler.InvalidCredentialsJson];
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        var accounts = OpenAccountsTab(provider, window);

        accounts.Email = Email;
        accounts.Password = Password;
        await accounts.SignInCommand.ExecuteAsync(null);
        window.Settle();

        accounts.IsSignedIn.ShouldBeFalse();
        var message = accounts.ErrorMessage.ShouldNotBeNull();
        window.GetVisualDescendants().OfType<TextBlock>()
            .ShouldContain(text => text.Text == message && text.IsEffectivelyVisible);
        fileSystem.File.Exists(provider.GetRequiredService<AppPaths>().SessionFilePath).ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task SignOut_Confirmed_DeletesTheStoredSessionAndReturnsToTheForm()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var handler);
        handler.Auth.Responses = [FakeVsAuthHandler.SuccessJson];
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        var accounts = OpenAccountsTab(provider, window);
        accounts.Email = Email;
        accounts.Password = Password;
        await accounts.SignInCommand.ExecuteAsync(null);
        window.Settle();

        accounts.SignOutCommand.Execute(null);
        window.Settle();
        var dialog = shell.Overlay.Active.ShouldBeOfType<SignOutDialogViewModel>();
        await dialog.ConfirmCommand.ExecuteAsync(null);
        window.Settle();

        accounts.IsSignedIn.ShouldBeFalse();
        shell.Overlay.Active.ShouldBeNull();
        fileSystem.File.Exists(provider.GetRequiredService<AppPaths>().SessionFilePath).ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task Launch_WithASignedInAccount_InjectsTheSessionIntoTheInstanceDataPath()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var handler);
        handler.Auth.Responses = [FakeVsAuthHandler.SuccessJson];
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");
        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        var accounts = OpenAccountsTab(provider, window);
        accounts.Email = Email;
        accounts.Password = Password;
        await accounts.SignInCommand.ExecuteAsync(null);
        window.Settle();

        var dataDirectory = provider.GetRequiredService<IInstanceRepository>().GetDataDirectory(slug);
        var settingsPath = fileSystem.Path.Combine(dataDirectory, ClientSettingsSessionWriter.FileName);
        fileSystem.File.Exists(settingsPath).ShouldBeFalse();

        // Le lanceur est reconstruit avec les pièces du conteneur SAUF trois. Le lanceur de
        // processus, parce que celui de production démarrerait un vrai binaire sur la machine qui
        // exécute la suite (même raison qui interdit de cliquer IExternalUrlOpener ici, voir
        // SettingsHeadlessTests). La stratégie de lancement, parce que celle du conteneur dépend de
        // l'OS courant et que macOS refuse de lancer quoi que ce soit : ce test parle d'injection de
        // session, pas de la matrice de plateformes, qui a ses propres tests. Et le détecteur de
        // runtime, qui irait interroger le vrai dotnet de la machine. Le service de compte, le
        // stockage de session et l'écrivain de clientsettings, eux, sont bien ceux du graphe réel.
        var launcher = new GameLauncher(
            provider.GetRequiredService<IInstanceRepository>(),
            provider.GetRequiredService<IInstalledGameVersionRepository>(),
            new FakeDotnetLocator(),
            provider.GetRequiredService<RunningInstanceTracker>(),
            new LinuxGameLaunchStrategy(fileSystem),
            new FakeProcessRunner(),
            fileSystem,
            provider.GetRequiredService<AppPaths>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<VsAccountService>(),
            provider.GetRequiredService<ClientSettingsSessionWriter>(),
            provider.GetRequiredService<InstanceBackupService>());

        await launcher.LaunchAsync(slug);

        var stringSettings = JsonNode.Parse(fileSystem.File.ReadAllText(settingsPath))!.AsObject()["stringSettings"]!.AsObject();
        stringSettings["sessionkey"]!.GetValue<string>().ShouldBe("cle-de-session");
        stringSettings["playername"]!.GetValue<string>().ShouldBe("Sylve");
        stringSettings["useremail"]!.GetValue<string>().ShouldBe(Email);

        window.Close();
    }

    [AvaloniaFact]
    public async Task FirstRunChecklist_AccountStep_ReflectsTheRealStateAndLeadsToTheAccountsTab()
    {
        using var provider = TestServiceProviderFactory.Create(out _, out var handler);
        handler.Auth.Responses = [FakeVsAuthHandler.SuccessJson];
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowFirstRunIfNeeded();
        window.Settle();
        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        var accountStep = firstRun.Steps[2];
        accountStep.IsDone.ShouldBeFalse();
        accountStep.ActionCommand.ShouldBeSameAs(firstRun.GoToAccountSettingsCommand);

        // L'action referme le panneau et emmène directement sur Réglages > Comptes.
        await firstRun.GoToAccountSettingsCommand.ExecuteAsync(null);
        window.Settle();
        shell.Overlay.Active.ShouldBeNull();
        shell.CurrentPage.ShouldBeSameAs(shell.Settings);
        shell.Settings.SelectedTab.ShouldBe(SettingsTab.Accounts);

        // Une fois connecté, la même checklist rejouée montre la ligne cochée : c'est bien l'état
        // réel du service qu'elle lit, pas un texte figé.
        var accounts = shell.Settings.Accounts;
        accounts.Email = Email;
        accounts.Password = Password;
        await accounts.SignInCommand.ExecuteAsync(null);
        await firstRun.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        firstRun.Steps[2].IsDone.ShouldBeTrue();
        firstRun.Steps[2].Subtitle.ShouldContain("Sylve");

        window.Close();
    }
}