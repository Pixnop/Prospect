using Prospect.Core.Migration;

using Shouldly;

namespace Prospect.Core.Tests.Migration;

public class VslInstanceMapperTests
{
    private static VslInstallation Installation(
        string startParams = "",
        string envVars = "",
        bool mesaGlThread = false,
        long lastTimePlayedMs = -1,
        long totalTimePlayedMs = 0)
        => new()
        {
            Id = "a1b2c3",
            Name = "Survie médiévale",
            Path = "/data/survie",
            Version = "1.20.4",
            StartParams = startParams,
            EnvVars = envVars,
            MesaGlThread = mesaGlThread,
            LastTimePlayedMs = lastTimePlayedMs,
            TotalTimePlayedMs = totalTimePlayedMs,
        };

    [Fact]
    public void ToLaunchSettings_StartParamsWithSeveralFlags_TokenizesIntoExtraArgs()
    {
        var settings = VslInstanceMapper.ToLaunchSettings(Installation(startParams: "-logexcept -tracelog"));

        settings.ExtraArgs.ShouldBe(["-logexcept", "-tracelog"]);
    }

    [Fact]
    public void ToLaunchSettings_EnvVars_AreParsedIntoDictionary()
    {
        var settings = VslInstanceMapper.ToLaunchSettings(Installation(envVars: "DXVK_HUD=fps"));

        settings.Env.ShouldContainKeyAndValue("DXVK_HUD", "fps");
    }

    [Fact]
    public void ToLaunchSettings_MesaGlThreadEnabled_BecomesTheTypedOptionNotAnEnvEntry()
    {
        var settings = VslInstanceMapper.ToLaunchSettings(Installation(mesaGlThread: true));

        // Chez nous la variable est posee par la strategie de lancement Linux, sous le nom que Mesa
        // lit reellement : l'importer en variable la ferait suivre l'instance sur les autres OS.
        settings.MesaGlThread.ShouldBeTrue();
        settings.Env.ShouldNotContainKey("MESA_GLTHREAD");
        settings.Env.ShouldNotContainKey("mesa_glthread");
    }

    [Fact]
    public void ToLaunchSettings_MesaGlThreadDisabled_LeavesTheOptionOff()
    {
        var settings = VslInstanceMapper.ToLaunchSettings(Installation(mesaGlThread: false));

        settings.MesaGlThread.ShouldBeFalse();
        settings.Env.ShouldNotContainKey("MESA_GLTHREAD");
    }

    [Fact]
    public void ToLaunchSettings_LegacyVariableInEnvVars_IsLiftedIntoTheOptionAndRemoved()
    {
        var settings = VslInstanceMapper.ToLaunchSettings(Installation(envVars: "MESA_GLTHREAD=true", mesaGlThread: false));

        // Une seule intention, un seul endroit : gardee dans env, l'ancienne cle serait inerte
        // (Mesa ne la lit pas) tout en donnant l'impression que l'option est active deux fois.
        settings.MesaGlThread.ShouldBeTrue();
        settings.Env.ShouldNotContainKey("MESA_GLTHREAD");
    }

    [Fact]
    public void ToLaunchSettings_MesaGlThreadEnabled_KeepsOtherEnvVarsEntries()
    {
        var settings = VslInstanceMapper.ToLaunchSettings(Installation(envVars: "DXVK_HUD=fps", mesaGlThread: true));

        settings.MesaGlThread.ShouldBeTrue();
        settings.Env.Count.ShouldBe(1);
        settings.Env.ShouldContainKeyAndValue("DXVK_HUD", "fps");
    }

    [Fact]
    public void ToLaunchSettings_NullInstallation_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => VslInstanceMapper.ToLaunchSettings(null!));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-100L)]
    public void ToLastLaunchedUtc_NeverPlayedSentinelOrNegative_ReturnsNull(long lastTimePlayedMs)
    {
        VslInstanceMapper.ToLastLaunchedUtc(lastTimePlayedMs).ShouldBeNull();
    }

    [Fact]
    public void ToLastLaunchedUtc_PositiveEpochMilliseconds_ConvertsToDateTimeOffset()
    {
        // 1770000000000 ms depuis l'epoch Unix == 2026-02-01T21:20:00Z (vérifié : 1770000000 s).
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(1770000000000L);

        VslInstanceMapper.ToLastLaunchedUtc(1770000000000L).ShouldBe(expected);
    }

    [Fact]
    public void ToLastLaunchedUtc_Zero_IsTreatedAsAValidTimestampNotNeverPlayed()
    {
        // Zéro est un epoch valide (1970-01-01), distinct de la sentinelle -1 : seule une valeur
        // strictement négative signifie « jamais joué ».
        VslInstanceMapper.ToLastLaunchedUtc(0L).ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(0L));
    }

    [Fact]
    public void ToTotalPlaytimeSeconds_ConvertsMillisecondsToWholeSeconds()
    {
        VslInstanceMapper.ToTotalPlaytimeSeconds(3600000L).ShouldBe(3600L);
    }

    [Fact]
    public void ToTotalPlaytimeSeconds_TruncatesPartialSeconds()
    {
        VslInstanceMapper.ToTotalPlaytimeSeconds(1999L).ShouldBe(1L);
    }

    [Fact]
    public void ToTotalPlaytimeSeconds_Zero_ReturnsZero()
    {
        VslInstanceMapper.ToTotalPlaytimeSeconds(0L).ShouldBe(0L);
    }

    [Fact]
    public void ToTotalPlaytimeSeconds_Negative_ClampsToZero()
    {
        VslInstanceMapper.ToTotalPlaytimeSeconds(-500L).ShouldBe(0L);
    }
}