using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;

namespace PocketPlug.Backroom.Desk;

/// <summary>
/// The Backroom laptop: the game's own laptop, set on a Wooden Square Table you own, with "Open Backroom" on E.
///
/// Save safety: the game's save only ever contains the plain table. Which tables carry a laptop (by the table's
/// GUID) is kept in PocketPlug's own file, UserData/PocketPlug/backroom/&lt;save&gt;.json, and the laptop (visual
/// copies plus one interaction) is rebuilt after each load. Without PocketPlug the save just has a table.
/// </summary>
internal static class BackroomDesk
{
    /// <summary>Tables the laptop fits on (both square tables are 1 x 1 m).</summary>
    private static readonly string[] Tables = { "woodsquaretable", "metalsquaretable" };
    private const string LaptopName = "PocketPlug Backroom Laptop";

    private sealed class Record
    {
        public string Table { get; set; }
        public float Yaw { get; set; }
    }

    private static List<Record> _records;
    private static bool _attached;
    private static float _nextCheck;
    private static Transform _laptopSource;
    private static Material _screen;
    private static int _interactionLayer = -1;

    public static void Reset()
    {
        _records = null;
        _attached = false;
        _nextCheck = 0f;
        _laptopSource = null;
        _interactionLayer = -1;
    }

    /// <summary>Once the save has loaded, puts laptops back on their tables; afterwards a cheap check every 5 s.</summary>
    public static void Update()
    {
        if (Time.unscaledTime < _nextCheck)
            return;
        _nextCheck = Time.unscaledTime + (_attached ? 5f : 1f);
        if (!Singleton<LoadManager>.InstanceExists)
            return;
        var load = Singleton<LoadManager>.Instance;
        if (!load.IsGameLoaded || load.IsLoading)
            return;
        if (!_attached)
        {
            _attached = true;
            AttachAll(prune: true);
        }
    }

    private static string FilePath => Util.SaveKey.File("backroom", ".json");

    private static List<Record> Records
    {
        get
        {
            if (_records != null)
                return _records;
            var path = FilePath;
            if (path == null)
                return new List<Record>();
            _records = new List<Record>();
            try
            {
                if (File.Exists(path))
                    _records = JsonSerializer.Deserialize<List<Record>>(File.ReadAllText(path)) ?? new List<Record>();
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Backroom laptops: couldn't read {path}: {e.Message}");
            }
            return _records;
        }
    }

    private static void Store()
    {
        var path = FilePath;
        if (path == null || _records == null)
            return;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(_records));
        }
        catch (Exception e)
        {
            Core.Log.Warning($"Backroom laptops: couldn't write {path}: {e.Message}");
        }
    }

    /// <summary>The square table the player is looking at, within reach, at a property they own.</summary>
    public static BuildableItem LookedAtTable(out string problem)
    {
        problem = null;
        if (!PlayerSingleton<PlayerCamera>.InstanceExists)
        {
            problem = "Not available right now";
            return null;
        }
        var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
        if (!Physics.Raycast(cam.position, cam.forward, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
        {
            problem = "Look at a Wooden Square Table first";
            return null;
        }
        var item = hit.collider.GetComponentInParent<BuildableItem>();
        if (item == null || item.ItemInstance == null || Array.IndexOf(Tables, item.ItemInstance.ID) < 0)
        {
            problem = "That's not a square table: look at a Wooden Square Table";
            return null;
        }
        if (item.ParentProperty == null || !item.ParentProperty.IsOwned)
        {
            problem = "The table has to be at a property you own";
            return null;
        }
        return item;
    }

    public static bool HasLaptop(BuildableItem table) => table != null && table.transform.Find(LaptopName) != null;

    /// <summary>Puts a laptop on the table the player is looking at, screen facing them.</summary>
    public static string Install()
    {
        var table = LookedAtTable(out string problem);
        if (table == null)
            return problem;
        if (HasLaptop(table))
            return "That table already has the laptop";
        // Face the screen toward the player, snapped to a side of the (square) table.
        var toTable = Vector3.ProjectOnPlane(table.transform.position - PlayerSingleton<PlayerCamera>.Instance.transform.position, Vector3.up);
        float yaw = Mathf.Round(Vector3.SignedAngle(table.transform.forward, toTable, Vector3.up) / 90f) * 90f;
        if (!Attach(table, yaw))
            return "The game's laptop model isn't loaded";
        Records.RemoveAll(r => r.Table == table.GUID.ToString());
        Records.Add(new Record { Table = table.GUID.ToString(), Yaw = yaw });
        Store();
        return null;
    }

    public static string Remove()
    {
        var table = LookedAtTable(out string problem);
        if (table == null)
            return problem;
        var laptop = table.transform.Find(LaptopName);
        if (laptop == null)
            return "That table has no laptop";
        UnityEngine.Object.Destroy(laptop.gameObject);
        Records.RemoveAll(r => r.Table == table.GUID.ToString());
        Store();
        return null;
    }

    /// <summary>Dev: installs on a given table (no raycast), facing yaw 0.</summary>
    public static string DevInstall(BuildableItem table)
    {
        if (table == null || HasLaptop(table))
            return "no table or already has one";
        if (!Attach(table, 0f))
            return "laptop model missing";
        Records.RemoveAll(r => r.Table == table.GUID.ToString());
        Records.Add(new Record { Table = table.GUID.ToString(), Yaw = 0f });
        Store();
        return $"installed on {table.GUID}";
    }

    /// <summary>Re-attaches every recorded laptop; records whose table is gone (sold, picked up) are dropped.</summary>
    private static void AttachAll(bool prune)
    {
        var records = Records;
        if (records.Count == 0)
            return;
        var tables = new Dictionary<string, BuildableItem>();
        var owned = Property.OwnedProperties;
        for (int i = 0; i < owned.Count; i++)
        {
            var items = owned[i]?.BuildableItems;
            if (items == null)
                continue;
            for (int j = 0; j < items.Count; j++)
            {
                var item = items[j];
                if (item != null && item.ItemInstance != null && Array.IndexOf(Tables, item.ItemInstance.ID) >= 0)
                    tables[item.GUID.ToString()] = item;
            }
        }
        int before = records.Count;
        if (prune)
            records.RemoveAll(r => !tables.ContainsKey(r.Table));
        foreach (var r in records)
        {
            if (tables.TryGetValue(r.Table, out var table) && !HasLaptop(table))
                Attach(table, r.Yaw);
        }
        if (records.Count != before)
            Store();
        Core.Log.Msg($"Backroom laptops: {records.Count} on tables ({before - records.Count} removed with their table).");
    }

    private static bool Attach(BuildableItem table, float yaw)
    {
        _laptopSource ??= Visuals.FindLaptop();
        if (_laptopSource == null)
            return false;

        var laptop = Visuals.Clone(_laptopSource, table.transform, LaptopName);
        // Screen faces the player: the laptop mesh's forward is the back of its lid.
        laptop.transform.rotation = table.transform.rotation * Quaternion.Euler(0, yaw, 0);
        var tableBounds = TableBounds(table, laptop);
        var lapBounds = Visuals.WorldBounds(laptop);
        laptop.transform.position += new Vector3(tableBounds.center.x - lapBounds.center.x, tableBounds.max.y - lapBounds.min.y,
            tableBounds.center.z - lapBounds.center.z);
        ApplyScreen(laptop);

        // The prompt: colliders on the interaction layer, one interactable on the root.
        int layer = InteractionLayer();
        foreach (var filter in laptop.GetComponentsInChildren<MeshFilter>())
        {
            filter.gameObject.layer = layer;
            filter.gameObject.AddComponent<BoxCollider>();
        }
        laptop.layer = layer;
        if (Util.Perf.Enabled)
            Core.Log.Msg($"Backroom laptop: layer {layer} ({LayerMask.LayerToName(layer)}), search mask {(Singleton<InteractionManager>.InstanceExists ? Singleton<InteractionManager>.Instance.Interaction_SearchMask.value : -1)}");
        var interactable = laptop.AddComponent<InteractableObject>();
        interactable.SetMessage("Open Backroom");
        interactable.SetInteractionType(InteractableObject.EInteractionType.Key_Press);
        interactable.MaxInteractionRange = 2.5f;
        interactable.onInteractStart.AddListener((Action)OpenFromLaptop);
        return true;
    }

    private static void OpenFromLaptop()
    {
        if (Backroom.IsOpen)
            return;
        if (Backroom.CanOpen(out string reason))
            Backroom.Open();
        else if (reason != null)
            Core.Log.Msg($"Backroom laptop: {reason}");
    }

    /// <summary>The table's own renderers (not the laptop being placed on it).</summary>
    private static Bounds TableBounds(BuildableItem table, GameObject exclude)
    {
        Bounds? bounds = null;
        foreach (var r in table.GetComponentsInChildren<MeshRenderer>())
        {
            if (r == null || !r.enabled || r.transform.IsChildOf(exclude.transform))
                continue;
            if (bounds == null)
                bounds = r.bounds;
            else
            {
                var b = bounds.Value;
                b.Encapsulate(r.bounds);
                bounds = b;
            }
        }
        return bounds ?? new Bounds(table.transform.position, Vector3.one);
    }

    /// <summary>
    /// Shows the Backroom lock screen: the laptop's screen material (the one with an emissive or chart texture,
    /// whichever the model uses) is swapped for a copy showing br_lockscreen.
    /// </summary>
    private static void ApplyScreen(GameObject laptop)
    {
        foreach (var renderer in laptop.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = renderer.sharedMaterials;
            if (mats.Length < 2)
                continue;
            // The body is the first material; the screen is the other one.
            int index = mats.Length - 1;
            if (_screen == null)
            {
                var sprite = Util.Assets.Sprite("br_lockscreen");
                if (sprite == null)
                    return;
                _screen = new Material(mats[index]) { name = "PocketPlug Backroom Screen" };
                _screen.hideFlags = HideFlags.DontUnloadUnusedAsset;
                // The game's LaptopScreen shader graph names its textures itself: replace every texture it has
                // (the screen image) with the lock screen.
                var names = _screen.GetTexturePropertyNames();
                foreach (var prop in names)
                {
                    var old = _screen.GetTexture(prop);
                    Core.Log.Msg($"Backroom laptop: screen texture {prop} = {(old != null ? old.name : "none")}");
                    if (old != null)
                        _screen.SetTexture(prop, sprite.texture);
                }
                if (_screen.HasProperty("_BaseColor"))
                    _screen.SetColor("_BaseColor", Color.white);
                if (_screen.HasProperty("_EmissionColor"))
                    _screen.SetColor("_EmissionColor", Color.white * 0.9f);
                Core.Log.Msg($"Backroom laptop: screen material '{mats[index].name}' (shader {mats[index].shader?.name}).");
            }
            var copy = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
                copy[i] = i == index ? _screen : mats[i];
            renderer.sharedMaterials = copy;
        }
    }

    private static int InteractionLayer()
    {
        if (_interactionLayer >= 0)
            return _interactionLayer;
        int named = LayerMask.NameToLayer("Interaction");
        int mask = Singleton<InteractionManager>.InstanceExists ? Singleton<InteractionManager>.Instance.Interaction_SearchMask.value : 0;
        if (named >= 0 && (mask & (1 << named)) != 0)
            return _interactionLayer = named;
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) != 0)
                return _interactionLayer = i;
        }
        return _interactionLayer = 0;
    }
}
