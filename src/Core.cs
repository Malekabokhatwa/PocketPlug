using System;
using MelonLoader;
using PocketPlug.Features;
using PocketPlug.Phone;

[assembly: MelonInfo(typeof(PocketPlug.Core), "PocketPlug", "1.0.0", "Malekabokhatwa",
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
        StackLimits.ApplyAll();
        Apps.OnSceneChanged();
        BankHistory.OnSceneChanged();
        ReadyAlerts.Reset();
        DealReminders.Reset();
        DealerTransfers.Reset();
    }

    public override void OnUpdate()
    {
        float now = UnityEngine.Time.unscaledTime;
        Config.ReloadIfChanged(now);
        DevTools.Poll(now);

        Safe(SkateStamina.Update);
        Safe(BankHistory.Update);
        Safe(Apps.Update);
        Safe(ReadyAlerts.Update);
        Safe(DealReminders.Update);
        Safe(DealerTransfers.Update);
    }

    public override void OnLateUpdate()
    {
        // After the game's CompassManager.Update has written its own labels.
        Safe(CompassDeals.LateUpdate);
    }

    private static void OnConfigChanged()
    {
        StackLimits.ApplyAll();
        CompassDeals.Refresh();
        Apps.Refresh();
    }

    private static string _lastError;

    /// <summary>Runs a per-frame feature without letting one exception spam the log every frame.</summary>
    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            string message = e.ToString();
            if (message != _lastError)
            {
                _lastError = message;
                Log.Error(message);
            }
        }
    }
}
