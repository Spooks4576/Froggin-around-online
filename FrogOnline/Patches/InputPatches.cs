using External_Packages.KinematicCharacterController.ExampleCharacter.Scripts;
using FrogOnline.Net;
using FrogOnline.Shared;
using FrogOnline.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace FrogOnline.Patches;

[HarmonyPatch(typeof(ExampleCharacterController))]
internal static class CharacterControllerPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ExampleCharacterController.SetInputs),
        new[] { typeof(PlayerCharacterInputs) },
        new[] { ArgumentType.Ref })]
    private static void SetInputs_Prefix(ExampleCharacterController __instance, ref PlayerCharacterInputs inputs)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return;

        var localChar  = repl.LocalPlayer?.Character;
        var remoteChar = repl.RemotePlayer?.Character;

        if (__instance == remoteChar)
        {
            if (repl.IsHost && repl.HasRemoteInput)
            {
                var f = repl.LatestRemoteInput;
                inputs.MoveAxisForward = f.MoveY;
                inputs.MoveAxisRight   = f.MoveX;
                inputs.CameraRotation  = new Quaternion(
                    f.CameraRotation.X, f.CameraRotation.Y,
                    f.CameraRotation.Z, f.CameraRotation.W);
                inputs.JumpDown = (f.Buttons & InputFrame.BtnJump) != 0;
            }
            else
            {
                inputs.MoveAxisForward = 0;
                inputs.MoveAxisRight   = 0;
                inputs.JumpDown        = false;
            }
            return;
        }

        if (__instance == localChar && repl.IsGuest)
        {
            var f = new InputFrame
            {
                MoveX = inputs.MoveAxisRight,
                MoveY = inputs.MoveAxisForward,
                CameraRotation = new Quat
                {
                    X = inputs.CameraRotation.x, Y = inputs.CameraRotation.y,
                    Z = inputs.CameraRotation.z, W = inputs.CameraRotation.w,
                },
                Buttons = (byte)(inputs.JumpDown ? InputFrame.BtnJump : 0),
            };
            NetClient.Instance.SendGame(GamePacketId.ClientInput, f, DeliveryMethod.Unreliable);
        }
    }
}
