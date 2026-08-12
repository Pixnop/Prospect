namespace Prospect.Core.Auth;

/// <summary>
/// Port de stockage du secret de session (docs/architecture.md, « Ports et adaptateurs » et
/// section « Après le MVP »). Séparé de <c>SettingsService</c> à dessein : une session de compte
/// n'est pas un réglage, elle n'a pas à voyager dans le même fichier que le thème et le nombre de
/// téléchargements simultanés.
/// </summary>
/// <remarks>
/// Une seule implémentation aujourd'hui, <see cref="FileSecretStore"/>, adossée à un fichier en
/// permissions restrictives. Le trousseau de l'OS (DPAPI, Secret Service, Keychain) est le chantier
/// suivant, et il n'aura rien d'autre à faire que d'implémenter cette interface : rien en amont ne
/// sait où vit le secret.
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Relit la session stockée. Renvoie <see langword="null"/> quand il n'y en a pas — absence
    /// normale — mais aussi quand ce qui est stocké est inexploitable : un secret illisible vaut un
    /// secret absent, l'utilisateur se reconnecte, rien ne casse au démarrage.
    /// </summary>
    Task<VsSession?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Remplace la session stockée.</summary>
    Task SaveAsync(VsSession session, CancellationToken cancellationToken = default);

    /// <summary>Efface la session stockée (déconnexion). Sans effet s'il n'y en avait pas.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
