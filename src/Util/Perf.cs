using System.Diagnostics;
using UnityEngine;

namespace PocketPlug.Util;

/// <summary>
/// Dev-mode timing: logs slow frames (hitches) and any PocketPlug feature that takes more than 2 ms in a frame.
/// </summary>
internal static class Perf
{
    public static bool Enabled;
    private const float HitchSeconds = 0.15f;
    private static readonly Stopwatch Watch = new();

    public static void Begin()
    {
        if (Enabled)
            Watch.Restart();
    }

    public static void End(string feature)
    {
        if (!Enabled)
            return;
        Watch.Stop();
        double ms = Watch.Elapsed.TotalMilliseconds;
        if (ms > 2.0)
            Core.Log.Msg($"perf: {feature} took {ms:F1} ms");
    }

    public static void CheckFrame()
    {
        if (Enabled && Time.unscaledDeltaTime > HitchSeconds && Time.frameCount > 100)
            Core.Log.Msg($"perf: HITCH {Time.unscaledDeltaTime * 1000f:F0} ms at {Time.realtimeSinceStartup:F1}s");
    }
}
