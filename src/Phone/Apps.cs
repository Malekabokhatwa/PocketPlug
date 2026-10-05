using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using PocketPlug.Util;
using UnityEngine;
using UnityEngine.UI;
using GamePhone = Il2CppScheduleOne.UI.Phone.Phone;
using Object = UnityEngine.Object;

namespace PocketPlug.Phone;

/// <summary>
/// A phone app built from scratch at runtime. The game's apps derive from a generic singleton that an IL2CPP mod
/// can't subclass, so this follows the same steps as <c>App&lt;T&gt;.SetOpen</c> by hand (approach adapted from S1API, MIT).
/// </summary>
internal abstract class CustomApp
{
    public abstract string Id { get; }
    public abstract string Label { get; }
    public abstract string IconName { get; }
    public virtual bool Enabled => true;

    internal GameObject Panel;
    internal GameObject Container;
    internal GameObject Icon;
    public bool IsOpen { get; internal set; }

    /// <summary>Builds the app's UI once, inside a full-size portrait container.</summary>
    protected internal abstract void Build(RectTransform root);

    /// <summary>Called every time the app opens, to refresh what it shows.</summary>
    protected internal virtual void OnOpened() { }

    /// <summary>Called every frame while the app is open.</summary>
    protected internal virtual void Tick() { }
}

internal static class Apps
{
    public static readonly SettingsApp Settings = new();
    public static readonly BankApp Bank = new();
    private static readonly CustomApp[] All = { Bank, Settings };

    private static HomeScreen _spawnedFor;
    private static GameInput.ExitDelegate _exitDelegate;

    public static void OnSceneChanged()
    {
        _spawnedFor = null;
        foreach (var app in All)
            app.IsOpen = false;
    }

    public static void Update()
    {
        if (!PlayerSingleton<HomeScreen>.InstanceExists)
            return;

        var home = PlayerSingleton<HomeScreen>.Instance;
        if (_spawnedFor == null || _spawnedFor.Pointer != home.Pointer)
        {
            Spawn(home);
            _spawnedFor = home;
        }

        foreach (var app in All)
        {
            if (!app.IsOpen)
                continue;
            if (!PlayerSingleton<GamePhone>.Instance.IsOpen || GamePhone.ActiveApp == null || GamePhone.ActiveApp.Pointer != app.Panel.Pointer)
            {
                // The phone was put away or another app took over.
                SetOpen(app, false);
                continue;
            }
            if (ClickedPhoneButton())
            {
                SetOpen(app, false);
                continue;
            }
            app.Tick();
        }
    }

    /// <summary>Show or hide icons after settings change, and close apps that were switched off.</summary>
    public static void Refresh()
    {
        foreach (var app in All)
        {
            if (app.Icon != null)
                app.Icon.SetActive(app.Enabled);
            if (app.IsOpen && !app.Enabled)
                SetOpen(app, false);
            else if (app.IsOpen)
                app.OnOpened();
        }
    }

    public static CustomApp Find(string id)
    {
        foreach (var app in All)
        {
            if (string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase))
                return app;
        }
        return null;
    }

    public static void Open(string id)
    {
        foreach (var app in All)
        {
            if (string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                if (GamePhone.ActiveApp != null)
                    PlayerSingleton<GamePhone>.Instance.RequestCloseApp();
                SetOpen(app, true);
            }
        }
    }

    private static void Spawn(HomeScreen home)
    {
        Ui.Font = home.appIconPrefab.transform.Find("Label").GetComponent<Text>().font;
        var appsCanvas = home.transform.parent.Find("AppsCanvas");
        if (appsCanvas == null)
        {
            Core.Log.Error("Phone AppsCanvas not found; apps disabled.");
            return;
        }

        foreach (var app in All)
        {
            try
            {
                SpawnApp(app, home, appsCanvas);
            }
            catch (Exception e)
            {
                Core.Log.Error($"Couldn't create the {app.Label} app: {e}");
            }
        }

        var phone = PlayerSingleton<GamePhone>.Instance;
        phone.closeApps += (Action)CloseAll;
        _exitDelegate = DelegateSupport.ConvertDelegate<GameInput.ExitDelegate>(new Action<ExitAction>(OnExit));
        GameInput.RegisterExitListener(_exitDelegate, 1);
        Refresh();
    }

    private static void SpawnApp(CustomApp app, HomeScreen home, Transform appsCanvas)
    {
        // Panel: a portrait page inside the landscape apps canvas, rotated like the game's vertical apps.
        var panel = new GameObject($"PocketPlug_{app.Id}");
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.SetParent(appsCanvas, false);
        panel.layer = appsCanvas.gameObject.layer;
        var parentRect = appsCanvas.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(parentRect.rect.height, parentRect.rect.width);
        panelRect.localRotation = Quaternion.Euler(0f, 0f, 90f);

        var container = Ui.Rect("Container", panelRect);
        Ui.Stretch(container);
        app.Panel = panel;
        app.Container = container.gameObject;
        app.Build(container);
        container.gameObject.SetActive(false);

        // Home screen icon, cloned from the game's own prefab so it looks and navigates like the rest.
        var icon = Object.Instantiate(home.appIconPrefab, home.appIconContainer);
        icon.name = $"PocketPlug_{app.Id}_Icon";
        icon.transform.Find("Mask/Image").GetComponent<Image>().sprite = Assets.Sprite(app.IconName);
        icon.transform.Find("Label").GetComponent<Text>().text = app.Label;
        icon.transform.Find("Notifications")?.gameObject.SetActive(false);
        var button = icon.GetComponent<Button>();
        button.onClick.AddListener((Action)(() => SetOpen(app, !app.IsOpen)));
        home.appIcons.Add(button);
        home.uiPanel.AddSelectable(icon.GetComponent<UISelectable>());
        app.Icon = icon;
    }

    private static void SetOpen(CustomApp app, bool open)
    {
        if (app.Panel == null)
            return;
        if (open && !app.Enabled)
            return;

        app.IsOpen = open;
        PlayerSingleton<AppsCanvas>.Instance.SetIsOpen(open);
        PlayerSingleton<HomeScreen>.Instance.SetIsOpen(!open);
        var phone = PlayerSingleton<GamePhone>.Instance;
        phone.SetLookOffsetMultiplier(1f);

        if (open)
        {
            GamePhone.ActiveApp = app.Panel;
            app.Container.SetActive(true);
            app.OnOpened();
        }
        else
        {
            if (GamePhone.ActiveApp != null && GamePhone.ActiveApp.Pointer == app.Panel.Pointer)
                GamePhone.ActiveApp = null;
            phone.SetIsHorizontal(false);
            app.Container.SetActive(false);
        }
    }

    private static void CloseAll()
    {
        foreach (var app in All)
        {
            if (app.IsOpen)
                SetOpen(app, false);
        }
    }

    private static void OnExit(ExitAction action)
    {
        if (action.Used || !PlayerSingleton<GamePhone>.Instance.IsOpen)
            return;
        foreach (var app in All)
        {
            if (app.IsOpen)
            {
                action.Used = true;
                SetOpen(app, false);
                return;
            }
        }
    }

    /// <summary>Same check the game's apps use: a click on the phone's physical home button.</summary>
    private static bool ClickedPhoneButton()
    {
        if (!GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick))
            return false;
        var cam = Singleton<GameplayMenu>.Instance.OverlayCamera;
        var ray = cam.ScreenPointToRay(GameInput.MousePosition);
        return Physics.Raycast(ray, out var hit, 2f, 1 << LayerMask.NameToLayer("Overlay")) && hit.collider.gameObject.name == "Button";
    }
}
