using System.Linq;
using External_Packages.KinematicCharacterController.Examples;
using FrogOnline.Net;
using FrogOnline.Shared;
using FrogOnline.Sync;
using HarmonyLib;
using InputManagement;
using LiteNetLib;
using UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using Utilities;

namespace FrogOnline.Patches;

[HarmonyPatch(typeof(PlayerInputDetection))]
internal static class PlayerInputDetectionPatches
{
    public static bool IsApplyingRemoteSubmit;

    [HarmonyPrefix]
    [HarmonyPatch("HandleJoinInput")]
    private static bool HandleJoinInput_Prefix(PlayerInputDetection __instance, InputDevice device)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return true;

        var slotProp = repl.IsHost ? "_p1Device" : "_p2Device";
        var current = Traverse.Create(__instance).Property(slotProp).GetValue<InputDevice>();
        if (current != null) return false;

        FillSlot(__instance, slotProp, device, repl.IsHost ? 1 : 2);

        NetClient.Instance.SendGameRaw(
            GamePacketId.PlayerJoinNotify,
            DeliveryMethod.ReliableOrdered,
            _ => { });

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(PlayerInputDetection.HandleSubmitInput))]
    private static bool HandleSubmitInput_Prefix(PlayerInputDetection __instance)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return true;
        if (IsApplyingRemoteSubmit) return true;

        if (!repl.IsHost) return false;

        NetClient.Instance.SendGameRaw(
            GamePacketId.GameplayStart,
            DeliveryMethod.ReliableOrdered,
            _ => { });
        return true;
    }

    public static void ApplyRemoteJoin()
    {
        var pid = Singleton<PlayerInputDetection>.Instance;
        if (pid == null) return;
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return;

        var slotProp = repl.IsHost ? "_p2Device" : "_p1Device";
        var current = Traverse.Create(pid).Property(slotProp).GetValue<InputDevice>();
        if (current != null) return;

        InputDevice placeholder = Keyboard.current
            ?? (InputDevice)Gamepad.all.FirstOrDefault()
            ?? Mouse.current;
        if (placeholder == null)
        {
            FrogOnlinePlugin.Log.LogWarning("No InputDevice available to represent remote join.");
            return;
        }
        FillSlot(pid, slotProp, placeholder, repl.IsHost ? 2 : 1);
    }

    public static void ApplyRemoteSubmit()
    {
        var pid = Singleton<PlayerInputDetection>.Instance;
        if (pid == null) return;
        IsApplyingRemoteSubmit = true;
        try { pid.HandleSubmitInput(); }
        finally { IsApplyingRemoteSubmit = false; }
    }

    private static void FillSlot(PlayerInputDetection pid, string slotProp, InputDevice device, int playerNumber)
    {
        Traverse.Create(pid).Property(slotProp).SetValue(device);

        var ev = Traverse.Create(pid).Field("PlayerJoined").GetValue<UnityEvent<InputDevice>>();
        ev?.Invoke(device);

        var layerName = playerNumber == 1 ? "Player 1" : "Player 2";
        Object.FindObjectsByType<ExamplePlayer>(FindObjectsSortMode.None)
            .FirstOrDefault(x => x.gameObject.layer == LayerMask.NameToLayer(layerName))
            ?.CroakOnDemand();
    }
}

[HarmonyPatch(typeof(MultiplayerInputManager))]
internal static class MultiplayerInputManagerPatches
{
    [HarmonyPrefix]
    [HarmonyPatch("SetPlayerDevices")]
    private static bool SetPlayerDevices_Prefix(
        MultiplayerInputManager __instance, InputDevice p1Device, InputDevice p2Device)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return true;

        var trav = Traverse.Create(__instance);
        var p1 = trav.Field<ExamplePlayer>("_player1").Value;
        var p2 = trav.Field<ExamplePlayer>("_player2").Value;
        if (p1 == null || p2 == null) return true;

        var local = repl.IsHost ? p1Device : p2Device;
        if (local == null) local = p1Device ?? p2Device;

        var p1Controls = new global::PlayerInput();
        var p2Controls = new global::PlayerInput();

        if (repl.IsHost)
        {
            InputUser.PerformPairingWithDevice(local).AssociateActionsWithUser(p1Controls);
            p1Controls.Enable();
            p1.SetPlayerInput(p1Controls);
            p2.SetPlayerInput(p2Controls);
        }
        else
        {
            InputUser.PerformPairingWithDevice(local).AssociateActionsWithUser(p2Controls);
            p2Controls.Enable();
            p2.SetPlayerInput(p2Controls);
            p1.SetPlayerInput(p1Controls);
        }

        trav.Field("_player1Controls").SetValue(p1Controls);
        trav.Field("_player2Controls").SetValue(p2Controls);
        trav.Property("hasSetControls").SetValue(true);

        FrogOnlinePlugin.Log.LogInfo(
            $"[online] paired {local?.name} → {(repl.IsHost ? "P1 (host-local)" : "P2 (guest-local)")}");

        if (UI.LobbyUI.Instance.Visible)
            UI.LobbyUI.Instance.Toggle();
        return false;
    }
}
