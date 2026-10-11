using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using PocketPlug.Backroom.Kit;
using GameManager = Il2CppScheduleOne.DevUtilities.GameManager;

namespace PocketPlug.Backroom.Pages;

/// <summary>Map regions, quests, product discovery and the tutorial.</summary>
internal sealed class ProgressPage : ListPage
{
    public override string Id => "progress";
    public override string Title => "Progress";
    public override string Icon => "trophy";

    private static readonly EQuestState[] States = { EQuestState.Inactive, EQuestState.Active, EQuestState.Completed, EQuestState.Failed, EQuestState.Expired, EQuestState.Cancelled };

    protected override void Define()
    {
        Section("Map");
        Link("Regions", "Unlock regions of the map.", _ => DefineRegions(), "map-pin",
            () => $"{UnlockedRegions()} / {(Singleton<Map>.InstanceExists ? Singleton<Map>.Instance.Regions.Length : 0)} unlocked");

        Section("Story");
        Link("Quests", "Set any quest's state.", _ => DefineQuests(), "flag", () => $"{Quest.Quests?.Count ?? 0} quests");
        Link("Discover products", "Mark products as discovered, so they show up for you and your customers.", _ => DefineProducts(), "sparkles",
            () => $"{ProductManager.DiscoveredProducts?.Count ?? 0} / {(NetworkSingleton<ProductManager>.InstanceExists ? NetworkSingleton<ProductManager>.Instance.AllProducts.Count : 0)}");
        Action("End the tutorial", "Finishes the tutorial right away.", () =>
        {
            NetworkSingleton<GameManager>.Instance.EndTutorial(false);
            Done("Tutorial ended");
        }, enabled: () => NetworkSingleton<GameManager>.InstanceExists && NetworkSingleton<GameManager>.Instance.IsTutorial);
    }

    private static int UnlockedRegions()
    {
        if (!Singleton<Map>.InstanceExists)
            return 0;
        int n = 0;
        foreach (var r in Singleton<Map>.Instance.Regions)
        {
            if (r != null && r.IsUnlocked)
                n++;
        }
        return n;
    }

    private void DefineRegions()
    {
        Section("Regions");
        if (!Singleton<Map>.InstanceExists)
            return;
        foreach (var r in Singleton<Map>.Instance.Regions)
        {
            if (r == null)
                continue;
            var region = r;
            string name = string.IsNullOrEmpty(r.Name) ? K.Pretty(r.Region.ToString()) : r.Name;
            Action($"Unlock {name}", $"Opens up {name} on the map.", () =>
            {
                region.SetUnlocked();
                Done($"{name} unlocked");
            }, "lock", () => region.IsUnlocked ? "Unlocked" : "Locked", enabled: () => !region.IsUnlocked);
        }
    }

    private void DefineQuests()
    {
        Section("Quests");
        var quests = new List<Quest>();
        foreach (var q in Quest.Quests)
        {
            if (q != null && !string.IsNullOrEmpty(q.Title))
                quests.Add(q);
        }
        quests.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        foreach (var q in quests)
        {
            var quest = q;
            var names = Array.ConvertAll(States, s => s.ToString());
            Choice(q.Title, $"Sets the state of “{q.Title}”.", () => names, () => Array.IndexOf(States, quest.State), i =>
            {
                quest.SetQuestState(States[i], true);
                Done($"{quest.Title}: {States[i]}");
            }, "flag");
        }
    }

    private void DefineProducts()
    {
        Section("Products");
        if (!NetworkSingleton<ProductManager>.InstanceExists)
            return;
        var all = NetworkSingleton<ProductManager>.Instance.AllProducts;
        var list = new List<ProductDefinition>();
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null)
                list.Add(all[i]);
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var p in list)
        {
            var product = p;
            Action(p.Name, $"Marks {p.Name} as discovered.", () =>
            {
                NetworkSingleton<ProductManager>.Instance.DiscoverProduct(product.ID);
                Done($"Discovered {product.Name}");
            }, value: () => IsDiscovered(product) ? "Discovered" : "Unknown", enabled: () => !IsDiscovered(product));
        }
    }

    private static bool IsDiscovered(ProductDefinition p)
    {
        var discovered = ProductManager.DiscoveredProducts;
        if (discovered == null)
            return false;
        for (int i = 0; i < discovered.Count; i++)
        {
            if (discovered[i] != null && discovered[i].Pointer == p.Pointer)
                return true;
        }
        return false;
    }
}
