using System;
using System.IO;
using System.Text;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Persistence;
using MelonLoader.Utils;
using PocketPlug.Phone;
using UnityEngine;
using GamePhone = Il2CppScheduleOne.UI.Phone.Phone;

namespace PocketPlug;

/// <summary>
/// Development helpers, active only when <c>UserData/PocketPlug.dev</c> exists. Commands are read from
/// <c>UserData/PocketPlug.cmd</c> (one per line) so the game can be tested without driving mouse and keyboard.
/// </summary>
internal static class DevTools
{
    private static readonly string FlagPath = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug.dev");
    private static readonly string CommandPath = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug.cmd");
    private static readonly string DumpPath = Path.Combine(MelonEnvironment.UserDataDirectory, "PocketPlug-dump.txt");

    private static bool _enabled;
    private static float _nextPoll;
    private static DateTime _lastCommandWrite;

    public static void Init()
    {
        _enabled = File.Exists(FlagPath);
        if (_enabled)
            Core.Log.Warning("Dev mode on (PocketPlug.dev found).");
        Util.Perf.Enabled = _enabled;
    }

    public static void Poll(float now)
    {
        if (!_enabled || now < _nextPoll)
            return;
        _nextPoll = now + 0.5f;
        if (!File.Exists(CommandPath))
            return;

        // Wine can lag on deletes, so never run the same file twice.
        var write = File.GetLastWriteTimeUtc(CommandPath);
        if (write == _lastCommandWrite)
            return;
        _lastCommandWrite = write;

        string[] lines = File.ReadAllLines(CommandPath);
        File.Delete(CommandPath);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                Run(line.Trim());
            }
            catch (Exception e)
            {
                Core.Log.Error($"dev '{line}': {e}");
            }
        }
    }

    private static GameObject _preview;

    /// <summary>
    /// deskpreview [ids]: visual copies of tables with the game's laptop on top, in a row in front of the camera,
    /// for comparing looks in a screenshot. Nothing is saved or networked; "deskclear" removes them.
    /// </summary>
    private static void DeskPreview(string arg)
    {
        if (_preview != null)
            UnityEngine.Object.Destroy(_preview);
        string[] ids = arg.Length > 0 ? arg.Split(' ') : new[] { "woodsquaretable", "metalsquaretable", "coffeetable", "plastictable" };
        var cam = PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance.transform;
        // Face the most open direction, so the row isn't spawned inside a wall.
        Vector3 fwd = Vector3.forward;
        float best = -1f;
        for (int a = 0; a < 360; a += 15)
        {
            var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
            float d = Physics.Raycast(cam.position, dir, out var h, 30f) ? h.distance : 30f;
            if (d > best)
            {
                best = d;
                fwd = dir;
            }
        }
        var mover = PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerMovement>.Instance;
        mover.Teleport(mover.transform.position, Quaternion.LookRotation(fwd), false);
        var right = Vector3.Cross(Vector3.up, fwd);
        var laptop = Backroom.Desk.Visuals.FindLaptop();
        _preview = new GameObject("PocketPlug DeskPreview");
        for (int i = 0; i < ids.Length; i++)
        {
            var def = Il2CppScheduleOne.Registry.GetItem(ids[i])?.TryCast<Il2CppScheduleOne.ItemFramework.BuildableItemDefinition>();
            if (def?.BuiltItem == null)
            {
                Core.Log.Msg($"deskpreview: no buildable '{ids[i]}'");
                continue;
            }
            var table = Backroom.Desk.Visuals.Clone(def.BuiltItem.transform, _preview.transform, ids[i]);
            var spot = cam.position + fwd * Mathf.Min(best - 1.2f, 3f) + right * ((i - (ids.Length - 1) / 2f) * 1.6f);
            table.transform.position = spot;
            table.transform.rotation = Quaternion.LookRotation(-fwd);
            // First surface below eye height (casting from above would hit tree canopies).
            float ground = Physics.Raycast(spot, Vector3.down, out var hit, 10f) ? hit.point.y : spot.y - 1.6f;
            var tb = Backroom.Desk.Visuals.WorldBounds(table);
            table.transform.position += Vector3.up * (ground - tb.min.y);
            tb = Backroom.Desk.Visuals.WorldBounds(table);
            if (laptop != null)
            {
                var lap = Backroom.Desk.Visuals.Clone(laptop, table.transform, "Laptop");
                // The laptop mesh's forward is the back of its lid: face it the other way so the screen faces us.
                lap.transform.rotation = Quaternion.LookRotation(fwd);
                var lb = Backroom.Desk.Visuals.WorldBounds(lap);
                lap.transform.position += new Vector3(tb.center.x - lb.center.x, tb.max.y - lb.min.y, tb.center.z - lb.center.z);
            }
            Core.Log.Msg($"deskpreview: {ids[i]} size={tb.size.ToString("F2")} top={tb.max.y - ground:F2} m, laptop={(laptop != null ? laptop.name : "missing")}");
        }
    }

    private static void StepsCommand(string arg)
    {
        string[] a = arg.Split(' ');
        Il2CppScheduleOne.ObjectScripts.MixingStation mk2 = null;
        foreach (var prop in Il2CppScheduleOne.Property.Property.OwnedProperties)
            foreach (var it in prop.BuildableItems)
                mk2 ??= it?.TryCast<Il2CppScheduleOne.ObjectScripts.MixingStationMk2>();
        switch (a[0])
        {
            case "put":
                // Test only: items from nothing; clear them with "steps clear" and don't save.
                var slot = a[1] == "product" ? mk2.ProductSlot : mk2.MixerSlot;
                slot.ClearStoredInstance(false);
                if (a.Length > 3 && int.Parse(a[3]) > 0)
                    slot.SetStoredItem(Il2CppScheduleOne.Registry.GetItem(a[2]).GetDefaultInstance(int.Parse(a[3])), false);
                break;
            case "next": Features.MixStepsUi.DevStep(1); break;
            case "back": Features.MixStepsUi.DevStep(-1); break;
            case "begin": Features.MixStepsUi.DevBegin(); break;
            case "confirm":
                // Presses the game's "New Mix Discovered" Confirm with the given name.
                var screen = Singleton<Il2CppScheduleOne.UI.NewMixScreen>.Instance;
                screen.nameInputField.text = string.Join(" ", a, 1, a.Length - 1);
                screen.ConfirmButtonClicked();
                break;
            case "output":
                var o = mk2.OutputSlot.ItemInstance;
                var pd = o?.Definition?.TryCast<Il2CppScheduleOne.Product.ProductDefinition>();
                string effects = "";
                if (pd != null)
                    foreach (var e in pd.Properties)
                        effects += e.Name + ", ";
                Core.Log.Msg($"steps output: {o?.ID} '{o?.Name}' x{mk2.OutputSlot.Quantity} quality={o?.TryCast<Il2CppScheduleOne.ItemFramework.QualityItemInstance>()?.Quality} effects=[{effects}]");
                break;
            case "mixers":
                foreach (var m in NetworkSingleton<Il2CppScheduleOne.Product.ProductManager>.Instance.ValidMixIngredients)
                    Core.Log.Msg($"steps mixer: {m.ID} '{m.Name}' effect={m.Properties[0].Name}");
                break;
            case "switch":
                // The same move the arrows make, without the mixing screen open.
                Core.Log.Msg($"steps switch: '{Features.MixSteps.Switch(mk2, int.Parse(a[1]) - 1)}'");
                break;
            case "closeui":
                Singleton<Il2CppScheduleOne.UI.Stations.MixingStationInterface>.Instance.Close();
                break;
            case "candestroy":
                bool can = mk2.CanBeDestroyed(out string why);
                Core.Log.Msg($"steps candestroy: {can} reason='{why}' hidden={Features.MixSteps.HasHidden(mk2)}");
                break;
            case "clear":
                // Empties every step and slot of the Mk2 (test items only) and forgets its queue.
                var q = Features.MixSteps.QueueOf(mk2, false);
                if (q != null)
                {
                    q.Cur = 0;
                    for (int i = 0; i < q.Steps.Count; i++)
                        q.Steps[i] = new Features.MixSteps.Step();
                }
                mk2.ProductSlot.ClearStoredInstance(false);
                mk2.MixerSlot.ClearStoredInstance(false);
                mk2.OutputSlot.ClearStoredInstance(false);
                break;
        }
        Core.Log.Msg("steps: " + Features.MixStepsUi.DevState());
    }

    private static void DeskPlace()
    {
        var cam = PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance.transform;
        var target = cam.position + Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized * 1.8f;
        Il2CppScheduleOne.Tiles.Grid bestGrid = null;
        Il2CppScheduleOne.Tiles.Tile bestTile = null;
        float best = 12f;
        foreach (var prop in Il2CppScheduleOne.Property.Property.OwnedProperties)
            foreach (var grid in prop.Grids)
                foreach (var tile in grid.Tiles)
                {
                    float d = Vector3.Distance(tile.transform.position, target);
                    // Only empty floor: nothing in the 1 x 1 m table footprint above the tile.
                    if (d < best && !Physics.CheckBox(tile.transform.position + new Vector3(0.25f, 0.6f, 0.25f), new Vector3(0.6f, 0.45f, 0.6f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    {
                        best = d;
                        bestGrid = grid;
                        bestTile = tile;
                    }
                }
        if (bestTile == null)
        {
            Core.Log.Msg("deskplace: no owned grid tile within 4 m in front of you");
            return;
        }
        var instance = Il2CppScheduleOne.Registry.GetItem("woodsquaretable").GetDefaultInstance(1);
        var item = NetworkSingleton<Il2CppScheduleOne.Building.BuildManager>.Instance.CreateGridItem(instance, bestGrid, new Vector2(bestTile.x, bestTile.y), 0, "", null);
        Core.Log.Msg($"deskplace: table at tile {bestTile.x},{bestTile.y} ({best:F1} m off) → {Backroom.Desk.BackroomDesk.DevInstall(item)}");
    }

    /// <summary>Backroom groundwork: fonts, table footprints and the laptop mesh, from the running game.</summary>
    private static void Probe()
    {
        foreach (var f in Resources.FindObjectsOfTypeAll<Il2CppTMPro.TMP_FontAsset>())
            Core.Log.Msg($"probe font: {f.name}");
        foreach (var def in PersistentSingleton<Il2CppScheduleOne.Registry>.Instance.GetAllItems())
        {
            var b = def?.TryCast<Il2CppScheduleOne.ItemFramework.BuildableItemDefinition>();
            if (b == null || b.BuiltItem == null)
                continue;
            string id = b.ID.ToLowerInvariant();
            if (!id.Contains("table") && !id.Contains("desk"))
                continue;
            var col = b.BuiltItem.BoundingCollider;
            var grid = b.BuiltItem.TryCast<Il2CppScheduleOne.EntityFramework.GridItem>();
            Core.Log.Msg($"probe table: {b.ID} '{b.Name}' collider={(col != null ? col.size.ToString("F2") : "none")} center={(col != null ? col.center.ToString("F2") : "")} footprint={(grid != null ? $"{grid.FootprintX}x{grid.FootprintY}" : "n/a")} price={b.BasePurchasePrice}");
        }
        foreach (var mf in Resources.FindObjectsOfTypeAll<MeshFilter>())
        {
            var mesh = mf.sharedMesh;
            if (mesh == null)
                continue;
            string n = mesh.name.ToLowerInvariant();
            if (n.Contains("laptop") || n.Contains("computer") || n.Contains("monitor"))
                Core.Log.Msg($"probe mesh: '{mesh.name}' on '{mf.gameObject.name}' (root '{mf.transform.root.name}', scene '{mf.gameObject.scene.name}') size={mesh.bounds.size.ToString("F3")} scale={mf.transform.lossyScale.ToString("F2")} mats={mf.GetComponent<MeshRenderer>()?.sharedMaterials.Length}");
        }
    }

    private static void MixTest(string arg)
    {
        string[] args = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int quantity = args.Length > 0 ? int.Parse(args[0]) : 20;
        int minutes = args.Length > 1 ? int.Parse(args[1]) : 0;
        var products = NetworkSingleton<Il2CppScheduleOne.Product.ProductManager>.Instance;
        if (products.mixRecipes.Count == 0)
        {
            Core.Log.Msg("mixtest: no known mix recipes in this save");
            return;
        }
        var recipe = products.mixRecipes[0];
        string a = recipe.Ingredients[0].Items[0].ID, b = recipe.Ingredients[1].Items[0].ID;
        if (products.GetRecipe(a, b) == null)
            (a, b) = (b, a);
        foreach (var prop in Il2CppScheduleOne.Property.Property.OwnedProperties)
            foreach (var it in prop.BuildableItems)
            {
                var mix = it?.TryCast<Il2CppScheduleOne.ObjectScripts.MixingStationMk2>();
                if (mix == null || mix.CurrentMixOperation != null || mix.OutputSlot.Quantity > 0)
                    continue;
                var op = new Il2CppScheduleOne.ObjectScripts.MixOperation(a, Il2CppScheduleOne.ItemFramework.EQuality.Standard, b, quantity);
                mix.SetMixOperation(null, op, minutes);
                Core.Log.Msg($"mixtest: {prop.PropertyName} {a}+{b} x{quantity} from {minutes} min, total {mix.GetMixTimeForCurrentOperation()} min, known={op.IsOutputKnown(out _)}");
                return;
            }
        Core.Log.Msg("mixtest: no idle Mk2 mixer");
    }

    private static void Run(string line)
    {
        string[] parts = line.Split(' ', 2);
        string arg = parts.Length > 1 ? parts[1] : "";
        Core.Log.Msg($"dev> {line}");

        switch (parts[0])
        {
            case "load":
                foreach (var info in LoadManager.SaveGames)
                {
                    if (info == null)
                        continue;
                    Singleton<LoadManager>.Instance.StartGame(info, false, true);
                    return;
                }
                Core.Log.Warning("No save found.");
                break;
            case "phone":
                var menu = Singleton<Il2CppScheduleOne.UI.GameplayMenu>.Instance;
                if (arg == "close")
                {
                    menu.Close();
                }
                else
                {
                    menu.Open();
                    menu.SetScreen(Il2CppScheduleOne.UI.GameplayMenu.EGameplayScreen.Phone);
                }
                break;
            case "app":
                Apps.Open(arg);
                break;
            case "cash":
                NetworkSingleton<MoneyManager>.Instance.ChangeCashBalance(float.Parse(arg), true, false);
                break;
            case "click":
                // click <app> <path under the app container>
                var a = arg.Split(' ', 2);
                var target = Apps.Find(a[0])?.Container.transform.Find(a[1]);
                if (target == null)
                    Core.Log.Warning($"click: {arg} not found");
                else
                    target.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                break;
            case "transfer":
                // Runs the dealer transfer for the first recruited dealer, as if the message was sent.
                foreach (var d in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
                {
                    if (d == null || !d.IsRecruited)
                        continue;
                    if (arg != "")
                        d.SetCash(float.Parse(arg));
                    Features.DealerTransfers.Transfer(d, d.MSGConversation);
                    return;
                }
                Core.Log.Warning("transfer: no recruited dealer");
                break;
            case "deals":
                var cs = Il2CppScheduleOne.Quests.Contract.Contracts;
                Core.Log.Msg($"deals: {cs.Count}");
                for (int i = 0; i < cs.Count; i++)
                {
                    var c = cs[i];
                    var npc = c.Customer != null ? c.Customer.GetComponent<Il2CppScheduleOne.NPCs.NPC>() : null;
                    int withElement = 0;
                    for (int j = 0; j < c.Entries.Count; j++)
                        if (c.Entries[j].compassElement != null) withElement++;
                    Core.Log.Msg($"  {c.Title} | {c.State} | tracked={c.IsTracked} | dealer={(c.Dealer != null)} | customer={npc?.FullName} | compass={withElement} | expires={(c.Expires ? c.GetMinsUntilExpiry() + "m" : "no")}");
                }
                break;
            case "notify":
                Singleton<Il2CppScheduleOne.UI.NotificationsManager>.Instance.SendNotification("PocketPlug test", arg == "" ? "Notifications work" : arg,
                    Il2CppScheduleOne.Registry.GetItem("mixingstation")?.Icon, 5f, true);
                break;
            case "fakedeal":
                var cm = Singleton<Il2CppScheduleOne.UI.Compass.CompassManager>.Instance;
                if (arg == "off")
                {
                    foreach (var (el, _) in Features.CompassDeals.DevMarkers)
                        cm.RemoveElement(el, true);
                    Features.CompassDeals.DevMarkers.Clear();
                    break;
                }
                Il2CppScheduleOne.NPCs.NPC who = null;
                foreach (var n in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.NPCs.NPC>())
                {
                    if (Util.Npcs.Mugshot(n) != null && n.GetComponent<Il2CppScheduleOne.Economy.Customer>() != null)
                    {
                        who = n;
                        break;
                    }
                }
                RectTransform icon = null;
                foreach (var q in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Quests.Quest>())
                {
                    if (q.IconPrefab != null)
                    {
                        icon = q.IconPrefab;
                        break;
                    }
                }
                if (who == null || icon == null)
                {
                    Core.Log.Warning($"fakedeal: npc={who != null} icon={icon != null}");
                    break;
                }
                var element = cm.AddElement(who.transform, icon, true);
                Features.CompassDeals.DevMarkers.Add((element, who));
                Core.Log.Msg($"fakedeal: marker on {who.FullName}");
                break;
            case "stations":
                foreach (var prop in Il2CppScheduleOne.Property.Property.OwnedProperties)
                {
                    foreach (var it in prop.BuildableItems)
                    {
                        var mix = it?.TryCast<Il2CppScheduleOne.ObjectScripts.MixingStation>();
                        if (mix != null)
                        {
                            bool mk2 = mix.TryCast<Il2CppScheduleOne.ObjectScripts.MixingStationMk2>() != null;
                            var field = mix.stationConfiguration?.StartThrehold;
                            Core.Log.Msg($"stations: {prop.PropertyName} | {(mk2 ? "Mk2" : "Mk1")} mixer | max={mix.MaxMixQuantity} timePerItem={mix.MixTimePerItem} sliderMax={(field != null ? field.MaxValue : -1)} output={mix.OutputSlot?.Quantity}");
                        }
                        var rack = it?.TryCast<Il2CppScheduleOne.ObjectScripts.DryingRack>();
                        if (rack != null)
                            Core.Log.Msg($"stations: {prop.PropertyName} | drying rack | capacity={rack.ItemCapacity} sliderMax={(rack.stationConfiguration?.StartThreshold != null ? rack.stationConfiguration.StartThreshold.MaxValue : -1)}");
                    }
                }
                break;
            case "fps":
                Core.Log.Msg($"fps: {1f / Time.smoothDeltaTime:F0} now, vSyncCount={QualitySettings.vSyncCount} targetFrameRate={Application.targetFrameRate} refresh={Screen.currentResolution.refreshRateRatio.value:F0} quality={QualitySettings.GetQualityLevel()}");
                break;
            case "dealers":
                foreach (var d in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
                    if (d != null)
                        Core.Log.Msg($"dealers: {d.FullName} recruited={d.IsRecruited} cash={d.Cash}");
                break;
            case "collecttest":
                // Gives the first dealer $50 (server side, instant), collects it to the bank, then takes it back.
                foreach (var d in Il2CppScheduleOne.Economy.Dealer.AllPlayerDealers)
                {
                    if (d == null)
                        continue;
                    float bank = Features.Bank.Online, cash = d.Cash;
                    d.RpcLogic___SetCash_431000436(cash + 50f);
                    float got = Features.Bank.CollectFrom(d);
                    Core.Log.Msg($"collecttest: {d.FullName} got={got} dealer {cash} -> {d.Cash} bank {bank} -> {Features.Bank.Online}");
                    float again = Features.Bank.CollectFrom(d);
                    Core.Log.Msg($"collecttest: second collect got={again} (must be 0)");
                    NetworkSingleton<MoneyManager>.Instance.CreateOnlineTransaction("Test", -got, 1f, "PocketPlug test refund");
                    d.RpcLogic___SetCash_431000436(cash);
                    Core.Log.Msg($"collecttest: refunded, dealer {d.Cash} bank {Features.Bank.Online}");
                    return;
                }
                break;
            case "tp":
                // tp [point]: the game's named teleport points (lists them without an argument).
                var points = Il2CppScheduleOne.Console.Instance.TeleportPointsContainer;
                for (int i = 0; i < points.childCount; i++)
                {
                    var p = points.GetChild(i);
                    if (arg == "")
                        Core.Log.Msg($"tp: {p.name}");
                    else if (p.name.Equals(arg, StringComparison.OrdinalIgnoreCase))
                    {
                        PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerMovement>.Instance.Teleport(p.position, p.rotation, false);
                        Core.Log.Msg($"tp: {p.name} {p.position}");
                    }
                }
                break;
            case "deskpreview":
                DeskPreview(arg);
                break;
            case "deskclear":
                if (_preview != null)
                    UnityEngine.Object.Destroy(_preview);
                _preview = null;
                break;
            case "backroom":
                // backroom open|close|page <id>
                string[] b = arg.Split(' ', 2);
                if (b[0] == "open")
                    Backroom.Backroom.Open();
                else if (b[0] == "close")
                    Backroom.Backroom.Close();
                else if (b[0] == "page" && b.Length > 1)
                    Backroom.Backroom.Show(Backroom.Backroom.IndexOf(b[1]));
                else if ((b[0] == "run" || b[0] == "adjust" || b[0] == "back") && Backroom.Backroom.Shell != null)
                {
                    // backroom run <row title> | adjust <+1|-1> <row title> | back   (on the current page)
                    if (Backroom.Backroom.Pages[Backroom.Backroom.Shell.Current] is not Backroom.Kit.ListPage list)
                        Core.Log.Msg("backroom: not a list page");
                    else if (b[0] == "back")
                        Core.Log.Msg("backroom back: " + list.DevBack());
                    else if (b[0] == "adjust")
                    {
                        string[] adj = b[1].Split(' ', 2);
                        Core.Log.Msg("backroom adjust: " + list.DevRun(adj[1], int.Parse(adj[0])));
                    }
                    else
                        Core.Log.Msg("backroom run: " + list.DevRun(b[1]));
                }
                else if (b[0] == "item" && b.Length > 1)
                {
                    // backroom item <id> [qty] [quality|-] [packaging|-] [give]
                    string[] ia = b[1].Split(' ');
                    var items = (Backroom.Pages.ItemsPage)Backroom.Backroom.Pages[Backroom.Backroom.IndexOf("items")];
                    Backroom.Backroom.Show(Backroom.Backroom.IndexOf("items"));
                    Core.Log.Msg("backroom item: " + items.DevSelect(ia[0], ia.Length > 1 ? int.Parse(ia[1]) : 1,
                        ia.Length > 2 && ia[2] != "-" ? ia[2] : null, ia.Length > 3 && ia[3] != "-" ? ia[3] : null, ia.Length > 4 && ia[4] == "give"));
                }
                Core.Log.Msg($"backroom: open={Backroom.Backroom.IsOpen} page={Backroom.Backroom.Shell?.Current}");
                break;
            case "inv":
                // Lists the hotbar: item, quantity, quality, packaging.
                foreach (var slot in PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance.hotbarSlots)
                {
                    var it = slot?.ItemInstance;
                    if (it == null)
                        continue;
                    var q = it.TryCast<Il2CppScheduleOne.ItemFramework.QualityItemInstance>();
                    var p = it.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>();
                    Core.Log.Msg($"inv: {it.Definition.ID} x{it.Quantity} quality={(q != null ? q.Quality.ToString() : "-")} packaging={(p?.AppliedPackaging != null ? p.AppliedPackaging.ID : "-")}");
                }
                break;
            case "deskplace":
                // Test only (never save after this): places a real Wooden Square Table on the nearest owned grid
                // tile in front of the camera and installs the Backroom laptop on it.
                DeskPlace();
                break;
            case "deskview":
                // Stands the player 1.6 m in front of the Backroom laptop, looking at it.
                var lap = GameObject.Find("PocketPlug Backroom Laptop");
                if (lap == null)
                {
                    Core.Log.Msg("deskview: no laptop");
                    break;
                }
                var front = -lap.transform.forward;   // the screen side
                var stand = lap.transform.position + front * 1.6f;
                stand.y = lap.transform.parent.position.y;
                PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerMovement>.Instance.Teleport(stand, Quaternion.LookRotation(-front), true);
                if (arg == "aim")
                    PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance.LookAt(Backroom.Desk.Visuals.WorldBounds(lap).center, 0.2f);
                foreach (var col in lap.GetComponentsInChildren<Collider>())
                    Core.Log.Msg($"deskview collider: {col.name} enabled={col.enabled} bounds={col.bounds.center.ToString("F2")} size={col.bounds.size.ToString("F2")} layer={col.gameObject.layer}");
                Core.Log.Msg($"deskview laptop renderer bounds: {Backroom.Desk.Visuals.WorldBounds(lap).center.ToString("F2")} size {Backroom.Desk.Visuals.WorldBounds(lap).size.ToString("F2")}");
                Core.Log.Msg($"deskview: at {stand}");
                break;
            case "deskuse":
                // Fires the laptop's interaction the way pressing E does.
                var useLap = GameObject.Find("PocketPlug Backroom Laptop");
                useLap?.GetComponent<Il2CppScheduleOne.Interaction.InteractableObject>()?.onInteractStart.Invoke();
                Core.Log.Msg($"deskuse: backroom open={Backroom.Backroom.IsOpen}");
                break;
            case "openmixer":
                // Opens the first Mk2 mixer's screen the way pressing E on it does.
                foreach (var prop in Il2CppScheduleOne.Property.Property.OwnedProperties)
                    foreach (var it in prop.BuildableItems)
                    {
                        var mk2 = it?.TryCast<Il2CppScheduleOne.ObjectScripts.MixingStationMk2>();
                        if (mk2 == null)
                            continue;
                        Core.Log.Msg($"openmixer: {prop.PropertyName} requiresInsertion={mk2.RequiresIngredientInsertion} guid={mk2.GUID}");
                        mk2.Interacted();
                        return;
                    }
                break;
            case "steps":
                // steps put <product|mixer> <id> <qty> | next | back | begin | state | mixers | clear
                StepsCommand(arg);
                break;
            case "deskhover":
                var hovered = Singleton<Il2CppScheduleOne.Interaction.InteractionManager>.Instance.HoveredInteractableObject;
                Core.Log.Msg($"deskhover: {(hovered != null ? hovered.gameObject.name : "nothing")}");
                var camT = PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance.transform;
                int searchMask = Singleton<Il2CppScheduleOne.Interaction.InteractionManager>.Instance.Interaction_SearchMask.value;
                var lapObj = GameObject.Find("PocketPlug Backroom Laptop");
                var dir = lapObj != null ? (Backroom.Desk.Visuals.WorldBounds(lapObj).center - camT.position).normalized : camT.forward;
                Il2CppScheduleOne.Interaction.InteractableObject winner = null;
                float winnerD = 0;
                foreach (var h in Physics.SphereCastAll(camT.position, 0.05f, dir, 4f, searchMask, QueryTriggerInteraction.Collide))
                {
                    var cand = h.collider.GetComponentInParent<Il2CppScheduleOne.Interaction.InteractableObject>();
                    if (cand != null && (winner == null || cand.Priority > winner.Priority || (cand.Priority == winner.Priority && h.distance < winnerD)))
                    {
                        winner = cand;
                        winnerD = h.distance;
                    }
                }
                Core.Log.Msg($"deskhover: looking at the laptop, the game's ranking picks {(winner != null ? winner.gameObject.name : "nothing")}");
                foreach (var h in Physics.SphereCastAll(camT.position, 0.05f, dir, 4f, searchMask, QueryTriggerInteraction.Collide))
                {
                    var io = h.collider.GetComponentInParent<Il2CppScheduleOne.Interaction.InteractableObject>();
                    Core.Log.Msg($"deskhover hit: {h.collider.name} layer={h.collider.gameObject.layer} trigger={h.collider.isTrigger} d={h.distance:F2} → {(io != null ? $"{io.gameObject.name} prio={io.Priority} enabled={io.enabled}" : "none")}");
                }
                break;
            case "probe":
                Probe();
                break;
            case "cap":
                // cap <fps>: frame cap for this session only (the game's own setting is untouched).
                Application.targetFrameRate = int.Parse(arg);
                Core.Log.Msg($"cap: targetFrameRate={Application.targetFrameRate}");
                break;
            case "mixtest":
                // mixtest [quantity] [minutes already mixed]: starts a known recipe on the first idle Mk2 mixer.
                // Test only: the product comes from nothing, so clear it with "mixclear" and don't save.
                MixTest(arg);
                break;
            case "mixclear":
                foreach (var prop in Il2CppScheduleOne.Property.Property.OwnedProperties)
                    foreach (var it in prop.BuildableItems)
                    {
                        var mix = it?.TryCast<Il2CppScheduleOne.ObjectScripts.MixingStationMk2>();
                        if (mix != null && mix.OutputSlot.Quantity > 0 && mix.ProductSlot.Quantity == 0)
                        {
                            Core.Log.Msg($"mixclear: removed {mix.OutputSlot.Quantity} from {prop.PropertyName}");
                            mix.OutputSlot.SetQuantity(0);
                        }
                    }
                break;
            case "mixstate":
                foreach (var prop in Il2CppScheduleOne.Property.Property.OwnedProperties)
                    foreach (var it in prop.BuildableItems)
                    {
                        var mix = it?.TryCast<Il2CppScheduleOne.ObjectScripts.MixingStation>();
                        if (mix?.CurrentMixOperation != null)
                            Core.Log.Msg($"mixstate: {prop.PropertyName} qty={mix.CurrentMixOperation.Quantity} time={mix.CurrentMixTime}/{mix.GetMixTimeForCurrentOperation()} done={mix.IsMixingDone} output={mix.OutputSlot.Quantity} screen='{mix.TryCast<Il2CppScheduleOne.ObjectScripts.MixingStationMk2>()?.ProgressLabel?.text}'");
                    }
                break;
            case "newday":
                // Runs the morning routine as if the day just ended (auto-pay, sweep, report).
                Features.Daily.Morning(NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance.ElapsedDays - 1);
                break;
            case "payroll":
                // payroll test: pay $1 to the first employee, log, then refund both sides (net zero).
                foreach (var prop in Features.Payroll.Properties())
                {
                    foreach (var emp in Features.Payroll.Employees(prop))
                    {
                        float before = Features.Payroll.LockerCash(emp), bank = Features.Bank.Online;
                        bool ok = Features.Payroll.Pay(emp, 1f, out string err);
                        float after = Features.Payroll.LockerCash(emp);
                        Core.Log.Msg($"payroll: {emp.FullName} @ {prop.PropertyName} ok={ok} err={err} locker {before} -> {after} bank {bank} -> {Features.Bank.Online}");
                        if (ok)
                        {
                            emp.GetHome().RemoveCash(1f);
                            Features.Bank.Receive(1f, "PocketPlug test refund");
                            Core.Log.Msg($"payroll: refunded, locker {Features.Payroll.LockerCash(emp)}");
                        }
                        return;
                    }
                }
                Core.Log.Warning("payroll: no employees");
                break;
            case "aab":
                Features.Aab.Show();
                break;
            case "report":
                Features.Daily.SendReportNow();
                break;
            case "employees":
                foreach (var prop in Features.Payroll.Properties())
                    foreach (var emp in Features.Payroll.Employees(prop))
                        Core.Log.Msg($"employees: {prop.PropertyName} | {emp.FullName} | {emp.EmployeeType} | wage {emp.DailyWage} | locker {Features.Payroll.LockerCash(emp)} | paidToday={emp.PaidForToday}");
                break;
            case "money":
                Core.Log.Msg($"money: cash={Features.Bank.Cash} bank={Features.Bank.Online} week={Il2CppScheduleOne.Money.ATM.WeeklyDepositSum}");
                break;
            case "shot":
                // Unity writes its own frame, so this works even when the window is on another workspace.
                ScreenCapture.CaptureScreenshot(Path.Combine(MelonEnvironment.UserDataDirectory, $"PocketPlug-{(arg == "" ? "shot" : arg)}.png"));
                break;
            case "dump":
                Dump(arg);
                break;
            default:
                Core.Log.Warning($"Unknown dev command: {parts[0]}");
                break;
        }
    }

    /// <summary>Writes the transform tree under a GameObject (found by name) with components and rect sizes.</summary>
    private static void Dump(string name)
    {
        var root = name == "compass"
            ? Singleton<Il2CppScheduleOne.UI.Compass.CompassManager>.Instance.ElementUIContainer.gameObject
            : GameObject.Find(name);
        if (root == null)
        {
            Core.Log.Warning($"dump: '{name}' not found");
            return;
        }

        var sb = new StringBuilder();
        Walk(root.transform, 0, sb);
        File.AppendAllText(DumpPath, $"==== {name}\n{sb}\n");
        Core.Log.Msg($"dump: wrote {name} to {DumpPath}");
    }

    private static void Walk(Transform t, int depth, StringBuilder sb)
    {
        sb.Append(' ', depth * 2).Append(t.name);
        if (!t.gameObject.activeSelf)
            sb.Append(" [off]");
        var rect = t.TryCast<RectTransform>();
        if (rect != null)
            sb.Append($" rect={rect.rect.width:F0}x{rect.rect.height:F0} pos={rect.anchoredPosition}");
        float rz = t.localEulerAngles.z;
        if (rz > 0.5f)
            sb.Append($" rot={rz:F0}");
        if (t.localScale != Vector3.one)
            sb.Append($" scale={t.localScale}");
        var img = t.GetComponent<UnityEngine.UI.Image>();
        if (img != null)
            sb.Append($" color={img.color} sprite={(img.sprite != null ? img.sprite.name : "-")}");
        var text = t.GetComponent<UnityEngine.UI.Text>();
        if (text != null)
            sb.Append($" font={(text.font != null ? text.font.name : "NULL")} size={text.fontSize} text='{(text.text.Length > 24 ? text.text.Substring(0, 24) : text.text)}'");
        var group = t.GetComponent<CanvasGroup>();
        if (group != null)
            sb.Append($" groupAlpha={group.alpha:F2}");
        if (rect != null)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            sb.Append($" screen=({corners[0].x:F0},{corners[0].y:F0})-({corners[2].x:F0},{corners[2].y:F0})");
        }
        var tmp = t.GetComponent<Il2CppTMPro.TextMeshProUGUI>();
        if (tmp != null)
            sb.Append($" tmp font={(tmp.font != null ? tmp.font.name : "NULL")} size={tmp.fontSize} color={tmp.color} alpha={tmp.alpha} chars={tmp.textInfo?.characterCount} text='{(tmp.text.Length > 24 ? tmp.text.Substring(0, 24) : tmp.text)}'");
        var components = t.GetComponents<Component>();
        foreach (var c in components)
        {
            string type = c.GetIl2CppType().Name;
            if (type != "Transform" && type != "RectTransform")
                sb.Append(" <").Append(type).Append('>');
        }
        sb.Append('\n');
        if (depth > 12)
            return;
        for (int i = 0; i < t.childCount; i++)
            Walk(t.GetChild(i), depth + 1, sb);
    }
}
