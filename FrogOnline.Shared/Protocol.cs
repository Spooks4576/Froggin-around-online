namespace FrogOnline.Shared;

public static class Protocol
{
    public const ushort Version = 1;
    public const string ConnectKey = "frog-online-v1";
    public const int RoomCodeLength = 4;
}

public enum Envelope : byte
{
    Control = 0,
    Relay   = 1,
}

public enum ControlId : byte
{
    Hello       = 1,
    CreateRoom  = 2,
    JoinRoom    = 3,
    LeaveRoom   = 4,
    Ping        = 5,

    HelloAck    = 100,
    RoomCreated = 101,
    RoomJoined  = 102,
    RoomClosed  = 103,
    PeerReady   = 104,
    PeerLeft    = 105,
    Error       = 110,
    Pong        = 111,
}

public enum RoomRole : byte
{
    Host  = 0,
    Guest = 1,
}

public enum ErrorCode : byte
{
    Unknown          = 0,
    VersionMismatch  = 1,
    RoomNotFound     = 2,
    RoomFull         = 3,
    AlreadyInRoom    = 4,
    NotInRoom        = 5,
}
