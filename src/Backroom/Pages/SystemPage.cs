using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using Console = Il2CppScheduleOne.Console;

namespace PocketPlug.Backroom.Pages;

/// <summary>
/// Saving, HUD and debug switches, the full console (every command the game has, with a command line), and a
/// danger zone where everything is held to confirm.
/// </summary>
internal sealed class SystemPage : ListPage
{
    public override string Id => "system";
    public override string Title => "System";
    public override string Icon => "terminal";

    // The console's debug switches don't expose their state; Backroom tracks what it set (the game starts all on).
    private static bool _physics = true, _culling = true, _foliage = true, _instancing = true;

    private TMP_InputField _command;
    private (UnityEngine.UI.Button button, TextMeshProUGUI label) _run;
    private bool _consoleView;

    protected override void Define()
    {
        _consoleView = false;
        Section("Game");
        Action("Save game", "Saves now, exactly like sleeping or the pause menu's Save.", () =>
        {
            if (PersistentSingleton<SaveManager>.Instance.IsSaving)
            {
                Fail("Already saving");
                return;
            }
            PersistentSingleton<SaveManager>.Instance.Save();
            Done("Game saved");
        }, "save");
        Toggle("Show HUD", "The game's on-screen HUD (hotbar, money, compass).",
            () => Singleton<HUD>.InstanceExists && Singleton<HUD>.Instance.canvas.enabled,
            on => Singleton<HUD>.Instance.canvas.enabled = on, "eye");
        Toggle("FPS counter", "The game's frame-rate counter.",
            () => Singleton<HUD>.InstanceExists && Singleton<HUD>.Instance.fpsLabel.gameObject.activeSelf,
            on => Singleton<HUD>.Instance.fpsLabel.gameObject.SetActive(on), "gauge");

        Section("Console");
        Link("All console commands", "Every command the game's console has, with what it does. Run any of them from here.", _ => DefineConsole(), "terminal",
            () => $"{Console.Commands?.Count ?? 0} commands");
        Action("Clear key binds", "Removes every key bound with the console's bind command.", () => Run("clearbinds"));

        Section("Debug");
        Toggle("Physics", "Runs the physics simulation. Off freezes everything that moves physically.", () => _physics, on =>
        {
            Run(on ? "enablephysics" : "disablephysics");
            _physics = on;
        });
        Toggle("Occlusion culling", "Skips drawing what's hidden behind walls. Off can lower FPS.", () => _culling, on =>
        {
            Run(on ? "enableocclusionculling" : "disableocclusionculling");
            _culling = on;
        });
        Toggle("Trees and foliage", "Draws trees and grass.", () => _foliage, on =>
        {
            Run(on ? "enableterrain" : "disableterrain");
            _foliage = on;
        });
        Toggle("GPU instancing", "Batches repeated objects for speed.", () => _instancing, on =>
        {
            Run(on ? "enableinstancing" : "disableinstancing");
            _instancing = on;
        });

        Section("Danger zone");
        Action("Clear inventory", "Deletes everything in your inventory. Can't be undone.", () =>
        {
            PlayerSingleton<PlayerInventory>.Instance.ClearInventory();
            Done("Inventory cleared");
        }, "trash-2", hold: true);
        Action("Disable all NPCs", "Hides every NPC until you reload the save.", () => Run("disablenpcs"), "skull", hold: true);
        Action("Destroy all NPCs", "Deletes every NPC, including your employees and dealers. Don't save after this.", () => Run("destroynpcs"), "skull", hold: true);
        Action("Quit without saving", "Closes the game immediately. Anything since your last save is lost.", Application.Quit, "x", hold: true);
    }

    private void DefineConsole()
    {
        _consoleView = true;
        Section("Commands");
        var commands = new List<Console.ConsoleCommand>();
        foreach (var c in Console.Commands)
        {
            if (c != null)
                commands.Add(c);
        }
        commands.Sort((a, b) => string.Compare(a.CommandWord, b.CommandWord, StringComparison.Ordinal));
        foreach (var c in commands)
        {
            string word = c.CommandWord, example = c.ExampleUsage, description = c.CommandDescription;
            Action(word, $"{description}\n\nExample: <color=#E9EBEE>{example}</color>", () => Fill(example, word), "terminal");
        }
    }

    /// <summary>Puts a command into the command line (the example, so its arguments are shown).</summary>
    private void Fill(string example, string word)
    {
        if (_command == null)
            return;
        _command.text = string.IsNullOrEmpty(example) ? word + " " : example;
        _command.ActivateInputField();
        _command.caretPosition = _command.text.Length;
    }

    protected override void OnInfo(Row row)
    {
        if (_command == null)
        {
            var label = Controls.Section(InfoExtra, "Command line");
            K.At(label.rectTransform, 0, 0, 372, Theme.Line(Theme.Caption), 0, 104);
            _command = Widgets.TextField(InfoExtra, "Type a command, e.g. give ogkush 10", "terminal");
            K.At(_command.GetComponent<RectTransform>(), 0, 0, 372, 44, 0, 56);
            _command.onSubmit.AddListener((Action<string>)(_ => RunLine()));
            _run = Controls.Primary(InfoExtra, "Run command", RunLine, 44);
            K.At(_run.button.GetComponent<RectTransform>(), 0, 0, 372, 44, 0, 0);
        }
        InfoExtra.gameObject.SetActive(_consoleView);
    }

    private void RunLine()
    {
        if (_command == null || string.IsNullOrWhiteSpace(_command.text) || !K.Commit())
            return;
        string line = _command.text.Trim();
        Run(line);
        _command.text = "";
    }

    private static string _captured;
    private static bool _capturing;
    private static Application.LogCallback _logHook;

    /// <summary>Runs a console command and shows what the game logged in reply (the console only logs).</summary>
    private static void Run(string line)
    {
        if (_logHook == null)
        {
            _logHook = DelegateSupport.ConvertDelegate<Application.LogCallback>(new Action<string, string, LogType>((message, _, type) =>
            {
                if (_capturing && type != LogType.Exception)
                    _captured = message;
            }));
            Application.add_logMessageReceived(_logHook);
        }
        _captured = null;
        _capturing = true;
        try
        {
            Console.SubmitCommand(line);
        }
        finally
        {
            _capturing = false;
        }
        string reply = _captured;
        if (!string.IsNullOrEmpty(reply))
        {
            reply = reply.Split('\n')[0];
            if (reply.Length > 70)
                reply = reply.Substring(0, 67) + "…";
        }
        Core.Log.Msg($"Backroom console: {line} → {reply}");
        Done(string.IsNullOrEmpty(reply) ? $"Ran: {line}" : reply);
    }
}
