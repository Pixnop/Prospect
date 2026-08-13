using Prospect.Core.Common;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Double de test d'<see cref="IUiCulture"/> : la culture d'interface est fixée par le test plutôt
/// que lue de la machine. Le français est le défaut, comme partout dans ce harnais (voir
/// <see cref="TestAppBuilder"/>), pour qu'aucune assertion ne dépende de la langue de l'OS qui
/// exécute la suite — la CI tourne en <c>en-US</c>.
/// </summary>
internal sealed class FakeUiCulture(string name = "fr-FR") : IUiCulture
{
    /// <inheritdoc />
    public string Name { get; } = name;
}