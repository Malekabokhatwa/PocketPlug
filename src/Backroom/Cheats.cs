using Il2CppScheduleOne.Police;

namespace PocketPlug.Backroom;

/// <summary>
/// Backroom switches that must keep applying while the menu is closed. "Police ignore you" covers officers that
/// spawn later, which the console's one-shot command doesn't.
/// </summary>
internal static class Cheats
{
    public static bool PoliceIgnore { get; private set; }
    private static float _next;

    public static void SetPoliceIgnore(bool on)
    {
        PoliceIgnore = on;
        Apply(on);
    }

    public static void Reset()
    {
        PoliceIgnore = false;
        _next = 0f;
    }

    /// <summary>Every 2 seconds: re-applies to officers that spawned since.</summary>
    public static void Update()
    {
        if (!PoliceIgnore || UnityEngine.Time.unscaledTime < _next)
            return;
        _next = UnityEngine.Time.unscaledTime + 2f;
        Apply(true);
    }

    private static void Apply(bool ignore)
    {
        var officers = PoliceOfficer.Officers;
        if (officers == null)
            return;
        for (int i = 0; i < officers.Count; i++)
        {
            var officer = officers[i];
            if (officer != null)
                officer.SetIgnorePlayers(ignore);
        }
    }
}
