namespace Prospect.Core.Auth;

/// <summary>
/// Façade applicative du domaine Auth (docs/architecture.md, « Services applicatifs par
/// domaine ») : enchaîne les une ou deux passes de connexion, conserve la session courante,
/// la persiste via <see cref="ISecretStore"/> et prévient l'UI quand elle change. C'est le seul
/// type que les ViewModels connaissent ; ni le client HTTP ni le stockage ne remontent jusqu'à eux.
/// </summary>
/// <remarks>
/// Le mot de passe traverse ce service comme il traverse le client : en paramètre, le temps d'un
/// appel. Il n'est jamais mis en champ, jamais retenu entre les deux passes de la double
/// authentification — c'est l'appelant qui le redonne, et c'est voulu : la seule copie vivante
/// reste celle du formulaire, que l'écran efface dès qu'il en a fini.
/// </remarks>
public sealed class VsAccountService
{
    private readonly VsAccountClient _client;
    private readonly ISecretStore _secretStore;

    public VsAccountService(VsAccountClient client, ISecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(secretStore);

        _client = client;
        _secretStore = secretStore;
    }

    /// <summary>
    /// Levé chaque fois que la session change réellement : une session à la connexion,
    /// <see langword="null"/> à la déconnexion. C'est ce que l'écran Réglages et la checklist de
    /// premier lancement écoutent.
    /// </summary>
    public event EventHandler<VsSession?>? SessionChanged;

    /// <summary>Session en vigueur, ou <see langword="null"/> si personne n'est connecté.</summary>
    public VsSession? CurrentSession { get; private set; }

    /// <summary>Vrai si une session est en vigueur.</summary>
    public bool IsSignedIn => CurrentSession is not null;

    /// <summary>
    /// Relit la session stockée au démarrage de l'application. Ne lève jamais pour un secret
    /// absent ou illisible : dans les deux cas l'utilisateur est simplement déconnecté (voir
    /// <see cref="ISecretStore.LoadAsync"/>).
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var session = await _secretStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return;
        }

        SetSession(session);
    }

    /// <summary>
    /// Première passe. Un compte sans double authentification est connecté au retour ; sinon le
    /// résultat porte <see cref="VsLoginStatus.TwoFactorRequired"/> et le jeton à redonner à
    /// <see cref="CompleteTwoFactorAsync"/>.
    /// </summary>
    /// <exception cref="VsAccountUnavailableException">Service injoignable ou réponse illisible.</exception>
    public Task<VsLoginOutcome> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return LogInAsync(email, password, totpCode: null, preLoginToken: null, cancellationToken);
    }

    /// <summary>
    /// Deuxième passe : mêmes identifiants, plus le code à six chiffres et le jeton rendu par la
    /// première passe.
    /// </summary>
    /// <exception cref="VsAccountUnavailableException">Service injoignable ou réponse illisible.</exception>
    public Task<VsLoginOutcome> CompleteTwoFactorAsync(
        string email,
        string password,
        string totpCode,
        string preLoginToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(totpCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(preLoginToken);

        return LogInAsync(email, password, totpCode, preLoginToken, cancellationToken);
    }

    /// <summary>
    /// Déconnecte : efface le secret stocké et oublie la session courante. L'effacement part même
    /// si ce service se croyait déjà déconnecté, pour balayer un fichier qui aurait survécu à un
    /// arrêt brutal.
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _secretStore.ClearAsync(cancellationToken).ConfigureAwait(false);

        if (CurrentSession is null)
        {
            return;
        }

        CurrentSession = null;
        SessionChanged?.Invoke(this, null);
    }

    private async Task<VsLoginOutcome> LogInAsync(
        string email,
        string password,
        string? totpCode,
        string? preLoginToken,
        CancellationToken cancellationToken)
    {
        var outcome = await _client
            .LogInAsync(email, password, totpCode, preLoginToken, cancellationToken)
            .ConfigureAwait(false);

        // Tout ce qui n'est pas un succès laisse l'état exactement où il était : un refus
        // d'identifiants ne doit pas déconnecter quelqu'un qui l'était déjà.
        if (outcome.Session is not { } session)
        {
            return outcome;
        }

        await _secretStore.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        SetSession(session);

        return outcome;
    }

    private void SetSession(VsSession session)
    {
        CurrentSession = session;
        SessionChanged?.Invoke(this, session);
    }
}