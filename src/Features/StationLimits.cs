using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Features;

/// <summary>
/// Bigger batches for mixing stations (Mk1 and Mk2 separately), bigger drying racks, and Half Mixing Time.
/// Mix time is MixTimePerItem x quantity in the game, so time scales with the batch on its own.
/// Every limit is clamped to the product stack limit in force, because a finished batch is added to the output
/// slot in one go and must fit there.
/// </summary>
internal static class StationLimits
{
    /// <summary>Vanilla product stack limit, used when Bigger stacks is off.</summary>
    private const int VanillaProductStack = 20;

    private static readonly Dictionary<int, int> MixDefaults = new();
    private static readonly Dictionary<int, int> RackDefaults = new();

    /// <summary>The biggest batch an output slot can take right now.</summary>
    public static int OutputCap =>
        Config.StackLimits.On && Config.LimitFor(ItemGroup.Product) > VanillaProductStack
            ? Math.Min(Config.LimitFor(ItemGroup.Product), Config.MaxStackLimit)
            : VanillaProductStack;

    /// <summary>What a setting actually applies as (0 = game default, otherwise clamped to the output cap).</summary>
    public static int Effective(StackSetting setting, int vanilla)
    {
        if (!Config.StationLimits.On || setting.Entry.Value <= 0)
            return vanilla;
        return Math.Max(vanilla, Math.Min(setting.Entry.Value, OutputCap));
    }

    public static void Apply(MixingStation station)
    {
        if (station == null)
            return;
        int key = station.GetInstanceID();
        if (!MixDefaults.TryGetValue(key, out int vanilla))
        {
            vanilla = station.MaxMixQuantity;
            MixDefaults[key] = vanilla;
        }
        var setting = station.TryCast<MixingStationMk2>() != null ? Config.Mk2MixLimit : Config.Mk1MixLimit;
        int target = Effective(setting, vanilla);
        if (station.MaxMixQuantity == target)
            return;
        station.MaxMixQuantity = target;
        // The start threshold slider was sized from the old maximum when the station was placed.
        Resize(station.stationConfiguration?.StartThrehold, target);
    }

    public static void Apply(DryingRack rack)
    {
        if (rack == null)
            return;
        int key = rack.GetInstanceID();
        if (!RackDefaults.TryGetValue(key, out int vanilla))
        {
            vanilla = rack.ItemCapacity;
            RackDefaults[key] = vanilla;
        }
        int target = Effective(Config.DryingRackCapacity, vanilla);
        if (rack.ItemCapacity == target)
            return;
        rack.ItemCapacity = target;
        Resize(rack.stationConfiguration?.StartThreshold, target);
    }

    /// <summary>
    /// Resizes a station's start threshold. Configure doesn't clamp the stored value, and employees wait until a
    /// batch reaches it, so a threshold left above a lowered maximum would stall them for good.
    /// </summary>
    private static void Resize(Il2CppScheduleOne.Management.NumberField threshold, int max)
    {
        if (threshold == null)
            return;
        threshold.Configure(1f, max, true);
        if (threshold.Value > max)
            threshold.SetValue(max, true);
    }

    /// <summary>Re-applies to every station at owned properties (after a settings change).</summary>
    public static void ApplyAll()
    {
        var owned = Property.OwnedProperties;
        for (int i = 0; i < owned.Count; i++)
        {
            var items = owned[i]?.BuildableItems;
            if (items == null)
                continue;
            for (int j = 0; j < items.Count; j++)
            {
                var item = items[j];
                if (item == null)
                    continue;
                var mixer = item.TryCast<MixingStation>();
                if (mixer != null)
                {
                    Apply(mixer);
                    continue;
                }
                var rack = item.TryCast<DryingRack>();
                if (rack != null)
                    Apply(rack);
            }
        }
    }

    public static void Reset()
    {
        MixDefaults.Clear();
        RackDefaults.Clear();
    }
}

[HarmonyPatch(typeof(MixingStation), nameof(MixingStation.Awake))]
internal static class MixingStationAwakePatch
{
    private static void Postfix(MixingStation __instance)
    {
        Util.Perf.Begin();
        StationLimits.Apply(__instance);
        Util.Perf.End("StationLimits.Awake(mixer)");
    }
}

[HarmonyPatch(typeof(DryingRack), nameof(DryingRack.Awake))]
internal static class DryingRackAwakePatch
{
    private static void Postfix(DryingRack __instance)
    {
        Util.Perf.Begin();
        StationLimits.Apply(__instance);
        Util.Perf.End("StationLimits.Awake(rack)");
    }
}

/// <summary>
/// Half Mixing Time: mixes run their clock at double speed, so 20 items on a Mk2 take 30 minutes instead of 60.
/// The game's total and saved progress stay in its own minutes (toggling or removing the mod never strands a mix);
/// only the countdowns are shown in real minutes. GetMixTimeForCurrentOperation can't be patched instead: the
/// game's native code inlines it.
/// </summary>
internal static class HalfMixTime
{
    public static bool Active(MixingStation station) => Config.HalfMixTime.On && station.CurrentMixOperation != null;

    /// <summary>Real minutes left, rounded up.</summary>
    public static int Remaining(MixingStation station) =>
        (Math.Max(station.GetMixTimeForCurrentOperation() - station.CurrentMixTime, 0) + 1) / 2;
}

[HarmonyPatch(typeof(MixingStation), nameof(MixingStation.OnTimePass))]
internal static class MixingStationTimePassPatch
{
    private static void Prefix(MixingStation __instance, ref int minutes)
    {
        if (HalfMixTime.Active(__instance))
            minutes *= 2;
    }

    private static void Postfix(MixingStation __instance)
    {
        if (HalfMixTime.Active(__instance) && __instance.Clock != null)
            __instance.Clock.DisplayMinutes(HalfMixTime.Remaining(__instance));
    }
}

/// <summary>The Mk2 screen writes its own "mins remaining" after the base tick and when a mix starts.</summary>
[HarmonyPatch(typeof(MixingStationMk2))]
internal static class MixingStationMk2ScreenPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(MixingStationMk2.OnTimePass))]
    private static void AfterTimePass(MixingStationMk2 __instance) => Relabel(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MixingStationMk2.MixingStart))]
    private static void AfterStart(MixingStationMk2 __instance) => Relabel(__instance);

    private static void Relabel(MixingStationMk2 station)
    {
        if (HalfMixTime.Active(station) && station.ProgressLabel != null)
            station.ProgressLabel.text = HalfMixTime.Remaining(station) + " mins remaining";
    }
}
