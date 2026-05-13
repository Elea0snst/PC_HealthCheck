namespace PC_HealthCheck.Core;

public enum StressTestProfile
{
    Quick5m,
    Stability30m,
    Overclock60m
}

public static class StressTestProfileDefaults
{
    public static int Seconds(this StressTestProfile profile) => profile switch
    {
        StressTestProfile.Quick5m => 5 * 60,
        StressTestProfile.Stability30m => 30 * 60,
        StressTestProfile.Overclock60m => 60 * 60,
        _ => 5 * 60
    };

    public static string Label(this StressTestProfile profile) => profile switch
    {
        StressTestProfile.Quick5m => "Быстро (5 минут)",
        StressTestProfile.Stability30m => "Стабильность (30 минут)",
        StressTestProfile.Overclock60m => "После разгона (60 минут)",
        _ => profile.ToString()
    };
}

