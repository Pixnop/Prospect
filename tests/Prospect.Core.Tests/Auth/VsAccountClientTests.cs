using System.Net;

using Prospect.Core.Auth;
using Prospect.Core.Tests.Http;

using Shouldly;

namespace Prospect.Core.Tests.Auth;

/// <summary>
/// Le contrat de <c>POST auth3.vintagestory.at/v2/gamelogin</c>, rejoué contre un
/// <see cref="FakeHttpMessageHandler"/> : aucune requête ne quitte jamais la machine, aucun
/// identifiant réel ni fictif n'est envoyé nulle part. Les formes de réponse reproduites ici sont
/// celles relevées dans le code de VS Launcher
/// (docs/research/vslauncher-et-distribution.md, section a).
/// </summary>
public sealed class VsAccountClientTests
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
      "entitlements": "singleplayer,multiplayer",
      "playername": "Sylve",
      "hasgameserver": true
    }
    """;

    private static (VsAccountClient Client, FakeHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);

        return (new VsAccountClient(new HttpClient(handler, disposeHandler: false)), handler);
    }

    private static (VsAccountClient Client, List<string> Bodies) CreateRecordingClient(params string[] responses)
    {
        var bodies = new List<string>();
        var index = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult());
            var body = responses[Math.Min(index, responses.Length - 1)];
            index++;

            return FakeHttpMessageHandler.Text(body);
        });

        return (new VsAccountClient(new HttpClient(handler, disposeHandler: false)), bodies);
    }

    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => new VsAccountClient(null!));

    [Fact]
    public void GameLoginUrl_IsTheEndpointDocumentedInTheResearch()
        => VsAccountClient.GameLoginUrl.ShouldBe(new Uri("https://auth3.vintagestory.at/v2/gamelogin"));

    [Fact]
    public async Task LogInAsync_SinglePass_PostsFormUrlEncodedWithTheFourContractFields()
    {
        var (client, bodies) = CreateRecordingClient(SuccessBody);

        await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        var fields = ParseForm(bodies.ShouldHaveSingleItem());
        fields["email"].ShouldBe(Email);
        fields["password"].ShouldBe(Password);
        // Toujours présents, vides quand ils ne s'appliquent pas : c'est exactement ce que fait
        // netHandlers.ts (`?? ""`), et l'endpoint n'a jamais été observé autrement.
        fields["totpcode"].ShouldBe(string.Empty);
        fields["prelogintoken"].ShouldBe(string.Empty);
        fields.Count.ShouldBe(4);
    }

    [Fact]
    public async Task LogInAsync_SinglePass_UsesPostWithFormContentTypeAndIdentifiableUserAgent()
    {
        var (client, handler) = CreateClient(_ => FakeHttpMessageHandler.Text(SuccessBody));

        await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Url.ShouldBe(VsAccountClient.GameLoginUrl);
        request.UserAgent.ShouldNotBeNull();
        request.UserAgent!.ShouldStartWith("Prospect/");
    }

    [Fact]
    public async Task LogInAsync_ValidResponse_MapsEveryDocumentedSessionField()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text(SuccessBody));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.Success);
        var session = outcome.Session.ShouldNotBeNull();
        session.Email.ShouldBe(Email);
        session.SessionKey.ShouldBe("cle-de-session");
        session.SessionSignature.ShouldBe("signature-de-session");
        session.MpToken.ShouldBe("jeton-multijoueur");
        session.PlayerUid.ShouldBe("3f2b8e14");
        session.Entitlements.ShouldBe("singleplayer,multiplayer");
        session.PlayerName.ShouldBe("Sylve");
        // hasgameserver arrive en booléen JSON et repart en chaîne : stringSettings est un
        // dictionnaire de chaînes côté jeu (voir ClientSettingsSessionWriter).
        session.HostGameServer.ShouldBe("true");
    }

    [Fact]
    public async Task LogInAsync_TotpRequired_ReturnsTwoFactorRequiredWithThePreLoginToken()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("""
        { "valid": 0, "reason": "requiretotpcode", "prelogintoken": "jeton-de-pre-connexion" }
        """));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.TwoFactorRequired);
        outcome.PreLoginToken.ShouldBe("jeton-de-pre-connexion");
        outcome.Session.ShouldBeNull();
    }

    [Fact]
    public async Task LogInAsync_SecondPass_ResendsCredentialsWithTotpCodeAndPreLoginToken()
    {
        var (client, bodies) = CreateRecordingClient(
            """{ "valid": 0, "reason": "requiretotpcode", "prelogintoken": "jeton-de-pre-connexion" }""",
            SuccessBody);

        var first = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);
        var second = await client.LogInAsync(Email, Password, "123456", first.PreLoginToken, CancellationToken.None);

        second.Status.ShouldBe(VsLoginStatus.Success);
        second.Session.ShouldNotBeNull().PlayerName.ShouldBe("Sylve");
        bodies.Count.ShouldBe(2);
        var secondFields = ParseForm(bodies[1]);
        secondFields["email"].ShouldBe(Email);
        secondFields["password"].ShouldBe(Password);
        secondFields["totpcode"].ShouldBe("123456");
        secondFields["prelogintoken"].ShouldBe("jeton-de-pre-connexion");
    }

    [Fact]
    public async Task LogInAsync_InvalidEmailOrPassword_IsATypedFailureNotAnException()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("""
        { "valid": 0, "reason": "invalidemailorpassword" }
        """));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.InvalidEmailOrPassword);
        outcome.Session.ShouldBeNull();
        outcome.PreLoginToken.ShouldBeNull();
    }

    [Fact]
    public async Task LogInAsync_WrongTotpCode_IsATypedFailure()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("""
        { "valid": 0, "reason": "wrongtotpcode" }
        """));

        var outcome = await client.LogInAsync(Email, Password, "000000", "jeton", CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.InvalidTwoFactorCode);
    }

    [Fact]
    public async Task LogInAsync_UnknownReason_IsRejectedRatherThanTakenForASuccess()
    {
        // VS Launcher ne fait rien du tout dans ce cas (aucune branche), ce qui laisse
        // l'utilisateur devant un formulaire muet : un refus nommé vaut mieux.
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("""
        { "valid": 0, "reason": "accountlocked" }
        """));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.Rejected);
        outcome.Session.ShouldBeNull();
    }

    [Fact]
    public async Task LogInAsync_ValidButNoSessionMaterial_IsRejectedRatherThanInjectingEmptyKeys()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("""
        { "valid": 1, "playername": "Sylve" }
        """));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.Rejected);
    }

    [Fact]
    public async Task LogInAsync_MissingValidField_IsRejectedRatherThanTakenForASuccess()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("""
        { "sessionkey": "cle", "sessionsignature": "signature" }
        """));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.Rejected);
    }

    [Fact]
    public async Task LogInAsync_TransportFailure_IsTranslatedToVsAccountUnavailable()
    {
        var (client, _) = CreateClient(_ => throw new HttpRequestException("réseau coupé"));

        var exception = await Should.ThrowAsync<VsAccountUnavailableException>(
            () => client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None));

        exception.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task LogInAsync_HttpErrorStatus_IsTranslatedToVsAccountUnavailable()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Status(HttpStatusCode.BadGateway));

        await Should.ThrowAsync<VsAccountUnavailableException>(
            () => client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task LogInAsync_UnreadablePayload_IsTranslatedToVsAccountUnavailable()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("<html>maintenance</html>"));

        await Should.ThrowAsync<VsAccountUnavailableException>(
            () => client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task LogInAsync_CallerCancels_LetsTheCancellationThroughUntranslated()
    {
        using var cancellation = new CancellationTokenSource();
        var (client, _) = CreateClient(_ =>
        {
            cancellation.Cancel();

            throw new TaskCanceledException();
        });

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.LogInAsync(Email, Password, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task LogInAsync_NeverRetries_ARefusedLoginIsPostedExactlyOnce()
    {
        // Un identifiant refusé ne se réessaie pas : rejouer un mot de passe contre un service
        // d'authentification, c'est courir après un verrouillage de compte.
        var (client, handler) = CreateClient(_ => FakeHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable));

        await Should.ThrowAsync<VsAccountUnavailableException>(
            () => client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None));

        handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LogInAsync_BlankEmail_ThrowsBeforeSendingAnything(string email)
    {
        var (client, handler) = CreateClient(_ => FakeHttpMessageHandler.Text(SuccessBody));

        await Should.ThrowAsync<ArgumentException>(
            () => client.LogInAsync(email, Password, cancellationToken: CancellationToken.None));

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task LogInAsync_BlankPassword_ThrowsBeforeSendingAnything()
    {
        var (client, handler) = CreateClient(_ => FakeHttpMessageHandler.Text(SuccessBody));

        await Should.ThrowAsync<ArgumentException>(
            () => client.LogInAsync(Email, "  ", cancellationToken: CancellationToken.None));

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task LogInAsync_ResponseFieldsAreNumbersOrBooleans_AreReadLeniently()
    {
        // Le contrat n'est pas documenté par l'éditeur : rien ne garantit que `uid` restera une
        // chaîne. Un DTO qui casse sur un entier ferait échouer une connexion parfaitement valide.
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("""
        {
          "valid": 1,
          "sessionkey": "cle",
          "sessionsignature": "signature",
          "uid": 4815162342,
          "playername": "Sylve",
          "hasgameserver": "false"
        }
        """));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        var session = outcome.Session.ShouldNotBeNull();
        session.PlayerUid.ShouldBe("4815162342");
        session.HostGameServer.ShouldBe("false");
        session.MpToken.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task LogInAsync_ValidAsString_IsStillASuccess()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text("""
        { "valid": "1", "sessionkey": "cle", "sessionsignature": "signature", "playername": "Sylve" }
        """));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        outcome.Status.ShouldBe(VsLoginStatus.Success);
    }

    [Fact]
    public async Task LogInOutcomeAndSession_ToString_NeverSpillSecretMaterial()
    {
        var (client, _) = CreateClient(_ => FakeHttpMessageHandler.Text(SuccessBody));

        var outcome = await client.LogInAsync(Email, Password, cancellationToken: CancellationToken.None);

        outcome.ToString().ShouldNotContain("cle-de-session");
        outcome.ToString().ShouldNotContain("jeton-multijoueur");
        var session = outcome.Session.ShouldNotBeNull();
        session.ToString().ShouldNotContain("cle-de-session");
        session.ToString().ShouldNotContain("signature-de-session");
        session.ToString().ShouldNotContain("jeton-multijoueur");
        session.ToString().ShouldNotContain(Email);
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            fields[Uri.UnescapeDataString(name)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return fields;
    }
}
