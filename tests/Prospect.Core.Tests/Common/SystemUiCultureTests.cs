using System.Globalization;

using Prospect.Core.Common;

using Shouldly;

namespace Prospect.Core.Tests.Common;

public class SystemUiCultureTests
{
    [Fact]
    public void Name_IsTheCurrentUiCultureOfTheProcess()
    {
        var culture = new SystemUiCulture();

        culture.Name.ShouldBe(CultureInfo.CurrentUICulture.Name);
    }

    [Fact]
    public void Name_FollowsTheAmbientCulture()
    {
        // Preuve que l'adaptateur LIT la culture à chaque appel plutôt que d'en capturer une au
        // démarrage : c'est ce qui rend le port honnête vis-à-vis de son contrat.
        var previous = CultureInfo.CurrentUICulture;
        var culture = new SystemUiCulture();

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            culture.Name.ShouldBe("fr-FR");

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            culture.Name.ShouldBe("en-US");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}