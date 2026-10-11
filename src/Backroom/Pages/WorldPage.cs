using Il2CppScheduleOne.Core.Weather;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GamePhysics;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.Trash;
using Il2CppScheduleOne.Weather;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using Console = Il2CppScheduleOne.Console;

namespace PocketPlug.Backroom.Pages;

/// <summary>Time, weather, the law and the world's physics.</summary>
internal sealed class WorldPage : ListPage
{
    public override string Id => "world";
    public override string Title => "World";
    public override string Icon => "sun";

    private int _customTime = 1200;

    private static TimeManager Time => NetworkSingleton<TimeManager>.InstanceExists ? NetworkSingleton<TimeManager>.Instance : null;

    protected override void Define()
    {
        Section("Time");
        Readout("Time now", "The in-game day and time.", () => Time != null ? $"{Time.CurrentDay} {K.Clock(Time.CurrentTime)}" : "—", "clock");
        foreach (var (name, hhmm) in new[] { ("Morning", 700), ("Noon", 1200), ("Evening", 1800), ("Night", 2200), ("Late night", 300) })
        {
            int t = hhmm;
            Action($"{name} ({K.Clock(t)})", $"Jumps the clock to {K.Clock(t)} today.", () => SetTime(t));
        }
        Stepper("Custom time", "Pick a time, then use Set custom time. 30-minute steps (Shift: 5 hours).",
            () => ToMinutes(_customTime), v => _customTime = FromMinutes(v), 0f, 23 * 60 + 30, 30f, v => K.Clock(FromMinutes(v)));
        Action("Set custom time", "Jumps the clock to the time above.", () => SetTime(_customTime));
        Stepper("Time speed", "How fast the whole game runs (game: 1×). Affects everything, like the console's settimescale.",
            () => UnityEngine.Time.timeScale, v => UnityEngine.Time.timeScale = v, 0f, 20f, 0.5f, v => $"{v:0.0}×");
        Stepper("Day length", "Real minutes per in-game day (game: 24).", () => TimeManager.CycleDuration,
            v => Time?.SetCycleDuration(v), 4f, 240f, 1f, v => $"{v:0} min");
        Action("Sleep now", "Starts sleeping right away, wherever you are.", () =>
        {
            NetworkSingleton<SleepController>.Instance.StartSleep();
            Done("Sleeping");
        }, "moon");

        Section("Weather");
        Link("Weather", "Choose the weather.", _ => DefineWeather(), "cloud-rain");
        Action("Lightning strike", "A lightning bolt somewhere nearby.", () =>
        {
            EnvironmentHandler.TriggerLightningEvent();
            Done("Lightning");
        }, "zap");
        Action("Distant thunder", "A rumble of thunder in the distance.", () =>
        {
            EnvironmentHandler.TriggerDistantThunder();
            Done("Thunder");
        });

        Section("Law");
        Stepper("Law intensity", "How heavily the police patrol (0 to 10).", LawIntensity,
            v => Singleton<LawController>.Instance.SetInternalIntensity(v / 10f), 0f, 10f, 1f, v => $"{v:0}", "shield");

        Section("Places");
        Action("Grow all plants", "Every plant and mushroom bed finishes growing.", () =>
        {
            Console.SubmitCommand("growplants");
            Done("Everything is fully grown");
        }, "sparkles");
        Action("Clear all trash", "Removes every piece of trash in the world.", () =>
        {
            NetworkSingleton<TrashManager>.Instance.DestroyAllTrash();
            Done("Trash cleared");
        }, "trash-2");
        Stepper("Gravity", "World gravity (game: 1×).", () => NetworkSingleton<PhysicsManager>.InstanceExists ? NetworkSingleton<PhysicsManager>.Instance.GravityMultiplier : 1f,
            v => NetworkSingleton<PhysicsManager>.Instance.SetGravityMultiplier(null, v), -2f, 3f, 0.1f, v => $"{v:0.0}×");
    }

    private void DefineWeather()
    {
        Section("Weather");
        var sequences = NetworkSingleton<EnvironmentManager>.InstanceExists ? NetworkSingleton<EnvironmentManager>.Instance.WeatherSequences : null;
        if (sequences == null || sequences.Count == 0)
        {
            Readout("No weather available", "The weather system isn't ready.", () => "");
            return;
        }
        for (int i = 0; i < sequences.Count; i++)
        {
            string id = sequences[i]?.Id;
            if (string.IsNullOrEmpty(id))
                continue;
            string name = WeatherName(id);
            Action(name, $"Changes the weather to {name.ToLowerInvariant()}.", () =>
            {
                EnvironmentHandler.SetWeather(id);
                Done($"Weather: {name}");
            });
        }
    }

    /// <summary>Weather IDs are lowercase run-together words ("lightrain"); make them readable.</summary>
    private static string WeatherName(string id)
    {
        string spaced = id.Replace("light", "light ").Replace("heavy", "heavy ").Replace("partly", "partly ").Replace("_", " ").Trim();
        while (spaced.Contains("  "))
            spaced = spaced.Replace("  ", " ");
        return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
    }

    private static float LawIntensity() =>
        Singleton<LawController>.InstanceExists ? Mathf.Round(Singleton<LawController>.Instance.internalLawIntensity * 10f) : 0f;

    private static void SetTime(int hhmm)
    {
        if (Time == null)
            return;
        Time.SetTime_Server(hhmm);
        Done($"Time set to {K.Clock(hhmm)}");
    }

    private static float ToMinutes(int hhmm) => hhmm / 100 * 60 + hhmm % 100;
    private static int FromMinutes(float minutes)
    {
        int m = Mathf.Clamp(Mathf.RoundToInt(minutes), 0, 23 * 60 + 59);
        return m / 60 * 100 + m % 60;
    }
}
