using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;

namespace PocketPlug.Features;

/// <summary>Keeps stamina full while riding a skateboard, so pushing never runs out.</summary>
internal static class SkateStamina
{
    public static void Update()
    {
        if (!Config.SkateStamina.On || Player.Local == null || !Player.Local.IsSkating)
            return;
        if (!PlayerSingleton<PlayerMovement>.InstanceExists)
            return;

        var movement = PlayerSingleton<PlayerMovement>.Instance;
        if (movement.CurrentStaminaReserve < PlayerMovement.StaminaReserveMax)
            movement.SetStamina(PlayerMovement.StaminaReserveMax, false);
    }
}
