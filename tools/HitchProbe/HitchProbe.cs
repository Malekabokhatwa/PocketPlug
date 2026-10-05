using System;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(HitchProbe.Probe), "HitchProbe", "1.0.0", "PocketPlug dev")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace HitchProbe;

/// <summary>Dev tool: logs frames over 100 ms with GC counts, so stutter can be blamed on the right thing.</summary>
public sealed class Probe : MelonMod
{
    private int _m0, _m1, _m2, _i0;
    private float _sceneTime = -1;

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"scene {sceneName} at {Time.realtimeSinceStartup:F1}s");
        if (sceneName == "Main")
            _sceneTime = Time.realtimeSinceStartup;
        // Auto-load the first save when UserData/HitchProbe.autoload exists (for unattended comparisons).
        if (sceneName == "Menu" && System.IO.File.Exists(System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "HitchProbe.autoload")))
        {
            foreach (var info in Il2CppScheduleOne.Persistence.LoadManager.SaveGames)
            {
                if (info == null)
                    continue;
                Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Persistence.LoadManager>.Instance.StartGame(info, false, true);
                break;
            }
        }
    }

    public override void OnUpdate()
    {
        int m0 = GC.CollectionCount(0), m1 = GC.CollectionCount(1), m2 = GC.CollectionCount(2);
        int i0 = Il2CppSystem.GC.CollectionCount(0);
        float dt = Time.unscaledDeltaTime;
        if (dt > 0.1f && Time.frameCount > 100)
        {
            float since = _sceneTime < 0 ? -1 : Time.realtimeSinceStartup - _sceneTime;
            LoggerInstance.Msg($"HITCH {dt * 1000f:F0} ms, {since:F1}s after Main | managed GC +{m0 - _m0}/{m1 - _m1}/{m2 - _m2} | il2cpp GC +{i0 - _i0}");
        }
        _m0 = m0; _m1 = m1; _m2 = m2; _i0 = i0;
    }
}
