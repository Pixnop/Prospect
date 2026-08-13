using Prospect.Core.Common;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Double de test d'<see cref="IUiCulture"/> : la culture d'interface est fixée par le test plutôt
/// que lue de la machine, sans quoi la langue par défaut d'une installation neuve dépendrait de
/// l'OS qui exécute la suite (la CI tourne en <c>en-US</c>, un poste de dev français en
/// <c>fr-FR</c>). Même rôle que <see cref="FakeClock"/> pour l'heure.
/// </summary>
internal sealed class FakeUiCulture(string name) : IUiCulture
{
    /// <summary>Culture française, celle que la suite épingle par défaut.</summary>
    public static FakeUiCulture French => new("fr-FR");

    /// <inheritdoc />
    public string Name { get; } = name;
}