using System;
using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Stations;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PocketPlug.Features;

/// <summary>
/// The mixing-steps controls on the Mk2 mixing screen: small grey arrows inside the Mixer slot, under the game's
/// filter icon (next step above, previous step below), ← → on the keyboard, "Step n of m" under the slot, the
/// final product in the preview, and the total time under Begin.
/// </summary>
internal static class MixStepsUi
{
    private static readonly Color Idle = new(1f, 1f, 1f, 0.55f);
    private static readonly Color Hover = new(1f, 1f, 1f, 0.9f);
    private static readonly Color Dim = new(1f, 1f, 1f, 0.15f);

    private static MixingStationInterface _ui;
    private static MixingStation _station;
    private static GameObject _next, _back;
    private static Button _nextButton, _backButton;
    private static TextMeshProUGUI _mixerLabel, _total;
    private static string _mixerText;

    public static void Reset()
    {
        _ui = null;
        _station = null;
        _next = _back = null;
        _mixerLabel = _total = null;
    }

    private static bool Mk2Open => _ui != null && _station != null && MixSteps.IsMk2(_station);

    /// <summary>Steps are in use: more than one filled step, or the one filled step isn't the one shown.</summary>
    public static bool Engaged(MixingStation station)
    {
        if (!MixSteps.IsMk2(station))
            return false;
        var steps = MixSteps.Ordered(station);
        var q = MixSteps.QueueOf(station, false);
        return steps.Count > 1 || (steps.Count == 1 && steps[0].index != (q?.Cur ?? 0));
    }

    private static bool Visible(MixingStation station) =>
        MixSteps.IsMk2(station) && (Config.MixSteps.On || MixSteps.HasHidden(station) || (MixSteps.QueueOf(station, false)?.Cur ?? 0) > 0);

    // ---- building ----

    public static void Opened(MixingStationInterface ui)
    {
        _ui = ui;
        _station = ui.Station;
        Build(ui);
        Refresh();
    }

    public static void Closed()
    {
        _station = null;
    }

    private static void Build(MixingStationInterface ui)
    {
        if (_next != null)
            return;
        var slot = ui.IngredientSlotUI.transform;
        var filter = slot.Find("Filter");
        // Next step sits right under the filter icon, previous step under that, same size and corner.
        // Both fit between the filter icon and the slot's quantity text (bottom right).
        _next = Arrow(slot, "PocketPlug NextStep", "chevron-up", -31, () => Step(1));
        _back = Arrow(slot, "PocketPlug PrevStep", "chevron-down", -48, () => Step(-1));
        _nextButton = _next.GetComponent<Button>();
        _backButton = _back.GetComponent<Button>();
        if (filter != null)
        {
            _next.transform.SetSiblingIndex(filter.GetSiblingIndex() + 1);
            _back.transform.SetSiblingIndex(filter.GetSiblingIndex() + 2);
        }

        _mixerLabel = slot.Find("Label")?.GetComponent<TextMeshProUGUI>();
        _mixerText = _mixerLabel != null ? _mixerLabel.text : "Mixer";

        var begin = ui.BeginButton.GetComponent<RectTransform>();
        var go = new GameObject("PocketPlug StepsTotal");
        go.layer = begin.gameObject.layer;
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(begin.parent, false);
        rect.anchorMin = begin.anchorMin;
        rect.anchorMax = begin.anchorMax;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(460, 30);
        rect.anchoredPosition = begin.anchoredPosition + new Vector2(0, -begin.rect.height * (1 - begin.pivot.y) - 8);
        _total = go.AddComponent<TextMeshProUGUI>();
        var vanilla = ui.InstructionLabel;
        _total.font = vanilla.font;
        _total.fontStyle = FontStyles.Italic;
        _total.fontSize = 16;
        _total.color = new Color(1f, 1f, 1f, 0.85f);
        _total.alignment = TextAlignmentOptions.Center;
        _total.raycastTarget = false;
        _total.enableWordWrapping = false;
    }

    private static GameObject Arrow(Transform slot, string name, string glyph, float y, Action onClick)
    {
        var go = new GameObject(name);
        go.layer = slot.gameObject.layer;
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(slot, false);
        // Same placement as the game's filter button (top-right corner, 24 x 24, centred 12 px in).
        rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(20, 17);
        rect.anchoredPosition = new Vector2(-12, y);
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);
        var button = go.AddComponent<Button>();
        button.targetGraphic = hit;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);

        var iconGo = new GameObject("Icon");
        iconGo.layer = slot.gameObject.layer;
        var iconRect = iconGo.AddComponent<RectTransform>();
        iconRect.SetParent(rect, false);
        iconRect.sizeDelta = new Vector2(15, 15);
        var icon = iconGo.AddComponent<Image>();
        icon.sprite = Util.Assets.Sprite("br_" + glyph);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.color = Idle;
        return go;
    }

    // ---- behaviour ----

    /// <summary>Dev: the arrows / Begin as a player presses them, and what the screen shows.</summary>
    public static void DevStep(int direction) => Step(direction);

    public static void DevBegin() => _ui?.BeginButtonPressed();

    public static string DevState()
    {
        if (_ui == null || _station == null)
            return "mixing screen not open";
        var q = MixSteps.QueueOf(_station, false);
        var order = string.Join(" → ", MixSteps.Ordered(_station).ConvertAll(x => $"{x.index + 1}:{x.id}x{x.qty}"));
        var op = _station.CurrentMixOperation;
        var chain = MixSteps.ChainOf(_station);
        var mk2 = _station.TryCast<MixingStationMk2>();
        return $"cur={(q?.Cur ?? 0) + 1} steps=[{order}] label='{_mixerLabel?.text}' preview='{_ui.PreviewLabel.text}' unknown={_ui.UnknownOutputIcon.gameObject.activeSelf} " +
               $"props='{_ui.PreviewPropertiesLabel.text.Replace("\n", " | ")}' begin={_ui.BeginButton.gameObject.activeSelf}/{_ui.BeginButton.interactable} total='{_total.text}' " +
               $"instr='{(_ui.InstructionLabel.enabled ? _ui.InstructionLabel.text : "")}' next={_nextButton.interactable} back={_backButton.interactable} " +
               $"op={(op != null ? $"{op.ProductID}+{op.IngredientID}x{op.Quantity} {_station.CurrentMixTime}/{_station.GetMixTimeForCurrentOperation()}" : "none")} " +
               $"chain={(chain != null ? string.Join(">", chain.Ingredients) : "none")} screen='{mk2?.ProgressLabel?.text}' icon={mk2?.OutputIcon?.sprite?.name} q={mk2?.QuestionMark?.gameObject.activeSelf} " +
               $"product={_station.ProductSlot.ItemInstance?.ID}x{_station.ProductSlot.Quantity} mixer={_station.MixerSlot.ItemInstance?.ID}x{_station.MixerSlot.Quantity} output={_station.OutputSlot.ItemInstance?.ID}x{_station.OutputSlot.Quantity}";
    }

    private static void Step(int direction)
    {
        if (!Mk2Open || !Visible(_station))
            return;
        var q = MixSteps.QueueOf(_station, true);
        string problem = MixSteps.Switch(_station, q.Cur + direction);
        if (!string.IsNullOrEmpty(problem))
            Core.Log.Msg($"Mixing steps: {problem}");
        Refresh();
        _ui.UpdatePreview();
        _ui.UpdateBeginButton();
    }

    private static bool CanGo(int target)
    {
        if (target < 0 || target >= MixSteps.MaxSteps)
            return false;
        var q = MixSteps.QueueOf(_station, false);
        int cur = q?.Cur ?? 0;
        if (target < cur)
            return true;
        bool filled = _station.MixerSlot.Quantity > 0 && _station.MixerSlot.ItemInstance != null;
        if (filled)
            return MixSteps.IsMixer(_station.MixerSlot.ItemInstance.ID);
        return target <= MixSteps.Reachable(_station);
    }

    /// <summary>Arrows, the step label and the total line for the open station.</summary>
    public static void Refresh()
    {
        if (_ui == null || _next == null)
            return;
        bool show = _station != null && Visible(_station);
        _next.SetActive(show);
        _back.SetActive(show);
        var q = show ? MixSteps.QueueOf(_station, false) : null;
        int cur = q?.Cur ?? 0;
        if (show)
        {
            Paint(_nextButton, CanGo(cur + 1));
            Paint(_backButton, cur > 0);
        }

        var ordered = show ? MixSteps.Ordered(_station) : null;
        int filled = ordered?.Count ?? 0;
        int last = filled > 0 ? ordered[filled - 1].index + 1 : 0;
        if (_mixerLabel != null)
        {
            bool steps = show && (cur > 0 || filled > 1 || MixSteps.HasHidden(_station));
            _mixerLabel.text = steps ? $"{_mixerText}\n<size=14>Step {cur + 1} of {Mathf.Max(last, cur + 1)}</size>" : _mixerText;
        }

        string total = "";
        if (show && Engaged(_station) && _station.CurrentMixOperation == null && _station.OutputSlot.Quantity == 0)
        {
            string problem = MixSteps.CannotStart(_station, out int quantity, out var list);
            if (problem == null && list.Count > 1)
                total = $"{list.Count} mixes in a row  ·  {Duration(MixSteps.TotalMinutes(_station, list.Count, quantity))}";
        }
        _total.text = total;
        _total.enabled = total.Length > 0;
    }

    private static void Paint(Button button, bool enabled)
    {
        button.interactable = enabled;
        var icon = button.transform.Find("Icon").GetComponent<Image>();
        bool hover = enabled && button.IsHighlighted();
        icon.color = !enabled ? Dim : hover ? Hover : Idle;
    }

    private static string Duration(int minutes)
    {
        int h = minutes / 60, m = minutes % 60;
        return h == 0 ? $"{m} min" : m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    /// <summary>Keyboard ← → while the Mk2 mixing screen is open, and hover colours.</summary>
    public static void Update()
    {
        if (!Mk2Open || _next == null || !_next.activeInHierarchy)
            return;
        if (Singleton<NewMixScreen>.InstanceExists && Singleton<NewMixScreen>.Instance.IsOpen)
            return;
        var kb = Keyboard.current;
        if (kb != null && !GameInput.IsTyping)
        {
            if (kb.rightArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
                Step(1);
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
                Step(-1);
        }
        var q = MixSteps.QueueOf(_station, false);
        int cur = q?.Cur ?? 0;
        Paint(_nextButton, CanGo(cur + 1));
        Paint(_backButton, cur > 0);
    }

    // ---- the preview and the Mk2 screen ----

    /// <summary>Shows the chain's final product in the preview (known: icon and name; new: "Unknown" and its effects).</summary>
    public static void Preview(MixingStationInterface ui)
    {
        var station = ui.Station;
        if (station == null || !Engaged(station))
            return;
        var product = station.GetProduct();
        var steps = MixSteps.Ordered(station);
        if (product == null || steps.Count == 0)
            return;
        var ids = steps.ConvertAll(s => s.id);
        var props = MixSteps.Fold(product, ids);
        var known = MixSteps.Known(product, props);
        ui.IngredientProblemLabel.enabled = !steps.TrueForAll(s => MixSteps.IsMixer(s.id));
        if (known != null)
        {
            ui.PreviewIcon.sprite = known.Icon;
            ui.PreviewIcon.color = Color.white;
            ui.PreviewLabel.text = known.Name;
            ui.UnknownOutputIcon.gameObject.SetActive(false);
            ui.PreviewPropertiesLabel.text = List(props, null);
        }
        else
        {
            ui.PreviewIcon.sprite = product.Icon;
            ui.PreviewIcon.color = Color.black;
            ui.PreviewLabel.text = "Unknown";
            ui.UnknownOutputIcon.gameObject.SetActive(true);
            ui.PreviewPropertiesLabel.text = List(props, product.Properties);
        }
        ui.PreviewIcon.enabled = true;
        ui.PreviewLabel.enabled = true;
        ui.PreviewPropertiesLabel.enabled = true;
        LayoutRebuilder.ForceRebuildLayoutImmediate(ui.PreviewPropertiesLabel.rectTransform);
    }

    /// <summary>The game's effect list format; effects not already on the product show as "?" for a new mix.</summary>
    private static string List(Il2CppSystem.Collections.Generic.List<Effect> props, Il2CppSystem.Collections.Generic.List<Effect> hideNewAgainst)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < props.Count; i++)
        {
            var e = props[i];
            if (sb.Length > 0)
                sb.Append('\n');
            string color = ColorUtility.ToHtmlStringRGBA(e.LabelColor);
            bool hidden = hideNewAgainst != null && !hideNewAgainst.Contains(e);
            sb.Append("<color=#").Append(color).Append(">• ").Append(hidden ? "?" : e.Name).Append("</color>");
        }
        return sb.ToString();
    }

    /// <summary>The Mk2 screen's output icon: the chain's final product (black with "?" when it's new).</summary>
    public static void RefreshScreen(MixingStationMk2 station)
    {
        var chain = MixSteps.ChainOf(station);
        var op = station.CurrentMixOperation;
        if (chain == null || op == null || station.OutputIcon == null)
            return;
        var start = Registry.GetItem(op.ProductID)?.TryCast<ProductDefinition>();
        if (start == null)
            return;
        var known = MixSteps.Known(start, MixSteps.Fold(start, chain.Ingredients));
        station.OutputIcon.sprite = known != null ? known.Icon : start.Icon;
        station.OutputIcon.color = known != null ? Color.white : Color.black;
        if (station.QuestionMark != null)
            station.QuestionMark.gameObject.SetActive(known == null);
    }
}

// ---- Harmony ----

[HarmonyPatch(typeof(MixOperation), nameof(MixOperation.GetOutput))]
internal static class MixOperationGetOutputPatch
{
    private static void Postfix(MixOperation __instance, ref Il2CppSystem.Collections.Generic.List<Effect> properties)
    {
        var chain = MixSteps.ChainOf(__instance);
        if (chain == null)
            return;
        var start = Registry.GetItem(__instance.ProductID)?.TryCast<ProductDefinition>();
        if (start != null)
            properties = MixSteps.Fold(start, chain.Ingredients);
    }
}

[HarmonyPatch(typeof(MixOperation), nameof(MixOperation.IsOutputKnown))]
internal static class MixOperationIsOutputKnownPatch
{
    private static void Postfix(MixOperation __instance, ref bool __result, ref ProductDefinition knownProduct)
    {
        var chain = MixSteps.ChainOf(__instance);
        if (chain == null)
            return;
        var start = Registry.GetItem(__instance.ProductID)?.TryCast<ProductDefinition>();
        if (start == null)
            return;
        knownProduct = MixSteps.Known(start, MixSteps.Fold(start, chain.Ingredients));
        __result = knownProduct != null;
    }
}

[HarmonyPatch(typeof(MixingStation), nameof(MixingStation.RpcLogic___TryCreateOutputItems_2166136261))]
internal static class MixingStationCreateOutputPatch
{
    private static bool Prefix(MixingStation __instance) => !MixSteps.TryFinish(__instance);
}

[HarmonyPatch(typeof(MixingStation), nameof(MixingStation.RpcLogic___SetMixOperation_1073078804))]
internal static class MixingStationSetOperationPatch
{
    private static void Postfix(MixingStation __instance) => MixSteps.OperationChanged(__instance);
}

[HarmonyPatch(typeof(MixingStation), nameof(MixingStation.CanBeDestroyed))]
internal static class MixingStationCanBeDestroyedPatch
{
    /// <summary>Ingredients waiting in other steps count as contents: the station can't be picked up with them.</summary>
    private static void Postfix(MixingStation __instance, ref bool __result, ref string reason)
    {
        if (__result && MixSteps.IsMk2(__instance) && MixSteps.HasHidden(__instance))
        {
            __result = false;
            reason = "Contains items";
        }
    }
}

[HarmonyPatch(typeof(ProductManager), nameof(ProductManager.RpcLogic___FinishAndNameMix_4237212381))]
internal static class ProductManagerNameMixPatch
{
    private static bool Prefix(string productID, string ingredientID, string mixName, string mixID) =>
        !MixSteps.TryName(productID, ingredientID, mixName, mixID);
}

[HarmonyPatch(typeof(MixingStationInterface))]
internal static class MixingStationInterfacePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(MixingStationInterface.Open))]
    private static void Opened(MixingStationInterface __instance) => MixStepsUi.Opened(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MixingStationInterface.Close))]
    private static void Closed() => MixStepsUi.Closed();

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MixingStationInterface.UpdatePreview))]
    private static void Previewed(MixingStationInterface __instance)
    {
        MixStepsUi.Preview(__instance);
        MixStepsUi.Refresh();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MixingStationInterface.UpdateBeginButton))]
    private static void BeginButtonUpdated(MixingStationInterface __instance)
    {
        var station = __instance.Station;
        if (station == null || !MixStepsUi.Engaged(station) || station.CurrentMixOperation != null || station.OutputSlot.Quantity > 0)
            return;
        __instance.BeginButton.gameObject.SetActive(true);
        __instance.BeginButton.interactable = MixSteps.CannotStart(station, out _, out _) == null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MixingStationInterface.UpdateInstruction))]
    private static void InstructionUpdated(MixingStationInterface __instance)
    {
        var station = __instance.Station;
        if (station == null || !MixStepsUi.Engaged(station) || station.CurrentMixOperation != null || station.OutputSlot.Quantity > 0)
            return;
        string problem = MixSteps.CannotStart(station, out _, out _);
        __instance.InstructionLabel.enabled = problem != null;
        if (problem != null)
            __instance.InstructionLabel.text = problem;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MixingStationInterface.BeginButtonPressed))]
    private static bool BeginPressed(MixingStationInterface __instance)
    {
        var station = __instance.Station;
        if (station == null || !MixStepsUi.Engaged(station))
            return true;   // a normal single mix: the game's own path
        if (__instance.BeginButton.isActiveAndEnabled && __instance.BeginButton.interactable && MixSteps.Begin(station))
        {
            __instance.UpdatePreview();
            __instance.UpdateBeginButton();
            __instance.UpdateInstruction();
        }
        return false;
    }
}
