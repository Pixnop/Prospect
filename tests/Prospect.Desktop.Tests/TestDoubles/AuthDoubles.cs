using System.IO.Abstractions;
using System.Net;

using Prospect.Core.Auth;
using Prospect.Core.Common;
using Prospect.Core.Storage;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Pièces de compte pour les ViewModels et le lanceur construits à la main. Le service rendu ici
/// est toujours déconnecté et son client HTTP ne répond rien d'exploitable : un test qui ne parle
/// pas de compte n'a rien à en savoir, et un lancement sans session ne touche à aucun
/// <c>clientsettings.json</c> (voir <c>GameLauncher.InjectAccountSessionAsync</c>).
/// </summary>
internal static class AccountDoubles
{
    public static VsAccountService SignedOut()
        => new(
            new VsAccountClient(new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), disposeHandler: false)),
            new MemorySecretStore());

    public static ClientSettingsSessionWriter ClientSettings(IFileSystem fileSystem)
        => new(fileSystem, new JsonFileStore(fileSystem));
}

/// <summary>
/// Gestionnaire HTTP factice à répondeur libre, pour les ViewModels construits à la main. Le
/// répondeur reçoit la requête et rend la réponse ou lève : aucune requête ne quitte la machine,
/// aucun identifiant n'atteint le moindre service réel.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_responder(request));
}

/// <summary>
/// Faux service de compte Vintage Story adossé à <see cref="FakeCatalogHandler"/> : sert des corps
/// de réponse dans l'ordre où on les a posés (une passe, ou deux quand le scénario exerce la double
/// authentification) et garde les corps postés pour inspection.
/// </summary>
internal sealed class FakeVsAuthHandler
{
    /// <summary>Réponse d'une connexion réussie, à la forme relevée dans le code de VS Launcher.</summary>
    public const string SuccessJson = """
    {
      "valid": 1,
      "sessionkey": "cle-de-session",
      "sessionsignature": "signature-de-session",
      "mptoken": "jeton-multijoueur",
      "uid": "3f2b8e14",
      "entitlements": "singleplayer,multiplayer",
      "playername": "Sylve",
      "hasgameserver": false
    }
    """;

    /// <summary>Réponse « il me faut le code à six chiffres », avec le jeton à réinjecter.</summary>
    public const string TwoFactorJson = """
    { "valid": 0, "reason": "requiretotpcode", "prelogintoken": "jeton-de-pre-connexion" }
    """;

    /// <summary>Réponse « identifiants refusés ».</summary>
    public const string InvalidCredentialsJson = """
    { "valid": 0, "reason": "invalidemailorpassword" }
    """;

    private int _index;

    /// <summary>Corps servis, un par passe. Le dernier se répète si on appelle plus souvent.</summary>
    public IReadOnlyList<string> Responses { get; set; } = [SuccessJson];

    /// <summary>Corps postés, dans l'ordre : de quoi vérifier les quatre champs du contrat.</summary>
    public List<string> PostedBodies { get; } = [];

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        PostedBodies.Add(request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty);
        var body = Responses[Math.Min(_index, Responses.Count - 1)];
        _index++;

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}

/// <summary>
/// Double d'<see cref="IUnixFilePermissions"/> : mémorise les modes au lieu d'appeler <c>chmod</c>.
/// Indispensable dans le conteneur de test, et pas seulement pratique : l'adaptateur de production
/// tape directement sur la BCL, donc sur le VRAI disque, alors que tout le reste du conteneur
/// travaille sur un <c>MockFileSystem</c>. Sans cette substitution, un test qui enregistre une
/// session tenterait un <c>chmod</c> sur le dossier de données réel de la machine qui exécute la
/// suite.
/// </summary>
internal sealed class RecordingUnixFilePermissions : IUnixFilePermissions
{
    public Dictionary<string, UnixFileMode> Modes { get; } = new(StringComparer.Ordinal);

    public void SetMode(string path, UnixFileMode mode) => Modes[path] = mode;
}

/// <summary>
/// <see cref="ISecretStore"/> en mémoire : les tests headless n'ont pas à écrire un secret sur le
/// système de fichiers factice pour vérifier le comportement d'un écran, et ce double rend
/// inspectable ce qui a été conservé.
/// </summary>
internal sealed class MemorySecretStore : ISecretStore
{
    public VsSession? Stored { get; set; }

    public Task<VsSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);

    public Task SaveAsync(VsSession session, CancellationToken cancellationToken = default)
    {
        Stored = session;

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Stored = null;

        return Task.CompletedTask;
    }
}