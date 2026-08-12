using System.Net;

using Prospect.Core.Auth;
using Prospect.Core.Tests.Http;

using Shouldly;

namespace Prospect.Core.Tests.Auth;

/// <summary>
/// L'orchestration du compte : une passe ou deux selon la double authentification, la session
/// courante, la déconnexion, et l'évènement que l'UI écoute. Rien ici ne sort de la machine : le
/// client parle à un <see cref="FakeHttpMessageHandler"/>, le stockage est en mémoire.
/// </summary>
public sealed class VsAccountServiceTests
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

    private static readonly VsSession StoredSession = new()
    {
        Email = Email,
        PlayerName = "Sylve",
        PlayerUid = "3f2b8e14",
        Entitlements = "singleplayer",
        SessionKey = "cle-de-session",
        SessionSignature = "signature-de-session",
        MpToken = "jeton-multijoueur",
        HostGameServer = "false",
    };

    private sealed record Fixture(VsAccountService Service, FakeSecretStore Store, List<string> Bodies);

    private static Fixture CreateFixture(params string[] responses)
    {
        var bodies = new List<string>();
        var index = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult());
            var body = responses.Length == 0 ? SuccessBody : responses[Math.Min(index, responses.Length - 1)];
            index++;

            return FakeHttpMessageHandler.Text(body);
        });

        var store = new FakeSecretStore();
        var client = new VsAccountClient(new HttpClient(handler, disposeHandler: false));

        return new Fixture(new VsAccountService(client, store), store, bodies);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var client = new VsAccountClient(new HttpClient());

        Should.Throw<ArgumentNullException>(() => new VsAccountService(null!, new FakeSecretStore()));
        Should.Throw<ArgumentNullException>(() => new VsAccountService(client, null!));
    }

    [Fact]
    public void NewService_IsSignedOutBeforeAnythingIsLoaded()
    {
        var fixture = CreateFixture();

        fixture.Service.IsSignedIn.ShouldBeFalse();
        fixture.Service.CurrentSession.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_StoredSession_BecomesTheCurrentSessionAndNotifies()
    {
        var fixture = CreateFixture();
        fixture.Store.Stored = StoredSession;
        var notifications = new List<VsSession?>();
        fixture.Service.SessionChanged += (_, session) => notifications.Add(session);

        await fixture.Service.LoadAsync(CancellationToken.None);

        fixture.Service.IsSignedIn.ShouldBeTrue();
        fixture.Service.CurrentSession.ShouldBe(StoredSession);
        notifications.ShouldHaveSingleItem().ShouldBe(StoredSession);
    }

    [Fact]
    public async Task LoadAsync_NothingStored_StaysSignedOutWithoutNotifying()
    {
        var fixture = CreateFixture();
        var notified = false;
        fixture.Service.SessionChanged += (_, _) => notified = true;

        await fixture.Service.LoadAsync(CancellationToken.None);

        fixture.Service.IsSignedIn.ShouldBeFalse();
        notified.ShouldBeFalse();
    }

    [Fact]
    public async Task SignInAsync_SinglePassSuccess_StoresTheSessionAndNotifies()
    {
        var fixture = CreateFixture(SuccessBody);
        var notifications = new List<VsSession?>();
        fixture.Service.SessionChanged += (_, session) => notifications.Add(session);

        var outcome = await fixture.Service.SignInAsync(Email, Password, CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.Success);
        fixture.Service.IsSignedIn.ShouldBeTrue();
        fixture.Service.CurrentSession.ShouldNotBeNull().PlayerName.ShouldBe("Sylve");
        fixture.Store.SaveCount.ShouldBe(1);
        fixture.Store.Stored.ShouldBe(fixture.Service.CurrentSession);
        notifications.ShouldHaveSingleItem().ShouldNotBeNull();
    }

    [Fact]
    public async Task SignInAsync_Success_PersistsNothingThatCouldBeThePassword()
    {
        var fixture = CreateFixture(SuccessBody);

        await fixture.Service.SignInAsync(Email, Password, CancellationToken.None);

        var stored = fixture.Store.Stored.ShouldNotBeNull();
        foreach (var value in new[]
                 {
                     stored.Email, stored.PlayerName, stored.PlayerUid, stored.Entitlements,
                     stored.SessionKey, stored.SessionSignature, stored.MpToken, stored.HostGameServer,
                 })
        {
            value.ShouldNotContain(Password);
        }
    }

    [Fact]
    public async Task SignInAsync_TwoFactorRequired_StoresNothingAndStaysSignedOut()
    {
        var fixture = CreateFixture(TwoFactorBody);

        var outcome = await fixture.Service.SignInAsync(Email, Password, CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.TwoFactorRequired);
        outcome.PreLoginToken.ShouldBe("jeton-de-pre-connexion");
        fixture.Service.IsSignedIn.ShouldBeFalse();
        fixture.Store.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task CompleteTwoFactorAsync_AfterAFirstPass_ReplaysTheTokenAndSignsIn()
    {
        var fixture = CreateFixture(TwoFactorBody, SuccessBody);
        var first = await fixture.Service.SignInAsync(Email, Password, CancellationToken.None);

        var second = await fixture.Service.CompleteTwoFactorAsync(
            Email, Password, "123456", first.PreLoginToken!, CancellationToken.None);

        second.Status.ShouldBe(VsLoginStatus.Success);
        fixture.Service.IsSignedIn.ShouldBeTrue();
        fixture.Store.SaveCount.ShouldBe(1);
        fixture.Bodies.Count.ShouldBe(2);
        fixture.Bodies[1].ShouldContain("totpcode=123456");
        fixture.Bodies[1].ShouldContain("prelogintoken=jeton-de-pre-connexion");
    }

    [Fact]
    public async Task CompleteTwoFactorAsync_WrongCode_StaysSignedOut()
    {
        var fixture = CreateFixture("""{ "valid": 0, "reason": "wrongtotpcode" }""");

        var outcome = await fixture.Service.CompleteTwoFactorAsync(Email, Password, "000000", "jeton", CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.InvalidTwoFactorCode);
        fixture.Service.IsSignedIn.ShouldBeFalse();
        fixture.Store.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task SignInAsync_Refused_LeavesAnExistingSessionUntouched()
    {
        var fixture = CreateFixture(SuccessBody, """{ "valid": 0, "reason": "invalidemailorpassword" }""");
        await fixture.Service.SignInAsync(Email, Password, CancellationToken.None);
        var established = fixture.Service.CurrentSession;

        var outcome = await fixture.Service.SignInAsync(Email, "autre", CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.InvalidEmailOrPassword);
        fixture.Service.CurrentSession.ShouldBe(established);
        fixture.Store.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task SignInAsync_TransportFailure_PropagatesAndKeepsTheStateIntact()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Status(HttpStatusCode.GatewayTimeout));
        var store = new FakeSecretStore();
        var service = new VsAccountService(new VsAccountClient(new HttpClient(handler, disposeHandler: false)), store);

        await Should.ThrowAsync<VsAccountUnavailableException>(() => service.SignInAsync(Email, Password, CancellationToken.None));

        service.IsSignedIn.ShouldBeFalse();
        store.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task SignOutAsync_SignedIn_ClearsTheStoreAndNotifiesWithNull()
    {
        var fixture = CreateFixture(SuccessBody);
        await fixture.Service.SignInAsync(Email, Password, CancellationToken.None);
        var notifications = new List<VsSession?>();
        fixture.Service.SessionChanged += (_, session) => notifications.Add(session);

        await fixture.Service.SignOutAsync(CancellationToken.None);

        fixture.Service.IsSignedIn.ShouldBeFalse();
        fixture.Service.CurrentSession.ShouldBeNull();
        fixture.Store.ClearCount.ShouldBe(1);
        fixture.Store.Stored.ShouldBeNull();
        notifications.ShouldHaveSingleItem().ShouldBeNull();
    }

    [Fact]
    public async Task SignOutAsync_AlreadySignedOut_ClearsAnyLeftoverFileWithoutNotifying()
    {
        // La suppression part quand même : un fichier de session peut avoir survécu à un plantage
        // sans que ce service en ait jamais rien su.
        var fixture = CreateFixture();
        var notified = false;
        fixture.Service.SessionChanged += (_, _) => notified = true;

        await fixture.Service.SignOutAsync(CancellationToken.None);

        fixture.Store.ClearCount.ShouldBe(1);
        notified.ShouldBeFalse();
    }

    [Fact]
    public async Task SignInAsync_BlankInput_ThrowsWithoutReachingTheNetwork()
    {
        var fixture = CreateFixture(SuccessBody);

        await Should.ThrowAsync<ArgumentException>(() => fixture.Service.SignInAsync(" ", Password, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => fixture.Service.SignInAsync(Email, " ", CancellationToken.None));

        fixture.Bodies.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompleteTwoFactorAsync_BlankCodeOrToken_ThrowsWithoutReachingTheNetwork()
    {
        var fixture = CreateFixture(SuccessBody);

        await Should.ThrowAsync<ArgumentException>(() => fixture.Service.CompleteTwoFactorAsync(Email, Password, " ", "jeton", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => fixture.Service.CompleteTwoFactorAsync(Email, Password, "123456", " ", CancellationToken.None));

        fixture.Bodies.ShouldBeEmpty();
    }
}
