using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Product;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Features;

/// <summary>
/// Mixing steps (Mk2 mixing stations): up to 20 ingredients mixed in one go, in step order, into the final product,
/// taking as long as all the single mixes together.
///
/// How it stays safe:
/// - The running operation is a normal game operation (start product + the FIRST ingredient). The rest of the chain
///   lives in PocketPlug's file; without PocketPlug the save just finishes a one-step mix.
/// - Ingredients waiting in steps other than the one shown in the Mixer slot are kept in PocketPlug's file, written
///   only when the game itself saves, so reloading without saving can never duplicate or lose them.
/// - No in-between products are ever created; only the final one (named by you if it's new).
/// </summary>
internal static class MixSteps
{
    public const int MaxSteps = 20;

    internal sealed class Step
    {
        public string Id { get; set; }
        public int Qty { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Empty => string.IsNullOrEmpty(Id) || Qty <= 0;
    }

    /// <summary>Queued steps of one station. The step at Cur is the real Mixer slot; its entry here is unused.</summary>
    internal sealed class Queue
    {
        public int Cur { get; set; }
        public List<Step> Steps { get; set; } = new();
    }

    /// <summary>A chain that is mixing (or done and waiting to be named/collected).</summary>
    internal sealed class Active
    {
        public string Product { get; set; }
        public List<string> Ingredients { get; set; } = new();
        public int Quantity { get; set; }
    }

    private sealed class Data
    {
        public Dictionary<string, Queue> Queues { get; set; } = new();
        public Dictionary<string, Active> Active { get; set; } = new();
    }

    private static Data _data;
    private static string _file;
    private static bool _saveHooked;
    private static readonly Dictionary<string, MixingStation> Stations = new();
    private static readonly Dictionary<IntPtr, float> Carry = new();

    public static void Reset()
    {
        _data = null;
        _file = null;
        Stations.Clear();
        Carry.Clear();
    }

    private static Data Store
    {
        get
        {
            if (_data != null)
                return _data;
            var file = Util.SaveKey.File("mixsteps", ".json");
            if (file == null)
                return new Data();   // still loading: a throwaway, never cached
            _file = file;
            _data = new Data();
            try
            {
                if (File.Exists(file))
                    _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(file)) ?? new Data();
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Mixing steps: couldn't read {file}: {e.Message}");
            }
            return _data;
        }
    }

    /// <summary>Written when the game saves, so PocketPlug's file always matches the save.</summary>
    private static void Save()
    {
        if (_data == null || _file == null)
            return;
        try
        {
            // Drop empty queues so the file only holds what matters.
            var empty = new List<string>();
            foreach (var (guid, q) in _data.Queues)
            {
                if (q.Cur == 0 && q.Steps.TrueForAll(s => s.Empty))
                    empty.Add(guid);
            }
            foreach (var guid in empty)
                _data.Queues.Remove(guid);
            File.WriteAllText(_file, JsonSerializer.Serialize(_data));
        }
        catch (Exception e)
        {
            Core.Log.Warning($"Mixing steps: couldn't write {_file}: {e.Message}");
        }
    }

    public static void Update()
    {
        if (_saveHooked || !PersistentSingleton<SaveManager>.InstanceExists)
            return;
        _saveHooked = true;
        PersistentSingleton<SaveManager>.Instance.onSaveComplete.AddListener((Action)Save);
    }

    // ---- stations ----

    public static bool IsMk2(MixingStation station) => station != null && station.TryCast<MixingStationMk2>() != null;

    public static string Key(MixingStation station) => station.GUID.ToString();

    private static MixingStation Resolve(string guid)
    {
        if (Stations.TryGetValue(guid, out var s) && s != null)
            return s;
        var all = Property.Properties;
        for (int i = 0; i < all.Count; i++)
        {
            var items = all[i]?.BuildableItems;
            if (items == null)
                continue;
            for (int j = 0; j < items.Count; j++)
            {
                var mix = items[j]?.TryCast<MixingStation>();
                if (mix != null && mix.GUID.ToString() == guid)
                    return Stations[guid] = mix;
            }
        }
        return null;
    }

    public static Queue QueueOf(MixingStation station, bool create)
    {
        var data = Store;
        string key = Key(station);
        if (data.Queues.TryGetValue(key, out var q))
        {
            while (q.Steps.Count < MaxSteps)
                q.Steps.Add(new Step());
            q.Cur = Mathf.Clamp(q.Cur, 0, MaxSteps - 1);
            return q;
        }
        if (!create)
            return null;
        q = new Queue();
        for (int i = 0; i < MaxSteps; i++)
            q.Steps.Add(new Step());
        data.Queues[key] = q;
        return q;
    }

    /// <summary>True while ingredients wait in steps other than the one in the Mixer slot.</summary>
    public static bool HasHidden(MixingStation station)
    {
        var q = QueueOf(station, false);
        if (q == null)
            return false;
        for (int i = 0; i < q.Steps.Count; i++)
        {
            if (i != q.Cur && !q.Steps[i].Empty)
                return true;
        }
        return false;
    }

    /// <summary>Every filled step in order: (step index, item ID, quantity). The shown step reads the real slot.</summary>
    public static List<(int index, string id, int qty)> Ordered(MixingStation station)
    {
        var list = new List<(int, string, int)>();
        var q = QueueOf(station, false);
        int cur = q?.Cur ?? 0;
        for (int i = 0; i < MaxSteps; i++)
        {
            if (i == cur)
            {
                var item = station.MixerSlot.ItemInstance;
                if (item != null && station.MixerSlot.Quantity > 0)
                    list.Add((i, item.ID, station.MixerSlot.Quantity));
            }
            else if (q != null && !q.Steps[i].Empty)
                list.Add((i, q.Steps[i].Id, q.Steps[i].Qty));
        }
        return list;
    }

    /// <summary>Highest step that can be opened: one past the last filled step.</summary>
    public static int Reachable(MixingStation station)
    {
        int last = -1;
        foreach (var (index, _, _) in Ordered(station))
            last = index;
        return Mathf.Min(last + 1, MaxSteps - 1);
    }

    public static bool IsMixer(string id)
    {
        if (!NetworkSingleton<ProductManager>.InstanceExists)
            return false;
        var def = Registry.GetItem(id)?.TryCast<PropertyItemDefinition>();
        if (def == null || def.Properties == null || def.Properties.Count == 0)
            return false;
        var valid = NetworkSingleton<ProductManager>.Instance.ValidMixIngredients;
        for (int i = 0; i < valid.Count; i++)
        {
            if (valid[i] != null && valid[i].ID == id)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Shows step <paramref name="target"/> in the Mixer slot: the slot's contents go back to their step, the
    /// target step's ingredient comes out. Returns null, or why it can't.
    /// </summary>
    public static string Switch(MixingStation station, int target)
    {
        var q = QueueOf(station, true);
        if (target < 0 || target >= MaxSteps || target == q.Cur)
            return "";
        var slot = station.MixerSlot;
        var item = slot.ItemInstance;
        if (item != null && slot.Quantity > 0)
        {
            // Only plain mixing ingredients can wait in a step (they're stored by ID and amount).
            if (!IsMixer(item.ID))
                return "Only mixing ingredients can go in a step";
            if (target > q.Cur && target > Reachable(station))
                return "";
        }
        else if (target > q.Cur && target > Reachable(station))
            return "Fill this step first";

        q.Steps[q.Cur] = item != null && slot.Quantity > 0 ? new Step { Id = item.ID, Qty = slot.Quantity } : new Step();
        slot.ClearStoredInstance(false);
        q.Cur = target;
        var next = q.Steps[target];
        q.Steps[target] = new Step();
        if (!next.Empty)
        {
            var def = Registry.GetItem(next.Id);
            if (def != null)
                slot.SetStoredItem(def.GetDefaultInstance(next.Qty), false);
            else
                Core.Log.Warning($"Mixing steps: unknown item '{next.Id}' dropped from step {target + 1}.");
        }
        return null;
    }

    // ---- the chain's result ----

    /// <summary>The effects after mixing <paramref name="ingredients"/> into <paramref name="product"/> in order.</summary>
    public static Il2CppSystem.Collections.Generic.List<Effect> Fold(ProductDefinition product, IList<string> ingredients)
    {
        var props = product.Properties;
        foreach (var id in ingredients)
        {
            var mixer = Registry.GetItem(id)?.TryCast<PropertyItemDefinition>();
            if (mixer == null || mixer.Properties.Count == 0)
                continue;
            props = EffectMixCalculator.MixProperties(props, mixer.Properties[0], product.DrugType);
        }
        return props;
    }

    public static ProductDefinition Known(ProductDefinition product, Il2CppSystem.Collections.Generic.List<Effect> props) =>
        NetworkSingleton<ProductManager>.Instance.GetKnownProduct(product.DrugTypes[0].DrugType, props);

    /// <summary>The active chain behind this operation, if the operation is a chain's.</summary>
    public static Active ChainOf(MixOperation op)
    {
        if (op == null)
            return null;
        foreach (var (guid, chain) in Store.Active)
        {
            var station = Resolve(guid);
            var current = station != null ? station.CurrentMixOperation : null;
            if (current != null && current.Pointer == op.Pointer && Matches(chain, current))
                return chain;
        }
        return null;
    }

    public static Active ChainOf(MixingStation station)
    {
        if (station == null || !Store.Active.TryGetValue(Key(station), out var chain))
            return null;
        return Matches(chain, station.CurrentMixOperation) ? chain : null;
    }

    private static bool Matches(Active chain, MixOperation op) =>
        op != null && chain.Ingredients.Count > 1 && op.ProductID == chain.Product && op.IngredientID == chain.Ingredients[0]
        && op.Quantity == chain.Quantity;

    /// <summary>Number of single mixes the station's current operation stands for (1 for a normal mix).</summary>
    public static int Length(MixingStation station) => ChainOf(station)?.Ingredients.Count ?? 1;

    // ---- starting ----

    /// <summary>Why the station can't start its steps (null: it can). Only for stations with steps in use.</summary>
    public static string CannotStart(MixingStation station, out int quantity, out List<(int index, string id, int qty)> steps)
    {
        quantity = 0;
        steps = Ordered(station);
        if (station.CurrentMixOperation != null || station.OutputSlot.Quantity > 0)
            return "Busy";
        var product = station.GetProduct();
        if (product == null)
            return "Insert unpackaged product";
        if (steps.Count == 0)
            return "Insert a mixing ingredient";
        quantity = Mathf.Min(station.ProductSlot.Quantity, station.MaxMixQuantity);
        foreach (var (_, id, qty) in steps)
        {
            if (!IsMixer(id))
                return "Invalid mixer";
            quantity = Mathf.Min(quantity, qty);
        }
        return quantity > 0 ? null : "Not enough ingredients";
    }

    /// <summary>Starts the steps: takes the same amount from the product and every step, mixes them in order.</summary>
    public static bool Begin(MixingStation station)
    {
        if (CannotStart(station, out int quantity, out var steps) != null)
            return false;
        var product = station.ProductSlot.ItemInstance.TryCast<ProductItemInstance>();
        if (product == null)
            return false;
        string productId = product.ID;
        var quality = product.Quality;
        var q = QueueOf(station, true);

        station.ProductSlot.ChangeQuantity(-quantity, false);
        var ids = new List<string>();
        foreach (var (index, id, _) in steps)
        {
            ids.Add(id);
            if (index == q.Cur)
                station.MixerSlot.ChangeQuantity(-quantity, false);
            else
            {
                var s = q.Steps[index];
                s.Qty -= quantity;
                if (s.Qty <= 0)
                    q.Steps[index] = new Step();
            }
        }

        // Register the chain before the operation exists: starting it already asks for the output (Mk2 screen).
        string key = Key(station);
        if (ids.Count > 1)
            Store.Active[key] = new Active { Product = productId, Ingredients = ids, Quantity = quantity };
        else
            Store.Active.Remove(key);
        Stations[key] = station;
        station.SendMixingOperation(new MixOperation(productId, quality, ids[0], quantity), 0);
        Core.Log.Msg($"Mixing steps: {productId} x{quantity} + {string.Join(" → ", ids)}");
        return true;
    }

    /// <summary>Game minutes for the whole chain as the game counts them, and as you'll wait (Half Mixing Time).</summary>
    public static int TotalMinutes(MixingStation station, int steps, int quantity)
    {
        int minutes = station.MixTimePerItem * quantity * steps;
        return Config.HalfMixTime.On ? (minutes + 1) / 2 : minutes;
    }

    // ---- finishing ----

    /// <summary>Puts the final product in the output (no single-step recipe is recorded: none describes a chain).</summary>
    public static bool TryFinish(MixingStation station)
    {
        var chain = ChainOf(station);
        if (chain == null)
            return false;
        var op = station.CurrentMixOperation;
        var start = Registry.GetItem(op.ProductID)?.TryCast<ProductDefinition>();
        if (start == null)
            return true;
        var final = Known(start, Fold(start, chain.Ingredients));
        if (final == null)
            return true;   // a new product: it's named first, then this runs again
        var output = final.GetDefaultInstance(op.Quantity);
        output.TryCast<QualityItemInstance>()?.SetQuality(op.ProductQuality);
        station.OutputSlot.AddItem(output, false);
        Store.Active.Remove(Key(station));
        station.SetMixOperation(null, null, 0);
        Core.Log.Msg($"Mixing steps: done, {op.Quantity} x {final.Name}");
        return true;
    }

    /// <summary>Creates a newly named product from the chain's final effects (the game would use one step only).</summary>
    public static bool TryName(string productId, string ingredientId, string name, string id)
    {
        foreach (var (guid, chain) in Store.Active)
        {
            var station = Resolve(guid);
            var op = station != null ? station.CurrentMixOperation : null;
            if (op == null || !Matches(chain, op) || op.ProductID != productId || op.IngredientID != ingredientId || !station.IsMixingDone)
                continue;
            var pm = NetworkSingleton<ProductManager>.Instance;
            for (int i = 0; i < pm.AllProducts.Count; i++)
            {
                if (pm.AllProducts[i] != null && pm.AllProducts[i].ID == id)
                    return true;
            }
            var start = Registry.GetItem(productId)?.TryCast<ProductDefinition>();
            if (start == null)
                return false;
            var props = Fold(start, chain.Ingredients);
            var ids = new Il2CppSystem.Collections.Generic.List<string>();
            for (int i = 0; i < props.Count; i++)
                ids.Add(props[i].ID);
            switch (start.DrugType)
            {
                case EDrugType.Marijuana: pm.CreateWeed(null, name, id, EDrugType.Marijuana, ids, WeedDefinition.GetAppearanceSettings(props)); break;
                case EDrugType.Methamphetamine: pm.CreateMeth(null, name, id, EDrugType.Methamphetamine, ids, MethDefinition.GetAppearanceSettings(props)); break;
                case EDrugType.Cocaine: pm.CreateCocaine(null, name, id, EDrugType.Cocaine, ids, CocaineDefinition.GetAppearanceSettings(props)); break;
                case EDrugType.Shrooms: pm.CreateShroom_Client(null, name, id, EDrugType.Shrooms, ids, ShroomDefinition.GetAppearanceSettings(props)); break;
                default: return false;
            }
            Core.Log.Msg($"Mixing steps: named the new {start.DrugType} '{name}' from {chain.Ingredients.Count} steps");
            return true;
        }
        return false;
    }

    /// <summary>The operation changed (started, finished, loaded): forget chains that no longer match it.</summary>
    public static void OperationChanged(MixingStation station)
    {
        if (_data == null || station == null)
            return;
        string key = Key(station);
        Stations[key] = station;
        if (_data.Active.TryGetValue(key, out var chain) && !Matches(chain, station.CurrentMixOperation))
            _data.Active.Remove(key);
        Carry.Remove(station.Pointer);
    }

    // ---- time ----

    /// <summary>
    /// Game minutes to add for <paramref name="minutes"/> real ones: a chain of N runs its clock N times slower
    /// (so it lasts as long as N mixes), Half Mixing Time twice as fast. Fractions carry over.
    /// </summary>
    public static int Scale(MixingStation station, int minutes)
    {
        float rate = (Config.HalfMixTime.On ? 2f : 1f) / Length(station);
        if (Mathf.Approximately(rate, 1f))
            return minutes;
        Carry.TryGetValue(station.Pointer, out float carry);
        float total = carry + minutes * rate;
        int whole = Mathf.FloorToInt(total);
        Carry[station.Pointer] = total - whole;
        return whole;
    }

    /// <summary>Real minutes left, rounded up.</summary>
    public static int Remaining(MixingStation station)
    {
        float rate = (Config.HalfMixTime.On ? 2f : 1f) / Length(station);
        int left = Mathf.Max(station.GetMixTimeForCurrentOperation() - station.CurrentMixTime, 0);
        return Mathf.CeilToInt(left / rate - 0.0001f);
    }

    public static bool Scaled(MixingStation station) =>
        station.CurrentMixOperation != null && (Config.HalfMixTime.On || Length(station) > 1);
}
