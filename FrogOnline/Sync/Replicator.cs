using System.Linq;
using External_Packages.KinematicCharacterController.Core;
using External_Packages.KinematicCharacterController.Examples;
using FrogOnline.Net;
using FrogOnline.Shared;
using FrogOnline.UI;
using LiteNetLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Utilities;

namespace FrogOnline.Sync;

public sealed class Replicator
{
    public static Replicator Instance { get; private set; }
    public static void Ensure() { Instance ??= new Replicator(); }

    public RoomRole LocalRole { get; private set; }
    public bool IsHost => LocalRole == RoomRole.Host;
    public bool IsGuest => LocalRole == RoomRole.Guest;
    public bool Active { get; private set; }

    public ExamplePlayer P1 { get; private set; }
    public ExamplePlayer P2 { get; private set; }

    public ExamplePlayer LocalPlayer  => IsHost ? P1 : P2;
    public ExamplePlayer RemotePlayer => IsHost ? P2 : P1;

    public InputFrame LatestRemoteInput;
    public bool HasRemoteInput;

    public Snapshot LatestSnapshot;
    public bool HasSnapshot;
    public float SnapshotReceivedAt;

    private Snapshot _snap0, _snap1;
    private float _recv0, _recv1;
    private bool _has0, _has1;
    private const float InterpolationDelay = 0.10f;

    private uint _tick;
    private float _snapshotAccumulator;

    private Replicator()
    {
        NetClient.Instance.OnGamePacket += OnGame;
    }

    public void OnRoomReady(RoomRole role)
    {
        LocalRole = role;
        Active = true;
        _tick = 0;
        _snapshotAccumulator = 0;
        _has0 = _has1 = false;
        RefreshPlayerHandles();
        ApplyRoleSideEffects();
    }

    public void OnPeerLeft()
    {
        var wasActive = Active;
        Active = false;
        HasRemoteInput = false;
        HasSnapshot = false;
        _has0 = _has1 = false;
        RestoreMotors();

        if (wasActive)
        {
            var glm = Singleton<GameLogicManager>.Instance;
            if (glm != null)
            {
                FrogOnlinePlugin.Log.LogInfo("[online] peer left — returning to main menu");
                try { glm.ReturnToMainMenu(); }
                catch (System.Exception e) { FrogOnlinePlugin.Log.LogWarning($"ReturnToMainMenu failed: {e.Message}"); }
            }
            LobbyUI.Instance.Show();
        }
    }

    public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshPlayerHandles();
        ApplyRoleSideEffects();
    }

    public void FixedTick()
    {
        if (!Active) return;
        if (P1 == null || P2 == null) RefreshPlayerHandles();
        if (P1 == null || P2 == null) return;

        if (IsHost)
        {
            var rate = Mathf.Clamp(FrogOnlinePlugin.Instance.TickRate.Value, 10, 60);
            _snapshotAccumulator += Time.fixedDeltaTime;
            var interval = 1f / rate;
            if (_snapshotAccumulator >= interval)
            {
                _snapshotAccumulator = 0f;
                BroadcastSnapshot();
            }
        }
    }

    public void LateRenderTick()
    {
        if (!Active || !IsGuest) return;
        if (!_has1) return;
        var remote = RemotePlayer;
        if (remote == null) return;

        float renderTime = Time.time - InterpolationDelay;
        float t = _has0 && _recv1 > _recv0
            ? Mathf.Clamp01(Mathf.InverseLerp(_recv0, _recv1, renderTime))
            : 1f;

        var a = _has0 ? _snap0 : _snap1;
        var b = _snap1;

        var sa = a.P1;
        var sb = b.P1;

        var pos = Vector3.Lerp(Unpack(sa.Position),     Unpack(sb.Position),     t);
        var rot = Quaternion.Slerp(Unpack(sa.Rotation), Unpack(sb.Rotation),     t);
        var vel = Vector3.Lerp(Unpack(sa.BaseVelocity), Unpack(sb.BaseVelocity), t);

        var motor = remote.Character.Motor;
        var ks = new KinematicCharacterMotorState
        {
            Position = pos,
            Rotation = rot,
            BaseVelocity = vel,
            MustUnground = (sb.Flags & 1) != 0,
            LastMovementIterationFoundAnyGround = (sb.Flags & 2) != 0,
        };
        motor.ApplyState(ks, bypassInterpolation: true);

        var tm = remote.GetComponentInChildren<TongueManager>();
        if (tm != null && tm.TongueEndpoint != null && !IsZeroVec(sb.TongueEndpointPos))
        {
            var epPos = Vector3.Lerp(Unpack(sa.TongueEndpointPos), Unpack(sb.TongueEndpointPos), t);
            var epRot = Quaternion.Slerp(Unpack(sa.TongueEndpointRot), Unpack(sb.TongueEndpointRot), t);
            tm.TongueEndpoint.SetPositionAndRotation(epPos, epRot);
        }
    }

    private void BroadcastSnapshot()
    {
        _tick++;
        var snap = new Snapshot
        {
            Tick = _tick,
            ServerTime = Time.timeAsDouble,
            P1 = SampleState(P1),
            P2 = SampleState(P2),
        };
        NetClient.Instance.SendGame(GamePacketId.Snapshot, snap, DeliveryMethod.Unreliable);
    }

    private static PlayerState SampleState(ExamplePlayer p)
    {
        var motor = p.Character.Motor;
        var state = motor.GetState();
        var tm = p.GetComponentInChildren<TongueManager>();
        var tongueEp = tm?.TongueEndpoint;
        return new PlayerState
        {
            Position = Pack(state.Position),
            Rotation = Pack(state.Rotation),
            BaseVelocity = Pack(state.BaseVelocity),
            Flags = (byte)((state.MustUnground ? 1 : 0) | (state.LastMovementIterationFoundAnyGround ? 2 : 0)),
            TongueEndpointPos = tongueEp ? Pack(tongueEp.position) : default,
            TongueEndpointRot = tongueEp ? Pack(tongueEp.rotation) : default,
            TongueState = tm != null ? (byte)OrdinalOf(tm) : (byte)TongueFsm.Rest,
        };
    }

    private static TongueFsm OrdinalOf(TongueManager tm)
    {
        if (tm.IsInState(typeof(RestState)))     return TongueFsm.Rest;
        if (tm.IsInState(typeof(ExtendState)))   return TongueFsm.Extend;
        if (tm.IsInState(typeof(RetractState)))  return TongueFsm.Retract;
        if (tm.IsInState(typeof(AttachedState))) return TongueFsm.Attached;
        if (tm.IsInState(typeof(SlapState)))     return TongueFsm.Slap;
        if (tm.IsInState(typeof(KissState)))     return TongueFsm.Kiss;
        return TongueFsm.Rest;
    }

    private const float HardReconcileDistSq = 3f * 3f;

    public void ApplySnapshot(Snapshot s)
    {
        LatestSnapshot = s;
        HasSnapshot = true;
        SnapshotReceivedAt = Time.time;

        _snap0 = _snap1;
        _recv0 = _recv1;
        _has0  = _has1;
        _snap1 = s;
        _recv1 = Time.time;
        _has1  = true;

        if (IsGuest) ReconcileLocal(s);
    }

    private void ReconcileLocal(Snapshot s)
    {
        var lp = LocalPlayer;
        if (lp == null || lp.Character == null) return;
        var motor = lp.Character.Motor;
        if (motor == null) return;

        var srv = s.P2;
        var authPos = Unpack(srv.Position);
        if ((motor.TransientPosition - authPos).sqrMagnitude <= HardReconcileDistSq) return;

        var ks = new KinematicCharacterMotorState
        {
            Position = authPos,
            Rotation = Unpack(srv.Rotation),
            BaseVelocity = Unpack(srv.BaseVelocity),
            MustUnground = (srv.Flags & 1) != 0,
            LastMovementIterationFoundAnyGround = (srv.Flags & 2) != 0,
        };
        motor.ApplyState(ks, bypassInterpolation: true);
        FrogOnlinePlugin.Log.LogInfo(
            $"[reconcile] local frog snapped to authoritative position (drift >{Mathf.Sqrt(HardReconcileDistSq):F1}m).");
    }

    public void RefreshPlayerHandles()
    {
        var players = Object.FindObjectsByType<ExamplePlayer>(FindObjectsSortMode.None);
        P1 = players.FirstOrDefault(x => x.gameObject.layer == LayerMask.NameToLayer("Player 1"));
        P2 = players.FirstOrDefault(x => x.gameObject.layer == LayerMask.NameToLayer("Player 2"));
    }

    private void ApplyRoleSideEffects()
    {
        if (!Active) return;
        if (IsGuest)
        {
            var remote = RemotePlayer?.Character?.Motor;
            if (remote != null) KinematicCharacterSystem.UnregisterCharacterMotor(remote);

            var local = LocalPlayer?.Character?.Motor;
            if (local != null && !KinematicCharacterSystem.CharacterMotors.Contains(local))
                KinematicCharacterSystem.RegisterCharacterMotor(local);
        }
    }

    private void RestoreMotors()
    {
        if (P1?.Character?.Motor != null && !KinematicCharacterSystem.CharacterMotors.Contains(P1.Character.Motor))
            KinematicCharacterSystem.RegisterCharacterMotor(P1.Character.Motor);
        if (P2?.Character?.Motor != null && !KinematicCharacterSystem.CharacterMotors.Contains(P2.Character.Motor))
            KinematicCharacterSystem.RegisterCharacterMotor(P2.Character.Motor);
    }

    private void OnGame(GamePacketId id, LiteNetLib.NetPacketReader r)
    {
        switch (id)
        {
            case GamePacketId.ClientInput:
            {
                if (!IsHost) return;
                var f = new InputFrame();
                f.Deserialize(r);
                LatestRemoteInput = f;
                HasRemoteInput = true;
                break;
            }
            case GamePacketId.InputEdge:
            {
                if (!IsHost) return;
                var msg = new InputEdgeMsg();
                msg.Deserialize(r);
                Patches.EdgePatches.ApplyFromRemote((InputEdgeKind)msg.Kind);
                break;
            }
            case GamePacketId.Snapshot:
            {
                if (!IsGuest) return;
                var s = new Snapshot();
                s.Deserialize(r);
                ApplySnapshot(s);
                break;
            }
            case GamePacketId.TongueFsm:
            {
                var msg = new TongueFsmChange();
                msg.Deserialize(r);
                Patches.TonguePatches.ApplyRemote(msg);
                break;
            }
            case GamePacketId.SceneChange:
            {
                if (!IsGuest) return;
                var sc = new SceneChange();
                sc.Deserialize(r);
                Patches.GameLogicManagerPatches.ApplyFromRemote(sc);
                break;
            }
            case GamePacketId.AdvanceScene:
            {
                if (!IsGuest) return;
                Patches.GameLogicManagerPatches.ApplyAdvanceFromRemote();
                break;
            }
            case GamePacketId.PlayerJoinNotify:
            {
                Patches.PlayerInputDetectionPatches.ApplyRemoteJoin();
                break;
            }
            case GamePacketId.GameplayStart:
            {
                if (!IsGuest) return;
                Patches.PlayerInputDetectionPatches.ApplyRemoteSubmit();
                break;
            }
            case GamePacketId.CroakEvent:
            {
                var msg = new CroakMsg();
                msg.Deserialize(r);
                Patches.CroakPatches.ApplyFromRemote(msg);
                break;
            }
        }
    }

    private static bool IsZeroVec(Vec3 v) => v.X == 0 && v.Y == 0 && v.Z == 0;

    private static Vec3 Pack(Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };
    private static Quat Pack(Quaternion q) => new() { X = q.x, Y = q.y, Z = q.z, W = q.w };
    private static Vector3 Unpack(Vec3 v) => new(v.X, v.Y, v.Z);
    private static Quaternion Unpack(Quat q) => new(q.X, q.Y, q.Z, q.W);
}
