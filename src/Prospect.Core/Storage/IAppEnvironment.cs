namespace Prospect.Core.Storage;

/// <summary>
/// Système d'exploitation courant, au sens où <see cref="AppPaths"/> en a besoin pour choisir
/// sa racine par défaut. Volontairement une énumération fermée à trois valeurs plutôt que trois
/// booléens indépendants : un état comme « à la fois Windows et Linux » ne doit pas pouvoir
/// exister.
/// </summary>
public enum AppOperatingSystem
{
    Linux,
    Windows,
    MacOs,
}

/// <summary>
/// Port vers les effets de bord dont dépend l'emplacement des données de l'application :
/// variables d'environnement, dossiers spéciaux, système d'exploitation courant. Toute la
/// logique du Core qui a besoin de ces informations dépend de cette interface plutôt que
/// d'appeler <see cref="Environment"/> directement, ce qui la rend testable pour les trois OS
/// cibles indépendamment de celui qui exécute réellement les tests.
/// </summary>
public interface IAppEnvironment
{
    /// <summary>Système d'exploitation sur lequel Prospect s'exécute.</summary>
    AppOperatingSystem CurrentOperatingSystem { get; }

    /// <summary>
    /// Équivalent de <see cref="Environment.GetEnvironmentVariable(string)"/> : la valeur de la
    /// variable d'environnement <paramref name="name"/>, ou <see langword="null"/> si elle
    /// n'est pas définie.
    /// </summary>
    string? GetEnvironmentVariable(string name);

    /// <summary>Équivalent de <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>.</summary>
    string GetFolderPath(Environment.SpecialFolder folder);
}