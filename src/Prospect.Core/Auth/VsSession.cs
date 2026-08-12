namespace Prospect.Core.Auth;

/// <summary>
/// Session de compte vintagestory.at telle que l'endpoint de connexion la renvoie
/// (docs/research/vslauncher-et-distribution.md, section a). C'est exactement, et uniquement, ce
/// que le jeu attend dans le <c>clientsettings.json</c> de son <c>--dataPath</c> pour se considérer
/// connecté en multijoueur (section d) : Prospect ne s'en sert jamais lui-même pour parler au
/// réseau, il ne fait que la conserver et l'injecter au lancement.
/// </summary>
/// <remarks>
/// Le mot de passe n'est PAS un champ de ce type, et ne le sera jamais : il transite dans les
/// paramètres de <see cref="VsAccountClient.LogInAsync"/> et disparaît avec la pile d'appel
/// (docs/architecture.md, section « Après le MVP »). Le reste, en revanche, est du secret : d'où
/// le <see cref="ToString"/> réécrit à la main, qui neutralise celui qu'un record génère
/// automatiquement et qui recracherait clés et jeton dans le premier message d'erreur venu.
/// </remarks>
public sealed record VsSession
{
    /// <summary>Adresse du compte, telle que saisie : le jeu l'écrit dans <c>useremail</c>.</summary>
    public required string Email { get; init; }

    /// <summary>Pseudonyme affiché en jeu (<c>playername</c>).</summary>
    public required string PlayerName { get; init; }

    /// <summary>Identifiant du joueur (<c>uid</c> côté réponse, <c>playeruid</c> côté clientsettings).</summary>
    public required string PlayerUid { get; init; }

    /// <summary>Droits du compte, tels quels (<c>entitlements</c>).</summary>
    public required string Entitlements { get; init; }

    /// <summary>Clé de session (<c>sessionkey</c>).</summary>
    public required string SessionKey { get; init; }

    /// <summary>Signature de la session (<c>sessionsignature</c>).</summary>
    public required string SessionSignature { get; init; }

    /// <summary>Jeton multijoueur (<c>mptoken</c>), vide quand le serveur n'en renvoie pas.</summary>
    public required string MpToken { get; init; }

    /// <summary>
    /// Droit d'héberger un serveur (<c>hasgameserver</c> dans la réponse, <c>hostgameserver</c>
    /// dans le fichier du jeu : les deux noms diffèrent, c'est vérifié dans le code de VS Launcher).
    /// Conservé en chaîne parce que sa destination, <c>stringSettings</c>, est un dictionnaire de
    /// chaînes côté jeu.
    /// </summary>
    public required string HostGameServer { get; init; }

    /// <summary>Vrai si la session porte de quoi authentifier le jeu (clé et signature présentes).</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(SessionKey) && !string.IsNullOrWhiteSpace(SessionSignature);

    /// <summary>Représentation volontairement muette : voir la remarque de classe.</summary>
    public override string ToString() => $"VsSession({PlayerName})";
}