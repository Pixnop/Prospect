using Prospect.Core.Instances;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

public class InstanceLaunchSettingsTests
{
    [Fact]
    public void Equals_SameContentDifferentCollectionInstances_ReturnsTrue()
    {
        var a = new InstanceLaunchSettings
        {
            ExtraArgs = new List<string> { "--foo", "--bar" },
            Env = new Dictionary<string, string> { ["MESA_GLTHREAD"] = "true" },
        };
        var b = new InstanceLaunchSettings
        {
            ExtraArgs = new List<string> { "--foo", "--bar" },
            Env = new Dictionary<string, string> { ["MESA_GLTHREAD"] = "true" },
        };

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equals_EnvKeysInDifferentOrder_ReturnsTrue()
    {
        var a = new InstanceLaunchSettings
        {
            Env = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" },
        };
        var b = new InstanceLaunchSettings
        {
            Env = new Dictionary<string, string> { ["B"] = "2", ["A"] = "1" },
        };

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equals_ExtraArgsInDifferentOrder_ReturnsFalse()
    {
        // ExtraArgs devient ArgumentList : l'ordre est significatif pour la ligne de commande.
        var a = new InstanceLaunchSettings { ExtraArgs = ["--foo", "--bar"] };
        var b = new InstanceLaunchSettings { ExtraArgs = ["--bar", "--foo"] };

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void Equals_DifferentEnvValue_ReturnsFalse()
    {
        var a = new InstanceLaunchSettings { Env = new Dictionary<string, string> { ["A"] = "1" } };
        var b = new InstanceLaunchSettings { Env = new Dictionary<string, string> { ["A"] = "2" } };

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void Equals_DifferentEnvCount_ReturnsFalse()
    {
        var a = new InstanceLaunchSettings
        {
            Env = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" },
        };
        var b = new InstanceLaunchSettings { Env = new Dictionary<string, string> { ["A"] = "1" } };

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var a = InstanceLaunchSettings.Empty;

        a.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void Empty_HasNoArgsAndNoEnv()
    {
        InstanceLaunchSettings.Empty.ExtraArgs.ShouldBeEmpty();
        InstanceLaunchSettings.Empty.Env.ShouldBeEmpty();
    }
}