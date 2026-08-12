namespace Prospect.Tests.Live;

/// <summary>
/// Un test qui interroge le vrai ModDB, ignoré tant que <see cref="LiveOptIn.EnvironmentVariableName"/>
/// ne vaut pas <c>"1"</c>. Variante « sans interface » de l'étage live, pour les tests qui
/// n'ont besoin ni de fenêtre ni de fil UI Avalonia.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    /// <summary>Construit l'attribut, en lisant l'opt-in immédiatement.</summary>
    public LiveFactAttribute() => Skip = LiveOptIn.SkipReason;
}