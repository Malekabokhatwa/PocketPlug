using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using PocketPlug.Features;
using PocketPlug.Phone;
using PocketPlug.Util;

[assembly: MelonInfo(typeof(PocketPlug.Core), "PocketPlug", "2.1.1", "Malekabokhatwa",
    "https://github.com/Malekabokhatwa/PocketPlug")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace PocketPlug;

public sealed class Core : MelonMod
{
    internal static MelonLogger.Instance Log;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Config.Init();
        Config.Changed += OnConfigChanged;
        DevTools.Init();
        Log.Msg($"Loaded ({string.Join(", ", Config.Summary())}).");
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Log.Msg($"Scene ready: {sceneName}");
        // Each step guarded on its own: one failure must not leave the previous save's state in the others.
        Safe(StationLimits.Reset);
        Safe(Apps.OnSceneChanged);
        Safe(BankHistory.OnSceneChanged);
        Safe(ReadyAlerts.Reset);
        Safe(DealReminders.Reset);
        Safe(DealerTransfers.Reset);
        Safe(EmployeeAlerts.Reset);
        Safe(Payroll.Reset);
        Safe(Daily.Reset);
        Safe(StackLimits.ApplyAll);

        // One-time costs (first IL2CPP casts, opening files) happen here, behind the loading screen.
        if (sceneName == "Main")
        {
            Perf.Begin();
            Safe(ReadyAlerts.Warmup);
            Safe(BankHistory.Warmup);
            Safe(DealerTransfers.Warmup);
            Perf.End("Warmup (loading screen)");
        }
    }

    public override void OnUpdate()
    {
        float now = UnityEngine.Time.unscaledTime;
        Perf.CheckFrame();
        Safe(ReloadConfig);
        DevTools.Poll(now);

        Safe(SkateStamina.Update);
        Safe(BankHistory.Update);
        Safe(Apps.Update);
        Safe(ReadyAlerts.Update);
        Safe(DealReminders.Update);
        Safe(DealerTransfers.Update);
        Safe(Daily.Update);
    }

    public override void OnLateUpdate()
    {
        // After the game's CompassManager.Update has written its own labels.
        Safe(CompassDeals.LateUpdate);
    }

    private static void ReloadConfig() => Config.ReloadIfChanged(UnityEngine.Time.unscaledTime);

    private static void OnConfigChanged()
    {
        Safe(StackLimits.ApplyAll);
        Safe(StationLimits.ApplyAll);
        Safe(CompassDeals.Refresh);
        Safe(Apps.Refresh);
    }

    private static readonly Dictionary<MethodInfo, string> LastErrors = new();

    /// <summary>Runs a feature step without letting one exception spam the log every frame.</summary>
    private static void Safe(Action action)
    {
        Perf.Begin();
        try
        {
            action();
        }
        catch (Exception e)
        {
            string message = e.ToString();
            if (!LastErrors.TryGetValue(action.Method, out var last) || last != message)
            {
                LastErrors[action.Method] = message;
                Log.Error(message);
            }
        }
        finally
        {
            Perf.End(action.Method.DeclaringType?.Name);
        }
    }
}
