using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Features;

/// <summary>
/// Bigger batches for mixing stations (Mk1 and Mk2 separately) and bigger drying racks.
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
            ? Config.LimitFor(ItemGroup.Product)
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
        station.stationConfiguration?.StartThrehold?.Configure(1f, target, true);
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
        rack.stationConfiguration?.StartThreshold?.Configure(1f, target, true);
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
