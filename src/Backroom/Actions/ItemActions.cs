using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;

namespace PocketPlug.Backroom.Actions;

/// <summary>Giving items to the player, the way the console's "give" does, plus quality and packaging.</summary>
internal static class ItemActions
{
    /// <summary>A new instance of <paramref name="def"/> with the chosen quality and packaging applied.</summary>
    public static ItemInstance Make(ItemDefinition def, int quantity, EQuality? quality, PackagingDefinition packaging)
    {
        var instance = def.GetDefaultInstance(Math.Max(1, quantity));
        if (instance == null)
            return null;
        if (quality.HasValue)
            instance.TryCast<QualityItemInstance>()?.SetQuality(quality.Value);
        if (packaging != null)
            instance.TryCast<ProductItemInstance>()?.SetPackaging(packaging);
        return instance;
    }

    /// <summary>How many more of this exact item (quality and packaging included) the player's slots can take.</summary>
    public static int Capacity(ItemInstance sample)
    {
        if (sample == null || !PlayerSingleton<PlayerInventory>.InstanceExists)
            return 0;
        int total = 0;
        var slots = PlayerSingleton<PlayerInventory>.Instance.hotbarSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot != null)
                total += Math.Max(0, slot.GetCapacityForItem(sample, false));
        }
        return total;
    }

    /// <summary>Gives up to <paramref name="quantity"/>, one stack at a time; returns how many were given.</summary>
    public static int Give(ItemDefinition def, int quantity, EQuality? quality, PackagingDefinition packaging)
    {
        if (def == null || quantity <= 0 || !PlayerSingleton<PlayerInventory>.InstanceExists)
            return 0;
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
        int stack = Math.Max(1, def.StackLimit);
        int given = 0;
        while (given < quantity)
        {
            int n = Math.Min(stack, quantity - given);
            var instance = Make(def, n, quality, packaging);
            if (instance == null)
                break;
            int room = Capacity(instance);
            if (room <= 0)
                break;
            if (room < n)
            {
                n = room;
                instance = Make(def, n, quality, packaging);
            }
            if (!inventory.CanItemFitInInventory(instance, n))
                break;
            inventory.AddItemToInventory(instance);
            given += n;
        }
        return given;
    }
}
