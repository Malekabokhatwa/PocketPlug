using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.State;
using Il2CppScheduleOne.UI;
using PocketPlug.Backroom.Pages;
using UnityEngine;
using UnityEngine.InputSystem;
using GameState = Il2CppScheduleOne.State.State;

namespace PocketPlug.Backroom;

/// <summary>
/// PocketPlug Backroom, the mod menu. Hold the key (B) to open it over the running game: time keeps going,
/// the player stands still and the mouse drives the menu. Esc, the close button or holding the key again closes it.
/// The menu is a game State, so the game itself restores movement, camera and cursor when it closes.
/// </summary>
internal static class Backroom
{
    private const string UiName = "PocketPlugBackroom";
    /// <summary>The ring only appears after this much holding, so a quick tap shows nothing.</summary>
    private const float RingDelay = 0.1f;

    public static readonly List<Page> Pages = new()
    {
        new HomePage(), new ItemsPage(), new MoneyPage(), new PlayerPage(), new WorldPage(), new TravelPage(),
        new PeoplePage(), new PropertyPage(), new ProgressPage(), new SystemPage(), new PocketPlugPage(),
    };

    private static Shell _shell;
    private static HoldRing _ring;
    private static GameState _state;
    private static Il2CppSystem.Action _onRemoved;

    private static bool _open;
    private static float _fade;          // 0 hidden .. 1 fully shown
    private static float _hold;
    private static bool _latched;        // the key must be released before another hold counts
    private static bool _setTyping;      // we set GameInput.IsTyping (search field focused)
    private static int _page;

    public static bool IsOpen => _open;
    public static Shell Shell => _shell;

    public static float HoldSeconds => Mathf.Clamp(Config.BackroomHold.Value, 0.15f, 1.5f);

    /// <summary>Builds the whole UI behind the loading screen, so the first open costs nothing.</summary>
    public static void Warmup()
    {
        if (_shell != null)
            return;
        if (!Theme.LoadFonts())
        {
            Core.Log.Warning("Backroom: the game's fonts aren't loaded yet; building on first use.");
            return;
        }
        var root = new GameObject("PocketPlug Backroom Root");
        _shell = new Shell(Pages);
        _shell.Build(root.transform);
        _shell.BuildAll();
        _shell.Canvas.gameObject.SetActive(false);
        _ring = new HoldRing(root.transform);
        _onRemoved = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(OnStateRemoved));
    }

    /// <summary>Scene change: the UI was destroyed with the scene.</summary>
    public static void Reset()
    {
        _shell = null;
        _ring = null;
        _state = null;
        _open = false;
        _fade = 0f;
        _hold = 0f;
        _latched = false;
        if (_setTyping)
            GameInput.IsTyping = false;
        _setTyping = false;
        Recents.Clear();   // the actions reference objects of the old scene
        Fields.Clear();
        Cheats.Reset();
        Desk.BackroomDesk.Reset();
        foreach (var page in Pages)
        {
            page.Built = false;
            page.Root = null;
        }
    }

    public static void Update()
    {
        if (_shell == null || _shell.Canvas == null)
        {
            if (_shell != null)
                Reset();
            return;
        }

        if (!Config.Backroom.On)
        {
            if (_open)
                Close();
            Animate();
            return;
        }

        Cheats.Update();
        Desk.BackroomDesk.Update();
        HandleKey();
        if (_open)
        {
            UpdateTyping();
            HandleNavigation();
            _shell.Tick();
            var page = Pages[_shell.Current];
            page.Tick();
        }
        Animate();
    }

    private static void HandleKey()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;
        bool down = keyboard[Key()].isPressed;
        if (!down)
        {
            // While open, a quick tap of the key is just typing (B is a letter): it starts a search.
            if (_open && !_latched && _hold > 0f && _hold < HoldSeconds && !AnyFieldFocused())
                TypeIntoSearch(Key().ToString().ToLowerInvariant());
            _latched = false;
            _hold = 0f;
            _ring.Set(0f);
            return;
        }
        if (_latched)
            return;

        // While open, the key only closes when no text box is being typed in.
        if (_open && AnyFieldFocused())
        {
            _hold = 0f;
            return;
        }

        if (!_open && !CanOpen(out string reason))
        {
            _latched = true;
            if (reason != null)
                _ring.Refuse(reason);
            return;
        }

        _hold += Time.unscaledDeltaTime;
        _ring.Set(_hold < RingDelay ? 0f : _hold / HoldSeconds);
        if (_hold < HoldSeconds)
            return;
        _latched = true;
        _hold = 0f;
        _ring.Set(0f);
        if (_open)
            Close();
        else
            Open();
    }

    private static Key Key() =>
        Enum.TryParse(Config.BackroomKey.Value, true, out Key key) && key != UnityEngine.InputSystem.Key.None
            ? key
            : UnityEngine.InputSystem.Key.B;

    /// <summary>Whether the menu may open now; <paramref name="reason"/> says why not (null: don't say anything).</summary>
    public static bool CanOpen(out string reason)
    {
        reason = null;
        if (!Singleton<LoadManager>.InstanceExists)
            return false;
        var load = Singleton<LoadManager>.Instance;
        if (!load.IsGameLoaded || load.IsLoading || SceneState.Current == null)
            return false;
        if (GameInput.IsTyping || (Singleton<PauseMenu>.InstanceExists && Singleton<PauseMenu>.Instance.IsPaused))
            return false;
        var player = Player.Local;
        if (player == null || !PlayerSingleton<PlayerCamera>.InstanceExists)
            return false;
        if (player.CurrentVehicle != null)
        {
            reason = "Step out of the vehicle first";
            return false;
        }
        if (player.IsArrested || player.IsUnconscious || (player.Health != null && !player.Health.IsAlive))
        {
            reason = "Not available right now";
            return false;
        }
        if (PlayerSingleton<PlayerCamera>.Instance.ActiveUIElementCount > 0)
        {
            reason = "Close the other menu first";
            return false;
        }
        return true;
    }

    /// <summary>Opens the menu (dev commands call this directly, skipping the hold).</summary>
    public static void Open()
    {
        if (_open || _shell == null)
            return;
        _state = new GameState(UiName, new IStateMachine(SceneState.Current.InGame.Pointer), StateProperties.EPreset.Unenforced);
        _state.Properties = new StateProperties(
            StateProperties.EMouseState.Free,
            StateProperties.ECrosshairState.Hidden,
            StateProperties.EEquippingState.Disabled,
            StateProperties.EInventoryState.Disabled,
            StateProperties.EHUDState.Visible,
            StateProperties.ECompassState.Hidden,
            StateProperties.EMovementState.Locked,
            StateProperties.ELookState.Locked,
            StateProperties.EBlurState.Disabled);
        _state.EnableAutoExitListener(0);
        _state.add_OnRemovedFromStack(_onRemoved);
        _state.PushToDefaultParent();
        PlayerSingleton<PlayerCamera>.Instance.AddActiveUIElement(UiName);

        _open = true;
        _shell.Canvas.gameObject.SetActive(true);
        _shell.Select(Mathf.Clamp(_page, 0, Pages.Count - 1));
        _shell.SnapRail();
        _shell.RefreshReadouts();
    }

    public static void Close()
    {
        if (!_open)
            return;
        if (_state != null)
            _state.RemoveFromDefaultParent();   // fires OnStateRemoved
        else
            OnStateRemoved();
    }

    /// <summary>The game popped our state (Esc) or we removed it: hand the player back.</summary>
    private static void OnStateRemoved()
    {
        if (!_open)
            return;
        _open = false;
        _state = null;
        if (PlayerSingleton<PlayerCamera>.InstanceExists)
            PlayerSingleton<PlayerCamera>.Instance.RemoveActiveUIElement(UiName);
        if (_shell != null)
        {
            _page = _shell.Current;
            Pages[_shell.Current].OnHide();
            foreach (var field in Fields)
            {
                if (field != null)
                    field.DeactivateInputField();
            }
        }
        if (_setTyping)
        {
            GameInput.IsTyping = false;
            _setTyping = false;
        }
    }

    /// <summary>Recently run actions, newest first (shown on Home).</summary>
    public static readonly List<(string title, Action run)> Recents = new();

    public static void Recent(string title, Action run)
    {
        Recents.RemoveAll(r => r.title == title);
        Recents.Insert(0, (title, run));
        if (Recents.Count > 8)
            Recents.RemoveAt(Recents.Count - 1);
    }

    /// <summary>The search box text, trimmed and lowercased.</summary>
    public static string Query => _shell?.Search != null ? _shell.Search.text.Trim().ToLowerInvariant() : "";

    private static readonly float[] RepeatAt = new float[4];
    private const float RepeatDelay = 0.38f, RepeatRate = 0.055f;

    /// <summary>Arrow keys (with key repeat), Enter, Backspace, Tab, Page Up/Down, and type-to-search.</summary>
    private static void HandleNavigation()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;
        var search = _shell.Search;
        if (search.isFocused)
        {
            // Down or Enter leaves the box and goes to the results.
            if (kb.downArrowKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                search.DeactivateInputField();
            return;
        }
        if (AnyFieldFocused())
            return;   // another box (the console line) handles its own keys

        var page = Pages[_shell.Current];
        if (Repeat(kb.upArrowKey, 0)) Navigate(page, Nav.Up);
        if (Repeat(kb.downArrowKey, 1)) Navigate(page, Nav.Down);
        if (Repeat(kb.leftArrowKey, 2)) Navigate(page, Nav.Left);
        if (Repeat(kb.rightArrowKey, 3)) Navigate(page, Nav.Right);
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            Navigate(page, Nav.Submit);
        if (kb.backspaceKey.wasPressedThisFrame)
            Navigate(page, Nav.Back);
        if (kb.tabKey.wasPressedThisFrame)
            Navigate(page, kb.shiftKey.isPressed ? Nav.PreviousSection : Nav.NextSection);
        if (kb.pageDownKey.wasPressedThisFrame)
            Show((_shell.Current + 1) % Pages.Count);
        if (kb.pageUpKey.wasPressedThisFrame)
            Show((_shell.Current + Pages.Count - 1) % Pages.Count);

        // Type to search: the first letter or digit focuses the box (the menu key is handled as a hold/tap).
        var menuKey = Key();
        for (var k = UnityEngine.InputSystem.Key.A; k <= UnityEngine.InputSystem.Key.Z; k++)
        {
            if (k != menuKey && kb[k].wasPressedThisFrame)
            {
                TypeIntoSearch(kb.shiftKey.isPressed ? k.ToString() : k.ToString().ToLowerInvariant());
                return;
            }
        }
        for (var k = UnityEngine.InputSystem.Key.Digit1; k <= UnityEngine.InputSystem.Key.Digit0; k++)
        {
            if (k != menuKey && kb[k].wasPressedThisFrame)
            {
                TypeIntoSearch(k == UnityEngine.InputSystem.Key.Digit0 ? "0" : ((int)(k - UnityEngine.InputSystem.Key.Digit1) + 1).ToString());
                return;
            }
        }
    }

    private static void Navigate(Page page, Nav key)
    {
        if (page.OnNavigate(key))
            return;
        // Pages that don't use these: Backspace clears a search; Up/Down at the edges moves through the rail.
        if (key == Nav.Back && _shell.Search.text.Length > 0)
            _shell.Search.text = "";
        else if (key == Nav.Up)
            Show(Mathf.Max(0, _shell.Current - 1));
        else if (key == Nav.Down)
            Show(Mathf.Min(Pages.Count - 1, _shell.Current + 1));
    }

    private static bool Repeat(UnityEngine.InputSystem.Controls.KeyControl key, int slot)
    {
        float now = Time.unscaledTime;
        if (key.wasPressedThisFrame)
        {
            RepeatAt[slot] = now + RepeatDelay;
            return true;
        }
        if (key.isPressed && now >= RepeatAt[slot])
        {
            RepeatAt[slot] = now + RepeatRate;
            return true;
        }
        return false;
    }

    private static void TypeIntoSearch(string text)
    {
        var search = _shell.Search;
        search.text += text;
        search.ActivateInputField();
        search.caretPosition = search.text.Length;
        search.stringPosition = search.text.Length;
    }

    /// <summary>Shows a page by index (rail clicks, dev commands).</summary>
    public static void Show(int index)
    {
        if (_shell != null)
            _shell.Select(index);
    }

    public static int IndexOf(string id) => Pages.FindIndex(p => p.Id == id);

    public static void Toast(string text, bool ok = true)
    {
        if (Util.Perf.Enabled)
            Core.Log.Msg($"toast: {(ok ? "ok" : "FAIL")} {text}");
        _shell?.Toast(text, ok);
    }

    /// <summary>
    /// The game ignores its own keys while GameInput.IsTyping is set; keep it set exactly while the search box
    /// has focus. Esc while typing first leaves the box (the game's Esc handling is off while typing).
    /// </summary>
    private static readonly List<Il2CppTMPro.TMP_InputField> Fields = new();

    public static void RegisterField(Il2CppTMPro.TMP_InputField field) => Fields.Add(field);

    public static bool AnyFieldFocused()
    {
        for (int i = 0; i < Fields.Count; i++)
        {
            if (Fields[i] != null && Fields[i].isFocused)
                return true;
        }
        return false;
    }

    private static void UpdateTyping()
    {
        bool focused = AnyFieldFocused();
        if (focused && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            foreach (var field in Fields)
            {
                if (field != null && field.isFocused)
                    field.DeactivateInputField();
            }
            focused = false;
        }
        if (focused != _setTyping)
        {
            GameInput.IsTyping = focused;
            _setTyping = focused;
        }
    }

    private static void Animate()
    {
        float target = _open ? 1f : 0f;
        if (Mathf.Approximately(_fade, target))
            return;
        float step = Time.unscaledDeltaTime / (_open ? Theme.OpenTime : Theme.OpenTime * 0.6f);
        _fade = Mathf.MoveTowards(_fade, target, step);
        float e = Shell.Ease(_fade);
        _shell.Group.alpha = e;
        _shell.Window.anchoredPosition = new Vector2(0, -Theme.OpenRise * (1f - e));
        if (_fade <= 0f)
            _shell.Canvas.gameObject.SetActive(false);
    }
}
