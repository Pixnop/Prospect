using Prospect.Core.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Storage;

/// <summary>
/// Test de fumée sur l'adaptateur réel : vérifie qu'il reflète bien l'environnement système
/// plutôt que de re-tester le framework lui-même (les scénarios par OS sont couverts via
/// <see cref="FakeAppEnvironment"/> dans <see cref="AppPathsTests"/>).
/// </summary>
public class SystemAppEnvironmentTests
{
    [Fact]
    public void CurrentOperatingSystem_MatchesRuntimeOperatingSystem()
    {
        var environment = new SystemAppEnvironment();

        var expected = OperatingSystem.IsWindows() ? AppOperatingSystem.Windows
            : OperatingSystem.IsMacOS() ? AppOperatingSystem.MacOs
            : AppOperatingSystem.Linux;

        environment.CurrentOperatingSystem.ShouldBe(expected);
    }

    [Fact]
    public void GetEnvironmentVariable_KnownVariable_ReturnsSameValueAsFramework()
    {
        var environment = new SystemAppEnvironment();
        var variableName = OperatingSystem.IsWindows() ? "USERNAME" : "HOME";

        environment.GetEnvironmentVariable(variableName).ShouldBe(Environment.GetEnvironmentVariable(variableName));
    }

    [Fact]
    public void GetEnvironmentVariable_UnknownVariable_ReturnsNull()
    {
        var environment = new SystemAppEnvironment();

        environment.GetEnvironmentVariable("PROSPECT_THIS_VARIABLE_DOES_NOT_EXIST").ShouldBeNull();
    }

    [Fact]
    public void GetFolderPath_UserProfile_ReturnsSameValueAsFramework()
    {
        var environment = new SystemAppEnvironment();

        environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .ShouldBe(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }
}