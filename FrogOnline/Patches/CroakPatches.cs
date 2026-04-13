using External_Packages.KinematicCharacterController.Examples;
using FrogOnline.Net;
using FrogOnline.Shared;
using FrogOnline.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine.InputSystem;

namespace FrogOnline.Patches;

[HarmonyPatch(typeof(ExamplePlayer))]
internal static class CroakPatches
{
    public static bool IsApplyingRemote;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(ExamplePlayer.CroakOnDemand))]
    private static void CroakOnDemand_Postfix(ExamplePlayer __instance) => Broadcast(__instance);

    [HarmonyPostfix]
    [HarmonyPatch("HandleCroakInput")]
    private static void HandleCroakInput_Postfix(ExamplePlayer __instance, InputAction.CallbackContext context)
    {
        if (!context.started) return;
        Broadcast(__instance);
    }

    private static void Broadcast(ExamplePlayer p)
    {
        if (IsApplyingRemote) return;

        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return;

        byte slot;
        if (p == repl.P1) slot = 0;
        else if (p == repl.P2) slot = 1;
        else return;

        NetClient.Instance.SendGame(
            GamePacketId.CroakEvent,
            new CroakMsg { Slot = slot },
            DeliveryMethod.ReliableOrdered);
    }

    public static void ApplyFromRemote(CroakMsg msg)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return;
        var target = msg.Slot == 0 ? repl.P1 : repl.P2;
        if (target == null) return;

        IsApplyingRemote = true;
        try { target.CroakOnDemand(); }
        finally { IsApplyingRemote = false; }
    }
}
