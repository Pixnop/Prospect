using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Auth;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Dialogs;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Section Comptes de l'écran Réglages : se connecter une fois à son compte vintagestory.at pour
/// que le multijoueur fonctionne dans toutes les instances. Trois états, jamais deux à la fois —
/// le formulaire, la demande du code à six chiffres quand le compte a une double authentification,
/// et l'état connecté avec le pseudonyme et la déconnexion.
/// </summary>
/// <remarks>
/// <para>
/// Le compte ne sert à rien pour télécharger le jeu, tout y est public : c'est la conclusion de
/// docs/research/vslauncher-et-distribution.md, et c'est pour ça que cet écran n'a jamais bloqué
/// quoi que ce soit ailleurs dans l'application. Il sert au multijoueur, en écrivant la session
/// dans le dataPath de chaque instance au lancement.
/// </para>
/// <para>
/// Discipline du mot de passe : il vit dans <see cref="Password"/>, lié à un champ masqué, et il en
/// disparaît dès que le flux se termine — succès, refus ou abandon. Le seul moment où il survit à
/// un appel est l'attente du code à six chiffres, parce que la deuxième passe le repostera. Aucun
/// message d'erreur de cet écran ne reprend jamais le texte d'une exception ou d'un champ d'API :
/// chaque cas a sa phrase, écrite pour un joueur.
/// </para>
/// </remarks>
public sealed partial class SettingsAccountsViewModel : ObservableObject
{
    private readonly VsAccountService _accounts;
    private readonly IOverlayService _overlay;

    private string? _preLoginToken;

    public SettingsAccountsViewModel(VsAccountService accounts, IOverlayService overlay)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(overlay);

        _accounts = accounts;
        _overlay = overlay;

        // La session a pu être relue avant même que cette fenêtre n'existe (voir App), et elle peut
        // changer plus tard depuis ailleurs : l'état de départ vient du service, la suite de son
        // évènement. Les deux objets vivent aussi longtemps que l'application, il n'y a donc rien à
        // désabonner.
        ApplySession(accounts.CurrentSession);
        accounts.SessionChanged += (_, session) => ApplySession(session);
    }

    /// <summary>Adresse du compte. Conservée d'une tentative à l'autre : ce n'est pas un secret.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _email = string.Empty;

    /// <summary>Mot de passe, lié à un champ masqué. Vidé dès que le flux se termine.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _password = string.Empty;

    /// <summary>Code à six chiffres de la double authentification, quand le service le réclame.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitTwoFactorCommand))]
    private string _twoFactorCode = string.Empty;

    /// <summary>Vrai pendant un appel : les boutons se verrouillent, la vue montre l'attente.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitTwoFactorCommand))]
    private bool _isBusy;

    /// <summary>Message d'échec de la dernière tentative, en voix produit, ou <see langword="null"/>.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Vrai quand le service a demandé le code à six chiffres et attend la deuxième passe.</summary>
    [ObservableProperty]
    private bool _isTwoFactorPending;

    /// <summary>Vrai quand une session est en vigueur.</summary>
    [ObservableProperty]
    private bool _isSignedIn;

    /// <summary>Pseudonyme en jeu, affiché dans l'état connecté.</summary>
    [ObservableProperty]
    private string _playerName = string.Empty;

    /// <summary>Identifiant du compte, affiché en mono discret sous le pseudonyme.</summary>
    [ObservableProperty]
    private string _playerUid = string.Empty;

    /// <summary>Première passe : identifiants seuls.</summary>
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        var password = Password;
        await RunAsync(() => _accounts.SignInAsync(Email.Trim(), password, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>Deuxième passe : mêmes identifiants, plus le code et le jeton de pré-connexion.</summary>
    [RelayCommand(CanExecute = nameof(CanSubmitTwoFactor))]
    private async Task SubmitTwoFactorAsync()
    {
        if (_preLoginToken is not { } token)
        {
            return;
        }

        var password = Password;
        var code = TwoFactorCode.Trim();
        await RunAsync(() => _accounts.CompleteTwoFactorAsync(Email.Trim(), password, code, token, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>Abandon de la deuxième passe : retour au formulaire, tout ce qui est sensible est oublié.</summary>
    [RelayCommand]
    private void CancelTwoFactor()
    {
        IsTwoFactorPending = false;
        ErrorMessage = null;
        ForgetSecrets();
    }

    /// <summary>Déconnexion : demande confirmation avant de toucher à quoi que ce soit.</summary>
    [RelayCommand]
    private void SignOut()
    {
        if (!IsSignedIn)
        {
            return;
        }

        _overlay.Show(new SignOutDialogViewModel(
            string.IsNullOrWhiteSpace(PlayerName) ? UiText.Account.UnknownPlayerName : PlayerName,
            () => _accounts.SignOutAsync(CancellationToken.None),
            _overlay));
    }

    private bool CanSignIn() => !IsBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

    private bool CanSubmitTwoFactor() => !IsBusy && !string.IsNullOrWhiteSpace(TwoFactorCode);

    // Un seul endroit pour les deux passes : même gestion de l'attente, même traduction des cas,
    // même règle d'oubli du mot de passe à la sortie.
    private async Task RunAsync(Func<Task<VsLoginOutcome>> pass)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var outcome = await pass().ConfigureAwait(true);
            Apply(outcome);
        }
        catch (VsAccountUnavailableException)
        {
            // Volontairement sans le message de l'exception : « Connection refused » suivi d'une
            // adresse IP n'apprend rien à un joueur et fait fuiter du détail d'infrastructure dans
            // l'interface.
            ErrorMessage = UiText.Account.ServiceUnavailable;
            ForgetSecrets();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(VsLoginOutcome outcome)
    {
        if (outcome.Status == VsLoginStatus.TwoFactorRequired)
        {
            // Seul chemin qui garde le mot de passe : la deuxième passe le reposte.
            _preLoginToken = outcome.PreLoginToken;
            IsTwoFactorPending = true;
            TwoFactorCode = string.Empty;

            return;
        }

        if (outcome.Status != VsLoginStatus.Success)
        {
            ErrorMessage = outcome.Status switch
            {
                VsLoginStatus.InvalidEmailOrPassword => UiText.Account.InvalidCredentials,
                VsLoginStatus.InvalidTwoFactorCode => UiText.Account.InvalidTwoFactorCode,
                _ => UiText.Account.Refused,
            };

            // Le refus du code laisse l'écran sur sa deuxième passe (l'utilisateur retape un code,
            // le précédent a peut-être simplement expiré) ; tout autre refus ramène au formulaire.
            IsTwoFactorPending = outcome.Status == VsLoginStatus.InvalidTwoFactorCode;
            ForgetSecrets(keepPassword: IsTwoFactorPending);

            return;
        }

        IsTwoFactorPending = false;
        ForgetSecrets();
    }

    private void ApplySession(VsSession? session)
    {
        IsSignedIn = session is not null;
        PlayerName = session?.PlayerName ?? string.Empty;
        PlayerUid = session?.PlayerUid ?? string.Empty;

        if (session is null)
        {
            Email = string.Empty;
            ErrorMessage = null;
            IsTwoFactorPending = false;
            ForgetSecrets();
        }
    }

    private void ForgetSecrets(bool keepPassword = false)
    {
        if (!keepPassword)
        {
            Password = string.Empty;
        }

        TwoFactorCode = string.Empty;
        _preLoginToken = IsTwoFactorPending ? _preLoginToken : null;
    }
}