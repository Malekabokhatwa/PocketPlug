using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.UI;
using PocketPlug.Util;

namespace PocketPlug.Features;

/// <summary>
/// Notifies you when an employee can't work because they're out of supplies or their output has nowhere to go.
/// The game reports these through Employee.SubmitNoWorkReason, repeatedly while the employee is idle, so each
/// employee/reason pair alerts at most once per in-game day. Shift end, "nothing to do", locker and pay reasons are
/// ignored (pay is handled by the Payroll app).
/// </summary>
[HarmonyPatch(typeof(Employee), nameof(Employee.SubmitNoWorkReason))]
internal static class EmployeeAlerts
{
    /// <summary>Employee, reason and fix seen today (alerted or ignored), so each pair is only looked at once.</summary>
    private static readonly HashSet<(int, string, string)> SeenToday = new();

    /// <summary>Called by the morning routine so blocked employees get reminded again the next day.</summary>
    public static void NewDay() => SeenToday.Clear();

    public static void Reset() => SeenToday.Clear();

    private static void Postfix(Employee __instance, string reason, string fix)
    {
        if (!Config.EmployeeAlerts.On || __instance == null || string.IsNullOrEmpty(reason))
            return;

        // The game repeats this every tick while the employee is idle: check the cheap key before anything else.
        if (!SeenToday.Add((__instance.GetInstanceID(), reason, fix)))
            return;

        var (title, detail) = Classify(reason, fix);
        if (title == null || !Singleton<NotificationsManager>.InstanceExists)
            return;

        Singleton<NotificationsManager>.Instance.SendNotification(
            $"{__instance.FirstName} {title}", detail, Npcs.Mugshot(__instance), 6f, true);
    }

    /// <summary>Title and detail for reasons worth an alert; a null title means ignore.</summary>
    private static (string title, string detail) Classify(string reason, string fix)
    {
        string r = reason.ToLowerInvariant();
        string f = (fix ?? "").ToLowerInvariant();
        if (r.Contains("don't have any") || r.Contains("do not have any"))
            return ("is out of supplies", reason);
        // The packager's reason is the generic "nothing to do"; only the fix text says what's missing.
        if (f.Contains("enough product and packaging"))
            return ("is out of supplies", "Their stations need product and packaging.");
        if (r.Contains("destination"))
            return ("can't store their output", reason);
        return (null, null);
    }
}
