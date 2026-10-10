using System.Diagnostics;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace PocketPlug.Util;

/// <summary>
/// Dev-mode timing: logs slow frames (hitches), slow seconds (sustained low fps, with GC and game log counts),
/// and any PocketPlug feature that takes more than 1 ms in a frame.
/// </summary>
internal static class Perf
{
    public static bool Enabled;
    private const float HitchSeconds = 0.15f;
    private const int SlowFps = 40;
    private static readonly System.Collections.Generic.Stack<long> Starts = new();

    private static float _secondStart;
    private static int _frames;
    private static float _worst;
    private static int _gcs = -1;
    private static int _gameLogs;
    private static string _lastGameLog;
    private static bool _hooked;

    public static void Begin()
    {
        if (Enabled)
            Starts.Push(Stopwatch.GetTimestamp());
    }

    public static void End(string feature)
    {
        if (!Enabled || Starts.Count == 0)
            return;
        double ms = (Stopwatch.GetTimestamp() - Starts.Pop()) * 1000.0 / Stopwatch.Frequency;
        if (ms > 1.0)
            Core.Log.Msg($"perf: {feature} took {ms:F1} ms");
    }

    public static void CheckFrame()
    {
        if (!Enabled)
            return;
        if (!_hooked)
        {
            _hooked = true;
            Application.add_logMessageReceivedThreaded(DelegateSupport.ConvertDelegate<Application.LogCallback>(
                new System.Action<string, string, LogType>((message, _, _) =>
                {
                    _gameLogs++;
                    _lastGameLog = message;
                })));
        }

        float dt = Time.unscaledDeltaTime;
        if (dt > HitchSeconds && Time.frameCount > 100)
            Core.Log.Msg($"perf: HITCH {dt * 1000f:F0} ms at {Time.realtimeSinceStartup:F1}s");

        _frames++;
        if (dt > _worst)
            _worst = dt;
        float now = Time.realtimeSinceStartup;
        if (now - _secondStart < 1f)
            return;

        int gcs = Il2CppSystem.GC.CollectionCount(0);
        float fps = _frames / (now - _secondStart);
        if (fps < SlowFps && _gcs >= 0 && Time.frameCount > 100)
            Core.Log.Msg($"perf: SLOW {fps:F0} fps (worst {_worst * 1000f:F0} ms) | GC +{gcs - _gcs} | game logs +{_gameLogs}" +
                         (_gameLogs > 0 ? $" (last: {_lastGameLog})" : ""));
        _gcs = gcs;
        _secondStart = now;
        _frames = 0;
        _worst = 0;
        _gameLogs = 0;
    }
}
