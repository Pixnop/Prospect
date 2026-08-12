using System.Net;
using System.Net.Http;

using Prospect.Core.Auth;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Settings;

/// <summary>
/// La section Comptes des Réglages, construite à la main sur un service de compte dont le réseau
/// est entièrement simulé : rien ne part de la machine, aucun identifiant n'est réel. Ce qui est
/// vérifié ici, ce sont les trois états de l'écran (déconnecté, deuxième passe, connecté), les
/// messages nommés par cas, et la discipline du mot de passe — le champ ne garde jamais rien après
/// coup.
/// </summary>
public sealed class SettingsAccountsViewModelTests
{
    private const string Email = "joueuse@example.invalid";
    private const string Password = "mot-de-passe-de-test";

    private const string SuccessBody = """
    {
      "valid": 1,
      "sessionkey": "cle-de-session",
      "sessionsignature": "signature-de-session",
      "mptoken": "jeton-multijoueur",
      "uid": "3f2b8e14",
      "entitlements": "singleplayer",
      "playername": "Sylve",
      "hasgameserver": false
    }
    """;

    private const string TwoFactorBody = """
    { "valid": 0, "reason": "requiretotpcode", "prelogintoken": "jeton-de-pre-connexion" }
    """;

    private sealed record Fixture(
        SettingsAccountsViewModel ViewModel,
        VsAccountService Accounts,
        MemorySecretStore Store,
        RecordingOverlayService Overlay,
        List<string> Bodies);

    private static Fixture CreateFixture(params string[] responses)
    {
        var bodies = new List<string>();
        var index = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult());
            var body = responses.Length == 0 ? SuccessBody : responses[Math.Min(index, responses.Length - 1)];
            index++;

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });

        var store = new MemorySecretStore();
        var accounts = new VsAccountService(new VsAccountClient(new HttpClient(handler, disposeHandler: false)), store);
        var overlay = new RecordingOverlayService();

        return new Fixture(new SettingsAccountsViewModel(accounts, overlay), accounts, store, overlay, bodies);
    }

    private static async Task SignInAsync(Fixture fixture)
    {
        fixture.ViewModel.Email = Email;
        fixture.ViewModel.Password = Password;
        await fixture.ViewModel.SignInCommand.ExecuteAsync(null);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var accounts = new VsAccountService(new VsAccountClient(new HttpClient()), new MemorySecretStore());

        Should.Throw<ArgumentNullException>(() => new SettingsAccountsViewModel(null!, new RecordingOverlayService()));
        Should.Throw<ArgumentNullException>(() => new SettingsAccountsViewModel(accounts, null!));
    }

    [Fact]
    public void NewViewModel_ShowsTheSignedOutForm()
    {
        var fixture = CreateFixture();

        fixture.ViewModel.IsSignedIn.ShouldBeFalse();
        fixture.ViewModel.IsTwoFactorPending.ShouldBeFalse();
        fixture.ViewModel.ErrorMessage.ShouldBeNull();
        fixture.ViewModel.Email.ShouldBeEmpty();
        fixture.ViewModel.Password.ShouldBeEmpty();
    }

    [Fact]
    public async Task NewViewModel_ServiceAlreadySignedIn_ShowsTheConnectedState()
    {
        // Cas du démarrage réel : App relit la session avant de construire la première fenêtre.
        var store = new MemorySecretStore
        {
            Stored = new VsSession
            {
                Email = Email,
                PlayerName = "Sylve",
                PlayerUid = "3f2b8e14",
                Entitlements = "singleplayer",
                SessionKey = "cle",
                SessionSignature = "signature",
                MpToken = "jeton",
                HostGameServer = "false",
            },
        };
        var accounts = new VsAccountService(new VsAccountClient(new HttpClient()), store);
        await accounts.LoadAsync();

        var viewModel = new SettingsAccountsViewModel(accounts, new RecordingOverlayService());

        viewModel.IsSignedIn.ShouldBeTrue();
        viewModel.PlayerName.ShouldBe("Sylve");
        viewModel.PlayerUid.ShouldBe("3f2b8e14");
    }

    [Fact]
    public void SignInCommand_EmptyFields_CannotExecute()
    {
        var fixture = CreateFixture();

        fixture.ViewModel.SignInCommand.CanExecute(null).ShouldBeFalse();

        fixture.ViewModel.Email = Email;
        fixture.ViewModel.SignInCommand.CanExecute(null).ShouldBeFalse();

        fixture.ViewModel.Password = Password;
        fixture.ViewModel.SignInCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task SignInCommand_SinglePassSuccess_SwitchesToTheConnectedStateAndForgetsThePassword()
    {
        var fixture = CreateFixture(SuccessBody);

        await SignInAsync(fixture);

        fixture.ViewModel.IsSignedIn.ShouldBeTrue();
        fixture.ViewModel.PlayerName.ShouldBe("Sylve");
        fixture.ViewModel.PlayerUid.ShouldBe("3f2b8e14");
        fixture.ViewModel.Password.ShouldBeEmpty();
        fixture.ViewModel.ErrorMessage.ShouldBeNull();
        fixture.ViewModel.IsBusy.ShouldBeFalse();
        fixture.Store.Stored.ShouldNotBeNull();
    }

    [Fact]
    public async Task SignInCommand_TwoFactorRequired_OpensTheCodeStepAndKeepsWhatTheSecondPassNeeds()
    {
        var fixture = CreateFixture(TwoFactorBody);

        await SignInAsync(fixture);

        fixture.ViewModel.IsTwoFactorPending.ShouldBeTrue();
        fixture.ViewModel.IsSignedIn.ShouldBeFalse();
        fixture.ViewModel.ErrorMessage.ShouldBeNull();
        // Seul moment où le mot de passe survit à l'appel : la deuxième passe le redemande au
        // service, qui ne l'a pas gardé non plus.
        fixture.ViewModel.Password.ShouldBe(Password);
    }

    [Fact]
    public async Task SubmitTwoFactorCommand_ValidCode_SignsInAndClearsEverythingSensitive()
    {
        var fixture = CreateFixture(TwoFactorBody, SuccessBody);
        await SignInAsync(fixture);

        fixture.ViewModel.TwoFactorCode = "123456";
        await fixture.ViewModel.SubmitTwoFactorCommand.ExecuteAsync(null);

        fixture.ViewModel.IsSignedIn.ShouldBeTrue();
        fixture.ViewModel.IsTwoFactorPending.ShouldBeFalse();
        fixture.ViewModel.Password.ShouldBeEmpty();
        fixture.ViewModel.TwoFactorCode.ShouldBeEmpty();
        fixture.Bodies[1].ShouldContain("prelogintoken=jeton-de-pre-connexion");
        fixture.Bodies[1].ShouldContain("totpcode=123456");
    }

    [Fact]
    public async Task SubmitTwoFactorCommand_WrongCode_NamesThatCaseAndStaysOnTheCodeStep()
    {
        var fixture = CreateFixture(TwoFactorBody, """{ "valid": 0, "reason": "wrongtotpcode" }""");
        await SignInAsync(fixture);

        fixture.ViewModel.TwoFactorCode = "000000";
        await fixture.ViewModel.SubmitTwoFactorCommand.ExecuteAsync(null);

        fixture.ViewModel.IsTwoFactorPending.ShouldBeTrue();
        fixture.ViewModel.IsSignedIn.ShouldBeFalse();
        fixture.ViewModel.ErrorMessage.ShouldNotBeNull();
        fixture.ViewModel.ErrorMessage!.ShouldContain("code");
        fixture.ViewModel.TwoFactorCode.ShouldBeEmpty();
    }

    [Fact]
    public void SubmitTwoFactorCommand_NoCodeTyped_CannotExecute()
    {
        var fixture = CreateFixture();

        fixture.ViewModel.SubmitTwoFactorCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task CancelTwoFactorCommand_ReturnsToTheFormAndDropsThePassword()
    {
        var fixture = CreateFixture(TwoFactorBody);
        await SignInAsync(fixture);

        fixture.ViewModel.CancelTwoFactorCommand.Execute(null);

        fixture.ViewModel.IsTwoFactorPending.ShouldBeFalse();
        fixture.ViewModel.Password.ShouldBeEmpty();
        fixture.ViewModel.TwoFactorCode.ShouldBeEmpty();
        fixture.ViewModel.Email.ShouldBe(Email);
    }

    [Fact]
    public async Task SignInCommand_InvalidCredentials_NamesThatCaseWithoutApiJargon()
    {
        var fixture = CreateFixture("""{ "valid": 0, "reason": "invalidemailorpassword" }""");

        await SignInAsync(fixture);

        fixture.ViewModel.IsSignedIn.ShouldBeFalse();
        var message = fixture.ViewModel.ErrorMessage.ShouldNotBeNull();
        message.ShouldNotContain("invalidemailorpassword");
        message.ShouldNotContain("valid");
        message.ShouldNotContain("HTTP");
        fixture.ViewModel.Password.ShouldBeEmpty();
        fixture.ViewModel.Email.ShouldBe(Email);
    }

    [Fact]
    public async Task SignInCommand_UnnamedRefusal_StillSaysSomethingUsable()
    {
        var fixture = CreateFixture("""{ "valid": 0, "reason": "accountlocked" }""");

        await SignInAsync(fixture);

        var message = fixture.ViewModel.ErrorMessage.ShouldNotBeNull();
        message.ShouldNotBeEmpty();
        message.ShouldNotContain("accountlocked");
    }

    [Fact]
    public async Task SignInCommand_ServiceUnreachable_SaysSoWithoutLeakingTheException()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("Connection refused 203.0.113.7"));
        var accounts = new VsAccountService(
            new VsAccountClient(new HttpClient(handler, disposeHandler: false)),
            new MemorySecretStore());
        var viewModel = new SettingsAccountsViewModel(accounts, new RecordingOverlayService())
        {
            Email = Email,
            Password = Password,
        };

        await viewModel.SignInCommand.ExecuteAsync(null);

        var message = viewModel.ErrorMessage.ShouldNotBeNull();
        message.ShouldNotContain("203.0.113.7");
        message.ShouldNotContain("HttpRequestException");
        viewModel.IsBusy.ShouldBeFalse();
        viewModel.IsSignedIn.ShouldBeFalse();
    }

    [Fact]
    public async Task SignOutCommand_AsksForConfirmationBeforeAnythingHappens()
    {
        var fixture = CreateFixture(SuccessBody);
        await SignInAsync(fixture);

        fixture.ViewModel.SignOutCommand.Execute(null);

        fixture.Overlay.Active.ShouldBeOfType<SignOutDialogViewModel>();
        fixture.ViewModel.IsSignedIn.ShouldBeTrue();
        fixture.Store.Stored.ShouldNotBeNull();
    }

    [Fact]
    public async Task SignOutDialog_Confirmed_ClearsTheSessionAndReturnsToTheForm()
    {
        var fixture = CreateFixture(SuccessBody);
        await SignInAsync(fixture);
        fixture.ViewModel.SignOutCommand.Execute(null);
        var dialog = fixture.Overlay.Active.ShouldBeOfType<SignOutDialogViewModel>();

        await dialog.ConfirmCommand.ExecuteAsync(null);

        fixture.ViewModel.IsSignedIn.ShouldBeFalse();
        fixture.Store.Stored.ShouldBeNull();
        fixture.Overlay.Active.ShouldBeNull();
        fixture.ViewModel.Email.ShouldBeEmpty();
    }

    [Fact]
    public async Task SignOutDialog_Cancelled_KeepsTheSession()
    {
        var fixture = CreateFixture(SuccessBody);
        await SignInAsync(fixture);
        fixture.ViewModel.SignOutCommand.Execute(null);
        var dialog = fixture.Overlay.Active.ShouldBeOfType<SignOutDialogViewModel>();

        dialog.CancelCommand.Execute(null);

        fixture.ViewModel.IsSignedIn.ShouldBeTrue();
        fixture.Store.Stored.ShouldNotBeNull();
        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public void SignOutDialogTitle_NamesThePlayerRatherThanSayingAreYouSure()
    {
        var dialog = new SignOutDialogViewModel("Sylve", () => Task.CompletedTask, new RecordingOverlayService());

        dialog.Title.ShouldContain("Sylve");
        dialog.Message.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task SignedInState_FollowsTheServiceEvenWhenTheChangeComesFromElsewhere()
    {
        var fixture = CreateFixture(SuccessBody);
        await SignInAsync(fixture);

        await fixture.Accounts.SignOutAsync(CancellationToken.None);

        fixture.ViewModel.IsSignedIn.ShouldBeFalse();
        fixture.ViewModel.PlayerName.ShouldBeEmpty();
    }
}