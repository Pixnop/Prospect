using System.Text.Json;

using Prospect.Core.Common;

namespace Prospect.Core.Auth;

/// <summary>
/// Client de <c>POST https://auth3.vintagestory.at/v2/gamelogin</c>, le seul endpoint
/// d'authentification que Prospect appelle (docs/research/vslauncher-et-distribution.md,
/// section a). Une passe suffit sur un compte sans double authentification ; sinon le service
/// répond <c>requiretotpcode</c> avec un <c>prelogintoken</c> et il faut une deuxième passe qui
/// renvoie les mêmes identifiants plus ce jeton et le code à six chiffres.
/// </summary>
/// <remarks>
/// <para>
/// Deux décisions valent d'être dites. D'abord, aucune politique de réessai, contrairement aux
/// clients de catalogue et de téléchargement : rejouer automatiquement un mot de passe contre un
/// service d'authentification, c'est courir après un verrouillage de compte. Un échec de transport
/// remonte immédiatement en <see cref="VsAccountUnavailableException"/>, à l'utilisateur de
/// réessayer s'il le veut.
/// </para>
/// <para>
/// Ensuite, le mot de passe est un simple paramètre de méthode, jamais le champ d'un objet de
/// requête : il n'existe donc aucun type dont le <c>ToString()</c> pourrait le recracher dans un
/// journal, et sa durée de vie se limite à celle de l'appel.
/// </para>
/// </remarks>
public sealed class VsAccountClient
{
    /// <summary>Endpoint de connexion, relevé dans <c>src/renderer/src/components/ui/SessionButton.tsx</c>.</summary>
    public static readonly Uri GameLoginUrl = new("https://auth3.vintagestory.at/v2/gamelogin");

    private const string RequireTotpCodeReason = "requiretotpcode";
    private const string InvalidEmailOrPasswordReason = "invalidemailorpassword";
    private const string WrongTotpCodeReason = "wrongtotpcode";

    private readonly HttpClient _httpClient;

    /// <summary>Construit le client.</summary>
    /// <param name="httpClient">Client HTTP dédié, avec un délai de requête court (voir la composition root).</param>
    public VsAccountClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <summary>En-tête <c>User-Agent</c> envoyé sur la requête de connexion.</summary>
    public static string UserAgent => ProspectUserAgent.Value;

    /// <summary>
    /// Exécute une passe de connexion. Les quatre champs du contrat partent toujours, vides quand
    /// ils ne s'appliquent pas : c'est ce que fait l'implémentation d'origine, et l'endpoint n'a
    /// jamais été observé autrement.
    /// </summary>
    /// <param name="email">Adresse du compte.</param>
    /// <param name="password">Mot de passe. Ne quitte pas cette pile d'appel : ni journalisé, ni conservé, ni stocké.</param>
    /// <param name="totpCode">Code à six chiffres, seulement en deuxième passe.</param>
    /// <param name="preLoginToken">Jeton rendu par la première passe, seulement en deuxième passe.</param>
    /// <param name="cancellationToken">Annulation demandée par l'appelant.</param>
    /// <returns>L'état d'arrivée de la passe (voir <see cref="VsLoginStatus"/>).</returns>
    /// <exception cref="VsAccountUnavailableException">Service injoignable ou réponse illisible.</exception>
    public async Task<VsLoginOutcome> LogInAsync(
        string email,
        string password,
        string? totpCode = null,
        string? preLoginToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var payload = await PostAsync(email, password, totpCode, preLoginToken, cancellationToken).ConfigureAwait(false);

        VsGameLoginResponseDto? response;
        try
        {
            response = JsonSerializer.Deserialize(payload, VsAuthJsonContext.Default.VsGameLoginResponseDto);
        }
        catch (JsonException exception)
        {
            throw VsAccountUnavailableException.FromNetworkFailure(exception);
        }

        return response is null
            ? throw VsAccountUnavailableException.FromNetworkFailure(new JsonException("Réponse vide du service de compte."))
            : MapOutcome(response, email);
    }

    private async Task<string> PostAsync(
        string email,
        string password,
        string? totpCode,
        string? preLoginToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["email"] = email,
                ["password"] = password,
                ["totpcode"] = totpCode ?? string.Empty,
                ["prelogintoken"] = preLoginToken ?? string.Empty,
            });

            using var message = new HttpRequestMessage(HttpMethod.Post, GameLoginUrl) { Content = content };
            message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            throw VsAccountUnavailableException.FromNetworkFailure(exception);
        }
    }

    // Même distinction que ModDbClient.IsNetworkFailure : une TaskCanceledException n'est une
    // panne que si l'appelant, lui, n'a rien annulé — sinon c'est une décision, et elle ressort
    // intacte.
    private static bool IsTransportFailure(Exception exception, CancellationToken callerToken)
        => exception switch
        {
            OperationCanceledException => !callerToken.IsCancellationRequested,
            HttpRequestException or IOException or TimeoutException => true,
            _ => false,
        };

    private static VsLoginOutcome MapOutcome(VsGameLoginResponseDto response, string email)
    {
        if (response.Valid == 1)
        {
            var session = new VsSession
            {
                Email = email,
                PlayerName = response.PlayerName ?? string.Empty,
                PlayerUid = response.Uid ?? string.Empty,
                Entitlements = response.Entitlements ?? string.Empty,
                SessionKey = response.SessionKey ?? string.Empty,
                SessionSignature = response.SessionSignature ?? string.Empty,
                MpToken = response.MpToken ?? string.Empty,
                HostGameServer = response.HasGameServer ?? string.Empty,
            };

            // Un « valide » sans clé ni signature n'authentifierait rien : mieux vaut un refus
            // franc que huit clés vides écrites dans le clientsettings du joueur.
            return session.IsUsable ? VsLoginOutcome.Success(session) : VsLoginOutcome.Rejected;
        }

        var reason = response.Reason?.Trim();

        if (string.Equals(reason, RequireTotpCodeReason, StringComparison.OrdinalIgnoreCase))
        {
            // Sans jeton à réinjecter, la deuxième passe n'a aucune chance : autant le dire tout
            // de suite plutôt que d'ouvrir un champ de code qui ne mènera nulle part.
            return string.IsNullOrWhiteSpace(response.PreLoginToken)
                ? VsLoginOutcome.Rejected
                : VsLoginOutcome.TwoFactorRequired(response.PreLoginToken);
        }

        if (string.Equals(reason, InvalidEmailOrPasswordReason, StringComparison.OrdinalIgnoreCase))
        {
            return VsLoginOutcome.InvalidEmailOrPassword;
        }

        return string.Equals(reason, WrongTotpCodeReason, StringComparison.OrdinalIgnoreCase)
            ? VsLoginOutcome.InvalidTwoFactorCode
            : VsLoginOutcome.Rejected;
    }
}