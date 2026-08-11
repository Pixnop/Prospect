namespace Prospect.Core.Instances;

/// <summary>
/// Réglages de lancement propres à une instance (bloc <c>launch</c> d'<c>instance.json</c>).
/// <see cref="ExtraArgs"/> est une vraie liste d'arguments, jamais une chaîne collée : la
/// construction de la ligne de commande (PR de lancement) passera chaque élément séparément à
/// <c>ProcessStartInfo.ArgumentList</c>.
/// </summary>
public sealed record InstanceLaunchSettings
{
    /// <summary>Réglages par défaut d'une instance neuve : aucun argument, aucune variable.</summary>
    public static InstanceLaunchSettings Empty { get; } = new();

    /// <summary>Arguments supplémentaires passés au processus du jeu, dans l'ordre.</summary>
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];

    /// <summary>Variables d'environnement injectées dans le processus du jeu.</summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Égalité par contenu plutôt que par référence des collections : deux instances
    /// désérialisées séparément mais portant les mêmes arguments et variables doivent être
    /// égales, ce que l'égalité de record générée par le compilateur ne fait pas pour
    /// <see cref="IReadOnlyList{T}"/>/<see cref="IReadOnlyDictionary{TKey,TValue}"/> (comparées
    /// par référence faute d'override sur les types concrets sous-jacents).
    /// </summary>
    public bool Equals(InstanceLaunchSettings? other)
        => other is not null
            && ExtraArgs.SequenceEqual(other.ExtraArgs, StringComparer.Ordinal)
            && Env.Count == other.Env.Count
            && Env.All(pair => other.Env.TryGetValue(pair.Key, out var value) && value == pair.Value);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var arg in ExtraArgs)
        {
            hash.Add(arg, StringComparer.Ordinal);
        }

        foreach (var pair in Env.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}