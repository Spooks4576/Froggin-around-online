using System;
using System.Collections.Generic;
using External_Packages.KinematicCharacterController.Examples;
using FrogOnline.Net;
using FrogOnline.Shared;
using FrogOnline.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine;

namespace FrogOnline.Patches;

[HarmonyPatch(typeof(TongueManager))]
internal static class TonguePatches
{
    public static bool IsApplyingRemote;

    private const float StuckTimeoutSeconds = 6f;
    private static readonly Dictionary<TongueManager, float> _stateEnteredAt = new();

    [HarmonyPrefix]
    [HarmonyPatch("TransitionTo")]
    private static bool TransitionTo_Prefix()
    {
        if (IsApplyingRemote) return true;
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return true;
        return !repl.IsGuest;
    }

    [HarmonyPostfix]
    [HarmonyPatch("TransitionTo")]
    private static void TransitionTo_Postfix(TongueManager __instance, Type newState)
    {
        _stateEnteredAt[__instance] = Time.time;

        if (IsApplyingRemote) return;
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return;
        if (repl.IsGuest) return;

        byte slot;
        if (IsTongueOf(__instance, repl.P1)) slot = 0;
        else if (IsTongueOf(__instance, repl.P2)) slot = 1;
        else return;

        var ord = OrdinalFromState(newState);
        if (ord == null) return;

        NetClient.Instance.SendGame(
            GamePacketId.TongueFsm,
            new TongueFsmChange { Slot = slot, State = (byte)ord.Value },
            DeliveryMethod.ReliableOrdered);
    }

    public static void Tick()
    {
        var repl = Replicator.Instance;
        if (repl != null && repl.Active && repl.IsGuest) return;

        List<TongueManager> stuck = null;
        foreach (var kv in _stateEnteredAt)
        {
            var tm = kv.Key;
            if (tm == null) continue;
            if (tm.IsInState(typeof(RestState))) continue;
            if (Time.time - kv.Value < StuckTimeoutSeconds) continue;
            stuck ??= new List<TongueManager>();
            stuck.Add(tm);
        }
        if (stuck == null) return;

        foreach (var tm in stuck)
        {
            FrogOnlinePlugin.Log.LogInfo($"[tongue-watchdog] forcing reset (stuck >{StuckTimeoutSeconds:F0}s)");
            try { tm.TransitionTo(typeof(RestState)); }
            catch (Exception e) { FrogOnlinePlugin.Log.LogWarning($"watchdog reset failed: {e.Message}"); }
        }
    }

    public static void ApplyRemote(TongueFsmChange msg)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return;

        var player = msg.Slot == 0 ? repl.P1 : repl.P2;
        if (player == null) return;
        var tm = player.GetComponentInChildren<TongueManager>();
        if (tm == null) return;

        var target = StateFromOrdinal((TongueFsm)msg.State);
        if (target == null) return;
        if (tm.IsInState(target)) return;

        IsApplyingRemote = true;
        try { tm.TransitionTo(target); }
        catch (Exception e) { FrogOnlinePlugin.Log.LogWarning($"TongueFsm apply failed: {e.Message}"); }
        finally { IsApplyingRemote = false; }
    }

    private static bool IsTongueOf(TongueManager tm, ExamplePlayer p)
    {
        if (p == null) return false;
        return tm.GetComponentInParent<ExamplePlayer>() == p;
    }

    private static TongueFsm? OrdinalFromState(Type t)
    {
        if (t == typeof(RestState))     return TongueFsm.Rest;
        if (t == typeof(ExtendState))   return TongueFsm.Extend;
        if (t == typeof(RetractState))  return TongueFsm.Retract;
        if (t == typeof(AttachedState)) return TongueFsm.Attached;
        if (t == typeof(SlapState))     return TongueFsm.Slap;
        if (t == typeof(KissState))     return TongueFsm.Kiss;
        return null;
    }

    private static Type StateFromOrdinal(TongueFsm f) => f switch
    {
        TongueFsm.Rest     => typeof(RestState),
        TongueFsm.Extend   => typeof(ExtendState),
        TongueFsm.Retract  => typeof(RetractState),
        TongueFsm.Attached => typeof(AttachedState),
        TongueFsm.Slap     => typeof(SlapState),
        TongueFsm.Kiss     => typeof(KissState),
        _ => null,
    };
}
