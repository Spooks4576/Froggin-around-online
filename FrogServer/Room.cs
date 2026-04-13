using FrogOnline.Shared;
using LiteNetLib;

namespace FrogServer;

internal sealed class Room
{
    public string Code { get; }
    public NetPeer Host { get; private set; }
    public NetPeer? Guest { get; private set; }
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    public bool Full => Guest != null;

    public Room(string code, NetPeer host)
    {
        Code = code;
        Host = host;
    }

    public void SetGuest(NetPeer guest) => Guest = guest;

    public NetPeer? Other(NetPeer peer)
    {
        if (peer == Host) return Guest;
        if (peer == Guest) return Host;
        return null;
    }

    public RoomRole RoleOf(NetPeer peer)
    {
        if (peer == Host) return RoomRole.Host;
        return RoomRole.Guest;
    }

    public void Remove(NetPeer peer)
    {
        if (peer == Host) Host = null!;
        else if (peer == Guest) Guest = null;
    }

    public bool IsEmpty => Host == null && Guest == null;
}
