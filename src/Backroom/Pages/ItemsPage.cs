using System;
using System.Collections.Generic;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;
using Il2CppTMPro;
using PocketPlug.Backroom.Actions;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using UnityEngine.UI;

namespace PocketPlug.Backroom.Pages;

/// <summary>
/// Every item in the game as a picture grid (the game's own icons), filtered by category chips and the search box.
/// Selecting an item shows its details on the right (price, effects, addictiveness) with quality, packaging and
/// quantity controls and a Give button.
/// </summary>
internal sealed class ItemsPage : Page
{
    public override string Id => "items";
    public override string Title => "Items";
    public override string Icon => "package";

    private const int Columns = 5;
    private const float CellW = 124, CellH = 136, CellGap = 8;
    private const float DetailW = 420;
    private const float PadTop = 2, PadBottom = 16;
    private static readonly EQuality[] Qualities = { EQuality.Trash, EQuality.Poor, EQuality.Standard, EQuality.Premium, EQuality.Heavenly };

    /// <summary>Chip label and the game categories it shows (empty: everything).</summary>
    private static readonly (string label, EItemCategory[] categories)[] Filters =
    {
        ("All", Array.Empty<EItemCategory>()),
        ("Products", new[] { EItemCategory.Product }),
        ("Packaging", new[] { EItemCategory.Packaging }),
        ("Ingredients", new[] { EItemCategory.Ingredient }),
        ("Growing", new[] { EItemCategory.Agriculture }),
        ("Gear", new[] { EItemCategory.Tools, EItemCategory.Equipment, EItemCategory.Consumable, EItemCategory.Clothing }),
        ("Build", new[] { EItemCategory.Furniture, EItemCategory.Lighting, EItemCategory.Decoration, EItemCategory.Storage }),
    };

    private sealed class Tile
    {
        public ItemDefinition Def;
        public string Search;
        public RectTransform Rect;
        public TextMeshProUGUI Name;
    }

    private readonly List<Tile> _tiles = new();
    private readonly List<Tile> _visible = new();
    private ScrollArea _scroll;
    private RectTransform _frame;
    private Vector2 _frameFrom, _frameTo;
    private float _frameT = 1f;
    private ChipRow _chips;
    private TextMeshProUGUI _empty;
    private string _query = "";
    private int _selected = -1;   // index into _visible
    private int _registryCount;

    // Detail pane
    private RectTransform _detail, _detailBody;
    private Image _icon;
    private TextMeshProUGUI _name, _subtitle, _price, _description, _fits;
    private RectTransform _effects, _addictBar, _addictFill;
    private TextMeshProUGUI _effectsTitle, _addictTitle, _addictValue;
    private RectTransform _qualityBlock, _packagingBlock, _quantityBlock;
    private Cycle _quality;
    private Segmented _packaging;
    private PackagingDefinition[] _packagingDefs = Array.Empty<PackagingDefinition>();
    private TextMeshProUGUI _quantityLabel, _packagingTitle;
    private (Button button, TextMeshProUGUI label) _give;
    private int _quantity = 1;
    private bool _hasQuality;
    private bool _placePending;   // place the frame and scroll once layout has run (next frame)
    private int _capacity;

    public override string Breadcrumb =>
        "ITEMS  ›  " + Filters[_chips?.Selected ?? 0].label.ToUpperInvariant() + (_query.Length > 0 ? $"  ›  “{_query}”" : "");

    public override string Counter => _visible.Count == 0 ? "" : $"{_selected + 1} / {_visible.Count}";

    private ItemDefinition Current => _selected >= 0 && _selected < _visible.Count ? _visible[_selected].Def : null;

    protected internal override void Build(RectTransform root)
    {
        float width = Theme.WindowWidth - Theme.RailWidth;
        float gridWidth = width - DetailW - 24 - 24 - 16;

        var left = K.Rect("Browse", root);
        K.Fill(left, 24, 0, DetailW + 24 + 16, 0);
        var labels = new List<string>();
        foreach (var f in Filters)
            labels.Add(f.label);
        var chipHost = K.Rect("Chips", left);
        K.TopStrip(chipHost, 34);
        _chips = new ChipRow(chipHost, labels, _ => { Refilter(); Select(_visible.Count > 0 ? 0 : -1, true); }, gridWidth);
        chipHost.sizeDelta = new Vector2(chipHost.sizeDelta.x, _chips.Height);

        _scroll = new ScrollArea(left, "Grid");
        K.Fill(_scroll.Root, 0, _chips.Height + 14, 0, 0);
        var grid = _scroll.Content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CellW, CellH);
        grid.spacing = new Vector2(CellGap, CellGap);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.padding = new RectOffset(0, 0, (int)PadTop, (int)PadBottom);
        var fitter = _scroll.Content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildTiles();
        _registryCount = PersistentSingleton<Registry>.Instance.GetAllItems().Count;

        var frame = K.Rect("Selection", _scroll.Content).gameObject.AddComponent<Image>();
        frame.sprite = Util.Assets.Sprite("br_frame", new Vector4(16, 16, 16, 16));
        frame.type = Image.Type.Sliced;
        frame.color = Theme.Select;
        frame.raycastTarget = false;
        frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        _frame = frame.rectTransform;
        _frame.anchorMin = _frame.anchorMax = new Vector2(0, 1);
        _frame.sizeDelta = new Vector2(CellW + 2, CellH + 2);

        _empty = K.Label("Empty", _scroll.Viewport, "No items match", Theme.Body, Theme.Text3, Theme.Medium, TextAlignmentOptions.Center);
        K.Fill(_empty.rectTransform);

        BuildDetail(root);
        Refilter();
        Select(_visible.Count > 0 ? 0 : -1, false);
    }

    private void BuildTiles()
    {
        var all = new List<ItemDefinition>();
        foreach (var def in PersistentSingleton<Registry>.Instance.GetAllItems())
        {
            if (def == null || string.IsNullOrEmpty(def.ID) || def.ID == "cash" || string.IsNullOrEmpty(def.Name))
                continue;
            all.Add(def);
        }
        all.Sort((a, b) =>
        {
            int c = Order(a.Category).CompareTo(Order(b.Category));
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var def in all)
        {
            var tile = new Tile { Def = def, Search = (def.Name + " " + def.ID).ToLowerInvariant() };
            var bg = K.Box(def.ID, _scroll.Content, Color.white, Theme.Radius);
            tile.Rect = bg.rectTransform;
            var captured = tile;
            K.Clickable(bg, () => TileClicked(captured), Theme.Panel, Theme.Raised, Theme.RaisedHover);

            var icon = K.Rect("Icon", bg.rectTransform).gameObject.AddComponent<Image>();
            icon.sprite = def.Icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (icon.sprite == null)
            {
                icon.sprite = Util.Assets.Sprite("br_package");
                icon.color = Theme.Text3;
            }
            K.At(icon.rectTransform, 0.5f, 1, 72, 72, 0, -14);
            icon.rectTransform.pivot = new Vector2(0.5f, 1);

            tile.Name = K.Label("Name", bg.rectTransform, def.Name, Theme.Caption + 1, Theme.Text, Theme.Medium, TextAlignmentOptions.Top);
            tile.Name.enableWordWrapping = true;
            tile.Name.overflowMode = TextOverflowModes.Ellipsis;
            tile.Name.maxVisibleLines = 2;
            K.BottomStrip(tile.Name.rectTransform, 2 * Theme.Line(Theme.Caption + 1), 4, 8, 8);
            _tiles.Add(tile);
        }
    }

    private Tile _lastClicked;
    private float _lastClickAt = -10f;

    /// <summary>One click selects an item; a double-click gives it (with the chosen quality, packaging and amount).</summary>
    private void TileClicked(Tile tile)
    {
        float now = Time.unscaledTime;
        bool twice = tile == _lastClicked && now - _lastClickAt <= 0.4f;
        Select(_visible.IndexOf(tile), true);
        if (twice)
        {
            _lastClicked = null;
            Give();
            return;
        }
        _lastClicked = tile;
        _lastClickAt = now;
    }

    private static int Order(EItemCategory c) => c switch
    {
        EItemCategory.Product => 0,
        EItemCategory.Packaging => 1,
        EItemCategory.Ingredient => 2,
        EItemCategory.Agriculture => 3,
        EItemCategory.Tools => 4,
        EItemCategory.Equipment => 5,
        EItemCategory.Consumable => 6,
        _ => 10 + (int)c,
    };

    private void BuildDetail(RectTransform root)
    {
        var pane = K.Box("Detail", root, Theme.Panel, 10);
        _detail = pane.rectTransform;
        _detail.anchorMin = new Vector2(1, 0);
        _detail.anchorMax = new Vector2(1, 1);
        _detail.pivot = new Vector2(1, 0.5f);
        _detail.offsetMin = new Vector2(-DetailW - 16, 16);
        _detail.offsetMax = new Vector2(-16, 0);
        float inner = DetailW - 48;

        // Header: icon on a raised tile, name, type and price.
        var iconBg = K.Box("IconBg", _detail, Theme.Raised, 10);
        K.At(iconBg.rectTransform, 0, 1, 104, 104, 24, -24);
        _icon = K.Rect("Icon", iconBg.rectTransform).gameObject.AddComponent<Image>();
        _icon.preserveAspect = true;
        _icon.raycastTarget = false;
        K.Fill(_icon.rectTransform, 12, 12, 12, 12);

        _name = K.Label("Name", _detail, "", Theme.Title - 2, Theme.Text, Theme.SemiBold);
        _name.enableWordWrapping = true;
        _name.maxVisibleLines = 2;
        _name.alignment = TextAlignmentOptions.BottomLeft;
        K.At(_name.rectTransform, 0, 1, inner - 120, 60, 144, -20);
        _subtitle = K.Label("Subtitle", _detail, "", Theme.Small, Theme.Text2, Theme.Medium);
        K.At(_subtitle.rectTransform, 0, 1, inner - 120, Theme.Line(Theme.Small), 144, -80);
        _price = K.Label("Price", _detail, "", Theme.Small, Theme.Text, Theme.Medium);
        K.At(_price.rectTransform, 0, 1, inner - 120, Theme.Line(Theme.Small), 144, -104);

        // Body: description, effects and addictiveness (scrolls if long).
        var body = new ScrollArea(_detail, "Body");
        _detailBody = body.Content;
        body.Root.anchorMin = new Vector2(0, 0);
        body.Root.anchorMax = new Vector2(1, 1);
        body.Root.offsetMin = new Vector2(24, 0);
        body.Root.offsetMax = new Vector2(-14, -144);
        _description = K.Label("Description", _detailBody, "", Theme.Small, Theme.Text2);
        _description.enableWordWrapping = true;
        _effectsTitle = Controls.Section(_detailBody, "Effects");
        _effects = K.Rect("Effects", _detailBody);
        _addictTitle = Controls.Section(_detailBody, "Addictiveness");
        _addictValue = K.Label("Value", _detailBody, "", Theme.Caption, Theme.Text2, Theme.Medium, TextAlignmentOptions.MidlineRight);
        var bar = K.Box("Bar", _detailBody, Theme.Raised, 3);
        _addictBar = bar.rectTransform;
        _addictFill = K.Box("Fill", _addictBar, Theme.Text2, 3).rectTransform;

        // Controls pinned to the bottom (laid out bottom-up in Layout()).
        _give = Controls.Primary(_detail, "Give", Give);
        _fits = K.Label("Fits", _detail, "", Theme.Caption, Theme.Text2, Theme.Medium, TextAlignmentOptions.MidlineRight);

        _quantityBlock = K.Rect("Quantity", _detail);
        Controls.Section(_quantityBlock, "Quantity");
        var minus = Widgets.IconButton(_quantityBlock, "minus", 40, () => SetQuantity(_quantity - StepSize()));
        var plus = Widgets.IconButton(_quantityBlock, "plus", 40, () => SetQuantity(_quantity + StepSize()));
        var valueBg = K.Box("Value", _quantityBlock, Theme.Raised, Theme.Radius);
        _quantityLabel = K.Label("Label", valueBg.rectTransform, "1", Theme.Body, Theme.Text, Theme.SemiBold, TextAlignmentOptions.Center);
        K.Fill(_quantityLabel.rectTransform);
        K.At(minus, 0, 0, 40, 40, 0, 0);
        K.At(valueBg.rectTransform, 0, 0, 72, 40, 46, 0);
        K.At(plus, 0, 0, 40, 40, 124, 0);
        float px = 176;
        foreach (var (label, action) in new (string, Action)[]
                 {
                     ("1", () => SetQuantity(1)), ("10", () => SetQuantity(10)),
                     ("Stack", () => SetQuantity(Current != null ? Current.StackLimit : 1)), ("Fill", () => SetQuantity(_capacity)),
                 })
        {
            var b = Controls.Secondary(_quantityBlock, label, action, 40);
            float w = label.Length > 2 ? 58 : 40;
            K.At(b.button.GetComponent<RectTransform>(), 0, 0, w, 40, px, 0);
            px += w + 6;
        }

        _packagingBlock = K.Rect("Packaging", _detail);
        _packagingTitle = Controls.Section(_packagingBlock, "Packaging");

        _qualityBlock = K.Rect("Quality", _detail);
        Controls.Section(_qualityBlock, "Quality");
        var qLabels = new List<string>();
        var qDots = new List<Color?>();
        foreach (var q in Qualities)
        {
            qLabels.Add(q.ToString());
            qDots.Add(ItemQuality.GetColor(q));
        }
        _quality = new Cycle(_qualityBlock, qLabels, _ => RefreshDetail(false), qDots);
    }

    /// <summary>Places the bottom controls for the current item, bottom-up, and fits the body above them.</summary>
    private void Layout(bool hasQuality, bool hasPackaging)
    {
        float inner = DetailW - 48;
        float y = 24;
        var giveRect = _give.button.GetComponent<RectTransform>();
        K.At(giveRect, 0, 0, inner, 52, 24, y);
        y += 52 + 10;
        K.At(_quantityBlock, 0, 0, inner, 64, 24, y);
        K.At(_fits.rectTransform, 0, 0, inner, Theme.Line(Theme.Caption), 24, y + 64 - Theme.Line(Theme.Caption));
        PlaceSection(_quantityBlock);
        y += 64 + 14;

        _packagingBlock.gameObject.SetActive(hasPackaging);
        if (hasPackaging)
        {
            K.At(_packagingBlock, 0, 0, inner, 64, 24, y);
            PlaceSection(_packagingBlock);
            if (_packaging != null)
                K.At(_packaging.Root, 0, 0, inner, 40, 0, 0);
            y += 64 + 14;
        }
        _qualityBlock.gameObject.SetActive(hasQuality);
        if (hasQuality)
        {
            K.At(_qualityBlock, 0, 0, inner, 64, 24, y);
            PlaceSection(_qualityBlock);
            K.At(_quality.Root, 0, 0, inner, 40, 0, 0);
            y += 64 + 14;
        }

        var body = _detailBody.parent.parent.GetComponent<RectTransform>();
        body.offsetMin = new Vector2(24, y);
    }

    /// <summary>Section labels sit at the top of their block, the control at the bottom.</summary>
    private static void PlaceSection(RectTransform block)
    {
        var label = block.Find("Section_" + block.name);
        if (label != null)
            K.At(label.GetComponent<RectTransform>(), 0, 1, 300, Theme.Line(Theme.Caption), 0, 0);
    }

    private void Refilter()
    {
        var categories = Filters[_chips.Selected].categories;
        var previous = Current;
        _visible.Clear();
        foreach (var tile in _tiles)
        {
            bool show = (categories.Length == 0 || Array.IndexOf(categories, tile.Def.Category) >= 0)
                        && (_query.Length == 0 || tile.Search.Contains(_query));
            tile.Rect.gameObject.SetActive(show);
            if (show)
                _visible.Add(tile);
        }
        _empty.gameObject.SetActive(_visible.Count == 0);
        _selected = previous != null ? _visible.FindIndex(t => t.Def.Pointer == previous.Pointer) : -1;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.Content);
        _scroll.Content.anchoredPosition = Vector2.zero;
        PlaceFrame(true);
        Backroom.Shell?.RefreshCrumb();
    }

    private void Select(int index, bool animate)
    {
        if (index >= _visible.Count)
            index = _visible.Count - 1;
        bool changed = index != _selected;
        _selected = index;
        for (int i = 0; i < _visible.Count; i++)
            _visible[i].Name.color = i == index ? Theme.Text : Theme.Text2;
        PlaceFrame(!animate);
        RevealSelected();
        if (changed)
        {
            _quantity = 1;
            RefreshDetail(true);
        }
        Backroom.Shell?.RefreshCrumb();
    }

    /// <summary>
    /// Where cell <paramref name="index"/> sits in the grid (centre, relative to the content's top-left), worked out
    /// from the grid's fixed geometry rather than read back from layout, which may not have run yet.
    /// </summary>
    private static Vector2 CellCenter(int index)
    {
        int row = index / Columns, col = index % Columns;
        return new Vector2(col * (CellW + CellGap) + CellW / 2, -(PadTop + row * (CellH + CellGap) + CellH / 2));
    }

    private float ContentHeight()
    {
        int rows = (_visible.Count + Columns - 1) / Columns;
        return rows == 0 ? 0 : PadTop + rows * (CellH + CellGap) - CellGap + PadBottom;
    }

    private void RevealSelected()
    {
        if (_selected < 0)
            return;
        float top = PadTop + (_selected / Columns) * (CellH + CellGap);
        _scroll.Reveal(top, top + CellH, ContentHeight());
    }

    private void PlaceFrame(bool snap)
    {
        bool has = _selected >= 0 && _selected < _visible.Count;
        _frame.gameObject.SetActive(has);
        if (!has)
            return;
        _frame.SetAsLastSibling();
        var target = CellCenter(_selected);
        _frameFrom = snap ? target : _frame.anchoredPosition;
        _frameTo = target;
        _frameT = snap ? 1f : 0f;
        if (snap)
            _frame.anchoredPosition = target;
    }

    /// <summary>Fills the detail pane for the selected item. <paramref name="reset"/> also resets quality and packaging.</summary>
    private void RefreshDetail(bool reset)
    {
        var def = Current;
        _detail.gameObject.SetActive(def != null);
        if (def == null)
            return;

        var product = def.TryCast<ProductDefinition>();
        var storable = def.TryCast<StorableItemDefinition>();

        if (reset)
        {
            // Quality lives on the instance (products are QualityItemInstances; their definitions aren't
            // QualityItemDefinitions), so ask a sample instance.
            var sample = def.GetDefaultInstance(1)?.TryCast<QualityItemInstance>();
            _hasQuality = sample != null;
            _quality.Select(sample != null ? Mathf.Max(0, Array.IndexOf(Qualities, sample.Quality)) : 2, false);
            RebuildPackaging(product);
        }

        _name.text = def.Name;
        _subtitle.text = product != null ? $"{Pretty(product.DrugType.ToString())}  ·  Product" : Pretty(def.Category.ToString());
        if (product != null && NetworkSingleton<ProductManager>.InstanceExists)
            _price.text = $"Market {K.Money(product.MarketValue)}   <color=#5F656D>·</color>   Yours {K.Money(NetworkSingleton<ProductManager>.Instance.GetPrice(product))}";
        else if (storable != null && storable.BasePurchasePrice > 0)
            _price.text = $"{K.Money(storable.BasePurchasePrice)} in shops";
        else
            _price.text = "";

        var packaging = SelectedPackaging();
        Sprite icon = def.Icon;
        if (product != null && packaging != null && Singleton<ProductIconManager>.InstanceExists)
            icon = Singleton<ProductIconManager>.Instance.GetIcon(def.ID, packaging.ID, true) ?? def.Icon;
        _icon.sprite = icon;
        _icon.color = icon != null ? Color.white : new Color(0, 0, 0, 0);

        LayoutBody(def, product);
        Layout(_hasQuality, product != null && _packagingDefs.Length > 0);
        RefreshQuantity();
    }

    private void LayoutBody(ItemDefinition def, ProductDefinition product)
    {
        float inner = DetailW - 48 - 10;
        float y = 0;
        string desc = string.IsNullOrWhiteSpace(def.Description) ? "" : def.Description.Trim();
        _description.text = desc;
        _description.gameObject.SetActive(desc.Length > 0);
        if (desc.Length > 0)
        {
            float h = _description.GetPreferredValues(desc, inner, 0).y;
            K.At(_description.rectTransform, 0, 1, inner, h, 0, -y);
            y += h + 18;
        }

        var pills = new List<(string, Color?)>();
        if (product != null)
        {
            var effects = product.Properties;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e != null)
                    pills.Add((e.Name, e.LabelColor));
            }
        }
        bool hasEffects = pills.Count > 0;
        _effectsTitle.gameObject.SetActive(hasEffects);
        _effects.gameObject.SetActive(hasEffects);
        if (hasEffects)
        {
            K.At(_effectsTitle.rectTransform, 0, 1, inner, Theme.Line(Theme.Caption), 0, -y);
            y += 26;
            float h = Controls.Pills(_effects, pills, inner);
            K.At(_effects, 0, 1, inner, h, 0, -y);
            y += h + 18;
        }

        bool hasAddict = product != null;
        _addictTitle.gameObject.SetActive(hasAddict);
        _addictValue.gameObject.SetActive(hasAddict);
        _addictBar.gameObject.SetActive(hasAddict);
        if (hasAddict)
        {
            float a = Mathf.Clamp01(product.GetAddictiveness());
            K.At(_addictTitle.rectTransform, 0, 1, inner, Theme.Line(Theme.Caption), 0, -y);
            K.At(_addictValue.rectTransform, 0, 1, inner, Theme.Line(Theme.Caption), 0, -y);
            _addictValue.text = $"{a * 100f:0}%";
            y += 26;
            K.At(_addictBar, 0, 1, inner, 6, 0, -y);
            _addictFill.anchorMin = Vector2.zero;
            _addictFill.anchorMax = new Vector2(Mathf.Max(a, 0.02f), 1);
            _addictFill.offsetMin = _addictFill.offsetMax = Vector2.zero;
            y += 6 + 18;
        }
        _detailBody.sizeDelta = new Vector2(_detailBody.sizeDelta.x, y);
        _detailBody.anchoredPosition = Vector2.zero;
    }

    private void RebuildPackaging(ProductDefinition product)
    {
        if (_packaging != null)
            UnityEngine.Object.Destroy(_packaging.Root.gameObject);
        _packaging = null;
        var defs = new List<PackagingDefinition>();
        if (product != null && product.ValidPackaging != null)
        {
            foreach (var p in product.ValidPackaging)
            {
                if (p != null)
                    defs.Add(p);
            }
        }
        _packagingDefs = defs.ToArray();
        if (_packagingDefs.Length == 0)
            return;
        var labels = new List<string> { "Loose" };
        foreach (var p in _packagingDefs)
            labels.Add(p.Name);
        _packaging = new Segmented(_packagingBlock, labels, _ => RefreshDetail(false));
        _packaging.Select(0, false);
    }

    private PackagingDefinition SelectedPackaging() =>
        _packaging != null && _packaging.Selected > 0 && _packaging.Selected <= _packagingDefs.Length ? _packagingDefs[_packaging.Selected - 1] : null;

    private EQuality? SelectedQuality() =>
        _hasQuality ? Qualities[Mathf.Clamp(_quality.Selected, 0, Qualities.Length - 1)] : null;

    private int StepSize() =>
        UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.shiftKey.isPressed ? 10 : 1;

    private void SetQuantity(int value)
    {
        _quantity = Mathf.Clamp(value, 1, Mathf.Max(1, _capacity));
        RefreshQuantity();
    }

    /// <summary>Recomputes how much fits and updates the quantity, the "fits" line and the Give button.</summary>
    private void RefreshQuantity()
    {
        var def = Current;
        if (def == null)
            return;
        var sample = ItemActions.Make(def, 1, SelectedQuality(), SelectedPackaging());
        _capacity = ItemActions.Capacity(sample);
        _quantity = Mathf.Clamp(_quantity, 1, Mathf.Max(1, _capacity));
        _quantityLabel.text = _quantity.ToString();
        _fits.text = _capacity > 0 ? $"Room for {_capacity}" : "<color=#D46A6A>Inventory full</color>";
        var packaging = SelectedPackaging();
        string what = packaging != null ? $"{_quantity} × {packaging.Name}" : _quantity.ToString();
        _give.label.text = _capacity > 0 ? $"Give {what}" : "No room in inventory";
        Controls.SetEnabled(_give.button, _give.label, _capacity > 0);
    }

    private void Give()
    {
        var def = Current;
        if (def == null || _capacity <= 0 || !K.Commit())
            return;
        var quality = SelectedQuality();
        var packaging = SelectedPackaging();
        int given = ItemActions.Give(def, _quantity, quality, packaging);
        string extra = (quality.HasValue && quality.Value != EQuality.Standard ? $", {quality.Value}" : "") + (packaging != null ? $", {packaging.Name}" : "");
        if (given <= 0)
            Backroom.Toast($"Couldn't give {def.Name}: no room", false);
        else if (given < _quantity)
            Backroom.Toast($"Gave {given} of {_quantity} × {def.Name}{extra}: inventory full", false);
        else
            Backroom.Toast($"Gave {given} × {def.Name}{extra}");
        Core.Log.Msg($"Backroom: gave {given}/{_quantity} {def.ID} quality={quality} packaging={packaging?.ID}");
        RefreshQuantity();
    }

    protected internal override void OnShow()
    {
        // Mixes the player created are registered while the save loads, after this page was first built.
        int count = PersistentSingleton<Registry>.Instance.GetAllItems().Count;
        bool rebuilt = count != _registryCount;
        if (rebuilt)
        {
            foreach (var tile in _tiles)
            {
                // Detach now: Destroy only happens at the end of the frame, and the grid would count them until then.
                tile.Rect.SetParent(null, false);
                UnityEngine.Object.Destroy(tile.Rect.gameObject);
            }
            _tiles.Clear();
            _visible.Clear();
            _selected = -1;
            BuildTiles();
            _registryCount = count;
        }
        string query = Backroom.Query;
        if (rebuilt || query != _query)
        {
            _query = query;
            Refilter();
        }
        if (_selected < 0 && _visible.Count > 0)
            Select(0, false);
        else
            RefreshDetail(false);
        // The grid was laid out while hidden; place the frame and scroll after the next layout pass.
        _placePending = true;
    }

    protected internal override void Tick()
    {
        if (_placePending)
        {
            _placePending = false;
            PlaceFrame(true);
            RevealSelected();
        }
        if (_frameT < 1f)
        {
            _frameT = Mathf.Min(1f, _frameT + Time.unscaledDeltaTime / Theme.SelectTime);
            _frame.anchoredPosition = Vector2.Lerp(_frameFrom, _frameTo, Shell.Ease(_frameT));
        }
        string query = Backroom.Query;
        if (query != _query)
        {
            _query = query;
            Refilter();
            Select(_visible.Count > 0 ? 0 : -1, true);
        }
    }

    protected internal override bool OnNavigate(Nav key)
    {
        if (_visible.Count == 0)
            return false;
        int i = Mathf.Max(_selected, 0);
        switch (key)
        {
            case Nav.Left: Select(Mathf.Max(0, i - 1), true); return true;
            case Nav.Right: Select(Mathf.Min(_visible.Count - 1, i + 1), true); return true;
            case Nav.Up:
                if (i < Columns)
                    return false;   // top row: let the menu move focus up
                Select(i - Columns, true);
                return true;
            case Nav.Down: Select(Mathf.Min(_visible.Count - 1, i + Columns), true); return true;
            case Nav.Submit: Give(); return true;
            case Nav.NextSection: _chips.Select((_chips.Selected + 1) % Filters.Length, true); return true;
            case Nav.PreviousSection: _chips.Select((_chips.Selected + Filters.Length - 1) % Filters.Length, true); return true;
        }
        return false;
    }

    /// <summary>Dev: selects an item by ID (clearing filters), sets quality/packaging/quantity, optionally gives.</summary>
    public string DevSelect(string id, int quantity, string quality, string packaging, bool give)
    {
        _chips.Select(0, false);
        Backroom.Shell.Search.text = "";
        _query = "";
        Refilter();
        int index = _visible.FindIndex(t => t.Def.ID.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return $"no item '{id}'";
        Select(index, false);
        if (quality != null)
            _quality.Select(Mathf.Max(0, Array.FindIndex(Qualities, q => q.ToString().Equals(quality, StringComparison.OrdinalIgnoreCase))), true);
        if (packaging != null && _packaging != null)
            _packaging.Select(Mathf.Max(0, Array.FindIndex(_packagingDefs, p => p.ID.Equals(packaging, StringComparison.OrdinalIgnoreCase)) + 1), true);
        SetQuantity(quantity);
        if (give)
            Give();
        return $"{Current.ID} qty={_quantity} quality={SelectedQuality()} packaging={SelectedPackaging()?.ID} capacity={_capacity} button='{_give.label.text}'";
    }

    private static string Pretty(string enumName)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in enumName)
        {
            if (char.IsUpper(c) && sb.Length > 0)
                sb.Append(' ');
            sb.Append(c == '_' ? ' ' : c);
        }
        return sb.ToString();
    }
}
