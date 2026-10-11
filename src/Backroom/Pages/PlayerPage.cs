using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.PlayerScripts.Health;
using PocketPlug.Backroom.Kit;
using UnityEngine;
using Console = Il2CppScheduleOne.Console;
using EPursuitLevel = Il2CppScheduleOne.PlayerScripts.PlayerCrimeData.EPursuitLevel;

namespace PocketPlug.Backroom.Pages;

/// <summary>Health, movement, wanted level, rank and emotions for your own player.</summary>
internal sealed class PlayerPage : ListPage
{
    public override string Id => "player";
    public override string Title => "Player";
    public override string Icon => "user";

    private const string EmotionLabel = "backroom";
    private int _emotion = -1;

    private static Player Me => Player.Local;
    private static PlayerHealth Health => Me != null ? Me.Health : null;
    private static PlayerMovement Movement => PlayerSingleton<PlayerMovement>.InstanceExists ? PlayerSingleton<PlayerMovement>.Instance : null;

    protected override void Define()
    {
        Section("Health");
        Readout("Health", "Your current health.", () => Health != null ? $"{Health.CurrentHealth:0} / {PlayerHealth.MaxHealth:0}" : "—", "heart");
        Action("Heal to full", "Restores your health to the maximum.", () =>
        {
            if (Health == null || !Health.IsAlive)
            {
                Fail("Can't heal right now");
                return;
            }
            Health.SetHealth(PlayerHealth.MaxHealth);
            Done("Healed to full");
        });
        Stepper("Set health", "Sets your health directly.", () => Health?.CurrentHealth ?? 0f, v =>
        {
            if (Health != null && Health.IsAlive)
                Health.SetHealth(Mathf.Max(1f, v));
        }, 1f, PlayerHealth.MaxHealth, 5f, v => $"{v:0}");

        Section("Movement");
        Stepper("Move speed", "Multiplies how fast you walk and run (game: 1×).", () => PlayerMovement.StaticMoveSpeedMultiplier,
            v => PlayerMovement.StaticMoveSpeedMultiplier = v, 0.1f, 5f, 0.1f, v => $"{v:0.0}×");
        Stepper("Jump height", "Multiplies your jump (game: 1×).", () => PlayerMovement.JumpMultiplier,
            v => PlayerMovement.JumpMultiplier = v, 0.1f, 5f, 0.1f, v => $"{v:0.0}×");
        Stepper("Stamina", "Maximum stamina (game: 100). Refills to the new maximum.", () => PlayerMovement.StaminaReserveMax, v =>
        {
            PlayerMovement.StaminaReserveMax = v;
            Movement?.SetStamina(v, true);
        }, 10f, 2000f, 10f, v => $"{v:0}");
        Toggle("Free camera", "Detaches the camera so you can fly around (the console's freecam).",
            () => PlayerSingleton<PlayerCamera>.InstanceExists && PlayerSingleton<PlayerCamera>.Instance.FreeCamEnabled,
            on => PlayerSingleton<PlayerCamera>.Instance.SetFreeCam(on, true), "eye");
        Action("Reset movement", "Puts speed, jump and stamina back to the game's values.", () =>
        {
            PlayerMovement.StaticMoveSpeedMultiplier = 1f;
            PlayerMovement.JumpMultiplier = 1f;
            PlayerMovement.StaminaReserveMax = 100f;
            Movement?.SetStamina(100f, true);
            Done("Movement reset");
        }, "refresh-cw");

        Section("Wanted level");
        Readout("Wanted level", "How hard the police are after you.", () => Me != null ? K.Pretty(Me.CrimeData.CurrentPursuitLevel.ToString()) : "—", "shield");
        Action("Raise wanted level", "Calls the police on you, or escalates an existing pursuit.", () =>
        {
            Console.SubmitCommand("raisewanted");
            Done("Wanted level raised");
        });
        Action("Lower wanted level", "Drops the pursuit by one level.", () =>
        {
            Me?.CrimeData.Deescalate();
            Done("Wanted level lowered");
        });
        Action("Clear wanted level", "Ends any pursuit and forgets your crimes.", () =>
        {
            if (Me == null)
                return;
            Me.CrimeData.SetPursuitLevel(EPursuitLevel.None);
            Me.CrimeData.ClearCrimes();
            Done("Wanted level cleared");
        });
        Toggle("Police ignore you", "Officers don't react to you, including ones that show up later. Off when you load a save.",
            () => Cheats.PoliceIgnore, Cheats.SetPoliceIgnore, "shield");

        Section("Rank");
        Readout("Rank", "Your current rank and XP.", RankText, "trophy");
        foreach (int xp in new[] { 100, 1_000, 10_000 })
        {
            int amount = xp;
            Action($"Add {amount:N0} XP", $"Gives you {amount:N0} XP toward your next rank.", () =>
            {
                NetworkSingleton<LevelManager>.Instance.AddXP(amount);
                Done($"Added {amount:N0} XP");
            });
        }

        Section("Emotion");
        Choice("Facial expression", "Overrides your character's face. Choose None to go back to normal.", EmotionNames, () => _emotion + 1, i =>
        {
            var emotions = Me?.Avatar?.EmotionManager;
            if (emotions == null)
                return;
            emotions.RemoveEmotionOverride(EmotionLabel);
            _emotion = i - 1;
            if (_emotion >= 0)
                emotions.AddEmotionOverride(EmotionNames()[i], EmotionLabel, 0f, 10);
        }, "sparkles");
    }

    private static string RankText()
    {
        if (!NetworkSingleton<LevelManager>.InstanceExists)
            return "—";
        var lm = NetworkSingleton<LevelManager>.Instance;
        return $"{K.Pretty(lm.Rank.ToString())} {Roman(lm.Tier)}   ·   {lm.XP:N0} XP";
    }

    private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString() };

    private static string[] _emotionNames;

    private static string[] EmotionNames()
    {
        if (_emotionNames != null)
            return _emotionNames;
        var list = new List<string> { "None" };
        var presets = Me?.Avatar?.EmotionManager?.EmotionPresetList;
        if (presets != null)
        {
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i] != null && !string.IsNullOrEmpty(presets[i].PresetName))
                    list.Add(presets[i].PresetName);
            }
        }
        if (list.Count > 1)
            _emotionNames = list.ToArray();
        return list.ToArray();
    }
}
