using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using FrogOnline.Shared;
using LiteNetLib;
using LiteNetLib.Utils;

namespace FrogServer;

internal sealed class RelayServer : INetEventListener
{
    private readonly ushort _port;
    private readonly int _maxRooms;
    private readonly NetManager _net;
    private readonly NetDataWriter _w = new();

    private readonly Dictionary<string, Room> _roomsByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<NetPeer, Room> _roomByPeer = new();

    public RelayServer(ushort port, int maxRooms)
    {
        _port = port;
        _maxRooms = maxRooms;
        _net = new NetManager(this)
        {
            AutoRecycle = true,
            UnconnectedMessagesEnabled = false,
            PingInterval = 1000,
            DisconnectTimeout = 10_000,
        };
    }

    public void Run()
    {
        if (!_net.Start(_port))
        {
            Console.Error.WriteLine($"Failed to bind UDP {_port}.");
            return;
        }
        Log($"FrogServer listening on UDP {_port}");

        var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };

        var lastStats = DateTime.UtcNow;
        while (!stop.IsSet)
        {
            _net.PollEvents();
            Thread.Sleep(10);

            if ((DateTime.UtcNow - lastStats).TotalSeconds >= 30)
            {
                Log($"peers={_net.ConnectedPeersCount} rooms={_roomsByCode.Count}");
                lastStats = DateTime.UtcNow;
            }
        }

        _net.Stop();
        Log("Stopped.");
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey(Protocol.ConnectKey);
    }

    public void OnPeerConnected(NetPeer peer)
    {
        Log($"+peer {peer.EndPoint} (id={peer.Id})");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Log($"-peer {peer.EndPoint} ({disconnectInfo.Reason})");
        if (_roomByPeer.TryGetValue(peer, out var room))
        {
            HandlePeerLeftRoom(peer, room);
        }
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (reader.AvailableBytes == 0) return;
        var envelope = (Envelope)reader.GetByte();
        switch (envelope)
        {
            case Envelope.Control:
                HandleControl(peer, reader);
                break;

            case Envelope.Relay:
                if (_roomByPeer.TryGetValue(peer, out var room))
                {
                    var other = room.Other(peer);
                    if (other != null && other.ConnectionState == ConnectionState.Connected)
                    {
                        _w.Reset();
                        _w.Put((byte)Envelope.Relay);
                        _w.Put(reader.GetRemainingBytes());
                        other.Send(_w, method);
                    }
                }
                break;
        }
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

    private void HandleControl(NetPeer peer, NetPacketReader reader)
    {
        if (reader.AvailableBytes == 0) return;
        var id = (ControlId)reader.GetByte();
        switch (id)
        {
            case ControlId.Hello:
            {
                var clientVersion = reader.GetUShort();
                if (clientVersion != Protocol.Version)
                {
                    SendError(peer, ErrorCode.VersionMismatch);
                    peer.Disconnect();
                    return;
                }
                SendControl(peer, ControlId.HelloAck, w => w.Put(Protocol.Version));
                break;
            }

            case ControlId.CreateRoom:
            {
                if (_roomByPeer.ContainsKey(peer)) { SendError(peer, ErrorCode.AlreadyInRoom); return; }
                if (_roomsByCode.Count >= _maxRooms) { SendError(peer, ErrorCode.Unknown); return; }

                var code = GenerateRoomCode();
                var room = new Room(code, peer);
                _roomsByCode[code] = room;
                _roomByPeer[peer] = room;

                SendControl(peer, ControlId.RoomCreated, w => { w.Put(code); w.Put((byte)RoomRole.Host); });
                Log($"room+ {code} host={peer.EndPoint}");
                break;
            }

            case ControlId.JoinRoom:
            {
                var code = reader.GetString(32);
                if (_roomByPeer.ContainsKey(peer)) { SendError(peer, ErrorCode.AlreadyInRoom); return; }
                if (!_roomsByCode.TryGetValue(code, out var room)) { SendError(peer, ErrorCode.RoomNotFound); return; }
                if (room.Full) { SendError(peer, ErrorCode.RoomFull); return; }

                room.SetGuest(peer);
                _roomByPeer[peer] = room;

                SendControl(peer, ControlId.RoomJoined, w => { w.Put(room.Code); w.Put((byte)RoomRole.Guest); });
                SendControl(room.Host, ControlId.PeerReady, w => w.Put((byte)RoomRole.Guest));
                SendControl(peer, ControlId.PeerReady, w => w.Put((byte)RoomRole.Host));
                Log($"room= {room.Code} guest={peer.EndPoint}");
                break;
            }

            case ControlId.LeaveRoom:
            {
                if (_roomByPeer.TryGetValue(peer, out var room))
                    HandlePeerLeftRoom(peer, room);
                else
                    SendError(peer, ErrorCode.NotInRoom);
                break;
            }

            case ControlId.Ping:
            {
                var stamp = reader.GetLong();
                SendControl(peer, ControlId.Pong, w => w.Put(stamp));
                break;
            }
        }
    }

    private void HandlePeerLeftRoom(NetPeer peer, Room room)
    {
        room.Remove(peer);
        _roomByPeer.Remove(peer);
        var other = peer == room.Host ? room.Guest : room.Host;
        if (other != null)
        {
            SendControl(other, ControlId.PeerLeft, _ => { });
        }
        if (room.IsEmpty)
        {
            _roomsByCode.Remove(room.Code);
            Log($"room- {room.Code}");
        }
    }

    private void SendControl(NetPeer peer, ControlId id, Action<NetDataWriter> write)
    {
        _w.Reset();
        _w.Put((byte)Envelope.Control);
        _w.Put((byte)id);
        write(_w);
        peer.Send(_w, DeliveryMethod.ReliableOrdered);
    }

    private void SendError(NetPeer peer, ErrorCode code)
    {
        SendControl(peer, ControlId.Error, w => w.Put((byte)code));
    }

    private static readonly char[] CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    private string GenerateRoomCode()
    {
        Span<byte> buf = stackalloc byte[Protocol.RoomCodeLength];
        Span<char> chars = stackalloc char[Protocol.RoomCodeLength];
        for (var tries = 0; tries < 32; tries++)
        {
            RandomNumberGenerator.Fill(buf);
            for (int i = 0; i < Protocol.RoomCodeLength; i++)
                chars[i] = CodeAlphabet[buf[i] % CodeAlphabet.Length];
            var code = new string(chars);
            if (!_roomsByCode.ContainsKey(code)) return code;
        }
        throw new InvalidOperationException("Could not allocate a unique room code.");
    }

    private static void Log(string msg)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {msg}");
    }
}
