using LiteNetLib.Utils;

namespace FrogOnline.Shared;

public enum GamePacketId : byte
{
    ClientInput      = 10,
    InputEdge        = 11,
    Snapshot         = 20,
    TongueFsm        = 30,
    SceneChange      = 40,
    AdvanceScene     = 41,
    AnchorAttach     = 50,
    AnchorDetach     = 51,
    Croak            = 60,
    HostHello        = 70,
    GuestReady       = 71,
    PlayerJoinNotify = 80,
    GameplayStart    = 81,
    CroakEvent       = 82,
}

public enum InputEdgeKind : byte
{
    Grab   = 0,
    Crouch = 1,
    Croak  = 2,
}

public struct InputEdgeMsg : INetSerializable
{
    public byte Kind;

    public void Serialize(NetDataWriter w) => w.Put(Kind);
    public void Deserialize(NetDataReader r) => Kind = r.GetByte();
}

public struct Vec3 : INetSerializable
{
    public float X, Y, Z;
    public void Serialize(NetDataWriter w) { w.Put(X); w.Put(Y); w.Put(Z); }
    public void Deserialize(NetDataReader r) { X = r.GetFloat(); Y = r.GetFloat(); Z = r.GetFloat(); }
}

public struct Quat : INetSerializable
{
    public float X, Y, Z, W;
    public void Serialize(NetDataWriter w) { w.Put(X); w.Put(Y); w.Put(Z); w.Put(W); }
    public void Deserialize(NetDataReader r) { X = r.GetFloat(); Y = r.GetFloat(); Z = r.GetFloat(); W = r.GetFloat(); }
}

public struct InputFrame : INetSerializable
{
    public uint Tick;
    public float MoveX;
    public float MoveY;
    public Quat CameraRotation;
    public byte Buttons;

    public const byte BtnJump   = 1;
    public const byte BtnCrouch = 2;
    public const byte BtnGrab   = 4;
    public const byte BtnCroak  = 8;

    public void Serialize(NetDataWriter w)
    {
        w.Put(Tick); w.Put(MoveX); w.Put(MoveY);
        CameraRotation.Serialize(w);
        w.Put(Buttons);
    }

    public void Deserialize(NetDataReader r)
    {
        Tick = r.GetUInt(); MoveX = r.GetFloat(); MoveY = r.GetFloat();
        CameraRotation.Deserialize(r);
        Buttons = r.GetByte();
    }
}

public struct PlayerState : INetSerializable
{
    public Vec3 Position;
    public Quat Rotation;
    public Vec3 BaseVelocity;
    public byte Flags;
    public Vec3 TongueEndpointPos;
    public Quat TongueEndpointRot;
    public byte TongueState;

    public void Serialize(NetDataWriter w)
    {
        Position.Serialize(w);
        Rotation.Serialize(w);
        BaseVelocity.Serialize(w);
        w.Put(Flags);
        TongueEndpointPos.Serialize(w);
        TongueEndpointRot.Serialize(w);
        w.Put(TongueState);
    }

    public void Deserialize(NetDataReader r)
    {
        Position.Deserialize(r);
        Rotation.Deserialize(r);
        BaseVelocity.Deserialize(r);
        Flags = r.GetByte();
        TongueEndpointPos.Deserialize(r);
        TongueEndpointRot.Deserialize(r);
        TongueState = r.GetByte();
    }
}

public struct Snapshot : INetSerializable
{
    public uint Tick;
    public double ServerTime;
    public PlayerState P1;
    public PlayerState P2;

    public void Serialize(NetDataWriter w)
    {
        w.Put(Tick);
        w.Put(ServerTime);
        P1.Serialize(w);
        P2.Serialize(w);
    }

    public void Deserialize(NetDataReader r)
    {
        Tick = r.GetUInt();
        ServerTime = r.GetDouble();
        P1.Deserialize(r);
        P2.Deserialize(r);
    }
}

public struct SceneChange : INetSerializable
{
    public string Scene;
    public bool ResetPositions;
    public byte Mode;

    public void Serialize(NetDataWriter w)
    {
        w.Put(Scene ?? string.Empty);
        w.Put(ResetPositions);
        w.Put(Mode);
    }

    public void Deserialize(NetDataReader r)
    {
        Scene = r.GetString();
        ResetPositions = r.GetBool();
        Mode = r.GetByte();
    }
}

public enum TongueFsm : byte
{
    Rest    = 0,
    Extend  = 1,
    Retract = 2,
    Attached = 3,
    Slap    = 4,
    Kiss    = 5,
}

public struct TongueFsmChange : INetSerializable
{
    public byte Slot;
    public byte State;

    public void Serialize(NetDataWriter w) { w.Put(Slot); w.Put(State); }
    public void Deserialize(NetDataReader r) { Slot = r.GetByte(); State = r.GetByte(); }
}

public struct CroakMsg : INetSerializable
{
    public byte Slot;

    public void Serialize(NetDataWriter w) => w.Put(Slot);
    public void Deserialize(NetDataReader r) => Slot = r.GetByte();
}
