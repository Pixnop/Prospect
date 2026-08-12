namespace Prospect.Core.Auth;

/// <summary>
/// Les états d'arrivée possibles de la machine à deux passes de
/// <c>POST auth3.vintagestory.at/v2/gamelogin</c>. La panne de transport n'en fait pas partie :
/// elle est traduite en <see cref="VsAccountUnavailableException"/>, comme partout ailleurs dans
/// le Core (voir <c>ModDbUnavailableException</c>), parce qu'un réseau coupé n'est pas un verdict
/// du service d'authentification.
/// </summary>
public enum VsLoginStatus
{
    /// <summary>Connexion établie : la session est exploitable.</summary>
    Success,

    /// <summary>
    /// Le compte a une double authentification : le service a répondu <c>requiretotpcode</c> avec
    /// un <c>prelogintoken</c> à renvoyer tel quel avec le code à six chiffres (deuxième passe).
    /// </summary>
    TwoFactorRequired,

    /// <summary>Le service a répondu <c>invalidemailorpassword</c>.</summary>
    InvalidEmailOrPassword,

    /// <summary>Deuxième passe refusée : le service a répondu <c>wrongtotpcode</c>.</summary>
    InvalidTwoFactorCode,

    /// <summary>
    /// Refus que le contrat connu ne nomme pas, ou réponse « valide » inexploitable (sans clé de
    /// session). VS Launcher ne traite aucun de ces deux cas et laisse l'utilisateur devant un
    /// formulaire muet ; Prospect préfère un refus explicite.
    /// </summary>
    Rejected,
}

/// <summary>
/// Résultat d'une passe de connexion : un état (<see cref="Status"/>) et, selon l'état, la charge
/// utile qui permet de continuer — la session en cas de succès, le <c>prelogintoken</c> quand la
/// deuxième passe est nécessaire.
/// </summary>
/// <remarks>
/// Classe et non record, délibérément : un record afficherait <see cref="Session"/> et
/// <see cref="PreLoginToken"/> dans son <c>ToString()</c> généré, c'est-à-dire du secret dans le
/// premier journal ou message d'assertion venu. Même raison que <see cref="VsSession.ToString"/>.
/// </remarks>
public sealed class VsLoginOutcome
{
    private VsLoginOutcome(VsLoginStatus status, VsSession? session = null, string? preLoginToken = null)
    {
        Status = status;
        Session = session;
        PreLoginToken = preLoginToken;
    }

    /// <summary>État d'arrivée de cette passe.</summary>
    public VsLoginStatus Status { get; }

    /// <summary>Session obtenue, renseignée seulement quand <see cref="Status"/> vaut <see cref="VsLoginStatus.Success"/>.</summary>
    public VsSession? Session { get; }

    /// <summary>
    /// Jeton de pré-connexion à réinjecter dans la deuxième passe, renseigné seulement quand
    /// <see cref="Status"/> vaut <see cref="VsLoginStatus.TwoFactorRequired"/>.
    /// </summary>
    public string? PreLoginToken { get; }

    /// <summary>Vrai si cette passe a établi la session.</summary>
    public bool IsSuccess => Status == VsLoginStatus.Success;

    /// <summary>Refus d'identifiants.</summary>
    public static VsLoginOutcome InvalidEmailOrPassword { get; } = new(VsLoginStatus.InvalidEmailOrPassword);

    /// <summary>Refus du code à six chiffres.</summary>
    public static VsLoginOutcome InvalidTwoFactorCode { get; } = new(VsLoginStatus.InvalidTwoFactorCode);

    /// <summary>Refus non nommé par le contrat connu, ou réponse inexploitable.</summary>
    public static VsLoginOutcome Rejected { get; } = new(VsLoginStatus.Rejected);

    /// <summary>Connexion établie.</summary>
    public static VsLoginOutcome Success(VsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new VsLoginOutcome(VsLoginStatus.Success, session);
    }

    /// <summary>Deuxième passe nécessaire, avec le jeton à réinjecter.</summary>
    public static VsLoginOutcome TwoFactorRequired(string preLoginToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preLoginToken);

        return new VsLoginOutcome(VsLoginStatus.TwoFactorRequired, preLoginToken: preLoginToken);
    }

    /// <summary>Représentation volontairement muette : voir la remarque de classe.</summary>
    public override string ToString() => $"VsLoginOutcome({Status})";
}