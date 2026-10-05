using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;
using Object = UnityEngine.Object;

namespace PocketPlug.Features;


/// <summary>
/// Rewrites <c>StackLimit</c> on item definitions. The definition field is what the game reads everywhere
/// (inventory, storage, deliveries, shop cart, dead drops), so changing it covers every code path.
/// Items are grouped by definition type, so new mixes and modded items of the same type are included.
/// Guns, melee weapons, ammo and items that don't stack (limit 1) are never changed.
/// </summary>
internal static class StackLimits
{

    private sealed class Record
    {
        public int Original;
        public int Applied;
        public string TypeName;
    }

    private static readonly Dictionary<string, Record> Records = new();
    private static readonly HashSet<string> AmmoIds = new();

    internal static void ApplyAll()
    {
        var registry = Object.FindObjectOfType<Registry>();
        if (registry == null)
            return;

        var items = registry.GetAllItems();
        for (int i = 0; i < items.Count; i++)
            RegisterAmmo(items[i]);

        int changed = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (Apply(items[i]))
                changed++;
        }

        if (changed > 0)
            Core.Log.Msg($"Updated stack limits on {changed} of {items.Count} items.");
    }

    internal static bool Apply(ItemDefinition def)
    {
        if (def == null)
            return false;

        string id = def.ID;
        if (string.IsNullOrEmpty(id))
            return false;

        if (!Records.TryGetValue(id, out var record))
        {
            string typeName = def.GetIl2CppType().FullName;
            record = new Record { Original = OriginalFor(def.StackLimit, typeName), TypeName = typeName };
            Records[id] = record;
        }

        var group = Classify(def);
        int target = Config.StackLimits.On && group != ItemGroup.None ? Compute(group, record.Original) : record.Original;
        record.Applied = target;

        if (def.StackLimit == target)
            return false;

        def.StackLimit = target;
        return true;
    }

    /// <summary>The group's limit, but never below the original and never for items that don't stack.</summary>
    private static int Compute(ItemGroup group, int original)
    {
        int limit = System.Math.Min(Config.LimitFor(group), Config.MaxStackLimit);
        if (original <= 1 || limit <= original)
            return original;
        return limit;
    }

    /// <summary>Most specific type first: ProductDefinition derives from PropertyItemDefinition.</summary>
    private static ItemGroup Classify(ItemDefinition def)
    {
        if (IsWeapon(def) || AmmoIds.Contains(def.ID))
            return ItemGroup.None;

        if (def.TryCast<ProductDefinition>() != null)
            return ItemGroup.Product;
        if (def.TryCast<PackagingDefinition>() != null)
            return ItemGroup.Packaging;
        if (def.TryCast<PropertyItemDefinition>() != null)
            return ItemGroup.Mixer;
        if (def.TryCast<QualityItemDefinition>() != null)
            return ItemGroup.Precursor;
        if (def.TryCast<SeedDefinition>() != null)
            return ItemGroup.Seed;
        if (def.TryCast<SoilDefinition>() != null)
            return ItemGroup.Soil;
        if (def.TryCast<AdditiveDefinition>() != null)
            return ItemGroup.Additive;
        if (def.TryCast<SporeSyringeDefinition>() != null || def.TryCast<ShroomSpawnDefinition>() != null)
            return ItemGroup.ShroomSupply;
        if (def.TryCast<BuildableItemDefinition>() != null)
            return ItemGroup.Placeable;
        return ItemGroup.Other;
    }

    private static bool IsWeapon(ItemDefinition def)
    {
        var equippable = def.Equippable;
        if (equippable == null)
            return false;
        return equippable.TryCast<Equippable_RangedWeapon>() != null || equippable.TryCast<Equippable_MeleeWeapon>() != null;
    }

    /// <summary>
    /// Ammo has no type of its own: it's whatever item a gun's <c>Magazine</c> points at (e.g. shotgun shells).
    /// Returns the ammo definition if <paramref name="def"/> is a gun whose ammo wasn't known yet.
    /// </summary>
    internal static ItemDefinition RegisterAmmo(ItemDefinition def)
    {
        var gun = def?.Equippable?.TryCast<Equippable_RangedWeapon>();
        var ammo = gun?.Magazine;
        if (ammo == null || string.IsNullOrEmpty(ammo.ID) || !AmmoIds.Add(ammo.ID))
            return null;
        return ammo;
    }

    /// <summary>
    /// New product mixes are created with Instantiate(DefaultWeed) and similar, which copies a definition we may
    /// already have changed. If the value matches what we applied to a same-typed definition, use that
    /// definition's original instead, so limits don't compound.
    /// </summary>
    private static int OriginalFor(int current, string typeName)
    {
        foreach (var r in Records.Values)
        {
            if (r.TypeName == typeName && r.Applied == current && r.Applied != r.Original)
                return r.Original;
        }
        return current;
    }
}

[HarmonyPatch(typeof(Registry), nameof(Registry.AddToRegistry))]
internal static class RegistryAddToRegistryPatch
{
    private static void Postfix(ItemDefinition item)
    {
        // A modded gun registered after its ammo: put the ammo back to its original limit.
        var ammo = StackLimits.RegisterAmmo(item);
        if (ammo != null)
            StackLimits.Apply(ammo);
        StackLimits.Apply(item);
    }
}
