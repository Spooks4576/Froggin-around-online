using External_Packages.KinematicCharacterController.ExampleCharacter.Scripts;
using External_Packages.KinematicCharacterController.Examples;
using FrogOnline.Net;
using FrogOnline.Shared;
using FrogOnline.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine.InputSystem;

namespace FrogOnline.Patches;

[HarmonyPatch(typeof(ExamplePlayer))]
internal static class EdgePatches
{
    [HarmonyPrefix]
    [HarmonyPatch("HandleGrabInput")]
    private static bool HandleGrabInput_Prefix(ExamplePlayer __instance, InputAction.CallbackContext context)
        => ForwardOrRun(__instance, context, InputEdgeKind.Grab);

    [HarmonyPrefix]
    [HarmonyPatch("HandleCrouchInput")]
    private static bool HandleCrouchInput_Prefix(ExamplePlayer __instance, InputAction.CallbackContext context)
        => ForwardOrRun(__instance, context, InputEdgeKind.Crouch);

    private static bool ForwardOrRun(ExamplePlayer player, InputAction.CallbackContext ctx, InputEdgeKind kind)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return true;

        if (!ctx.started) return true;

        if (repl.IsGuest && player == repl.LocalPlayer)
        {
            NetClient.Instance.SendGame(
                GamePacketId.InputEdge,
                new InputEdgeMsg { Kind = (byte)kind },
                DeliveryMethod.ReliableOrdered);
            return false;
        }

        if (repl.IsHost && player == repl.RemotePlayer)
        {
            return false;
        }

        return true;
    }

    public static void ApplyFromRemote(InputEdgeKind kind)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.IsHost) return;
        var target = repl.RemotePlayer;
        if (target == null) return;

        switch (kind)
        {
            case InputEdgeKind.Grab:
            {
                var tm = target.GetComponentInChildren<TongueManager>();
                if (tm == null) return;
                if (tm.IsInState(typeof(RestState)))
                    tm.TransitionTo(typeof(ExtendState));
                else if (tm.IsInState(typeof(AttachedState)))
                    tm.TransitionTo(typeof(RetractState));
                break;
            }
            case InputEdgeKind.Crouch:
            {
                var trav = Traverse.Create(target).Field("_characterInputs");
                var ci = trav.GetValue<PlayerCharacterInputs>();
                ci.ToggleCrouch = !ci.ToggleCrouch;
                trav.SetValue(ci);
                break;
            }
            case InputEdgeKind.Croak:
                break;
        }
    }
}
