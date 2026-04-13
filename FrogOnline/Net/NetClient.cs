using System;
using System.Collections.Generic;
using FrogOnline.Shared;
using FrogOnline.Sync;
using LiteNetLib;
using LiteNetLib.Utils;

namespace FrogOnline.Net;

public enum ConnectionPhase : byte
{
    Idle,
    Connecting,
    Connected,
    InRoom,
    RoomReady,
}

public sealed class NetClient : INetEventListener
{
    public static NetClient Instance { get; private set; }
    public static void Ensure() { Instance ??= new NetClient(); }

    public ConnectionPhase Phase { get; private set; } = ConnectionPhase.Idle;
    public RoomRole RoomRole { get; private set; }
    public string RoomCode { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public int Ping => _server?.Ping ?? -1;

    private LiteNetLib.NetManager _net;
    private NetPeer _server;
    private readonly NetDataWriter _w = new();

    public event Action<GamePacketId, NetPacketReader> OnGamePacket;
    public event Action OnRoomReady;
    public event Action OnPeerLeft;

    public void ConnectToServer(string host, ushort port)
    {
        Disconnect();
        _net = new LiteNetLib.NetManager(this) { AutoRecycle = true };
        if (!_net.Start())
        {
            FrogOnlinePlugin.Log.LogError("Could not start UDP client.");
            return;
        }
        _net.Connect(host, port, Protocol.ConnectKey);
        Phase = ConnectionPhase.Connecting;
        FrogOnlinePlugin.Log.LogInfo($"Connecting to {host}:{port}");
    }

    public void Disconnect()
    {
        _net?.Stop();
        _net = null;
        _server = null;
        Phase = ConnectionPhase.Idle;
        RoomCode = string.Empty;
    }

    public void Shutdown() => Disconnect();

    public void Poll() => _net?.PollEvents();

    public void CreateRoom()
    {
        if (Phase != ConnectionPhase.Connected) return;
        SendControl(ControlId.CreateRoom, _ => { });
    }

    public void JoinRoom(string code)
    {
        if (Phase != ConnectionPhase.Connected) return;
        SendControl(ControlId.JoinRoom, w => w.Put(code));
    }

    public void LeaveRoom()
    {
        if (Phase != ConnectionPhase.InRoom && Phase != ConnectionPhase.RoomReady) return;
        SendControl(ControlId.LeaveRoom, _ => { });
    }

    public void SendGame<T>(GamePacketId id, T payload, DeliveryMethod method) where T : INetSerializable
    {
        if (_server == null || _server.ConnectionState != ConnectionState.Connected) return;
        _w.Reset();
        _w.Put((byte)Envelope.Relay);
        _w.Put((byte)id);
        payload.Serialize(_w);
        _server.Send(_w, method);
    }

    public void SendGameRaw(GamePacketId id, DeliveryMethod method, Action<NetDataWriter> write)
    {
        if (_server == null || _server.ConnectionState != ConnectionState.Connected) return;
        _w.Reset();
        _w.Put((byte)Envelope.Relay);
        _w.Put((byte)id);
        write?.Invoke(_w);
        _server.Send(_w, method);
    }

    private void SendControl(ControlId id, Action<NetDataWriter> write)
    {
        if (_server == null || _server.ConnectionState != ConnectionState.Connected) return;
        _w.Reset();
        _w.Put((byte)Envelope.Control);
        _w.Put((byte)id);
        write?.Invoke(_w);
        _server.Send(_w, DeliveryMethod.ReliableOrdered);
    }

    public void OnConnectionRequest(ConnectionRequest request) => request.Reject();

    public void OnPeerConnected(NetPeer peer)
    {
        _server = peer;
        Phase = ConnectionPhase.Connected;
        FrogOnlinePlugin.Log.LogInfo($"Connected to relay {peer.EndPoint}");
        SendControl(ControlId.Hello, w => w.Put(Protocol.Version));
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        FrogOnlinePlugin.Log.LogInfo($"Disconnected: {disconnectInfo.Reason}");
        _server = null;
        var wasLive = Phase == ConnectionPhase.RoomReady || Phase == ConnectionPhase.InRoom;
        Phase = ConnectionPhase.Idle;
        RoomCode = string.Empty;
        if (wasLive)
        {
            LastError = $"Disconnected: {disconnectInfo.Reason}";
            Sync.Replicator.Instance?.OnPeerLeft();
        }
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (reader.AvailableBytes == 0) return;
        var env = (Envelope)reader.GetByte();
        switch (env)
        {
            case Envelope.Control: HandleControl(reader); break;
            case Envelope.Relay:   HandleRelay(reader); break;
        }
    }

    private void HandleControl(NetPacketReader r)
    {
        if (r.AvailableBytes == 0) return;
        var id = (ControlId)r.GetByte();
        switch (id)
        {
            case ControlId.HelloAck:
                break;

            case ControlId.RoomCreated:
                RoomCode = r.GetString(32);
                RoomRole = (RoomRole)r.GetByte();
                Phase = ConnectionPhase.InRoom;
                FrogOnlinePlugin.Log.LogInfo($"Room {RoomCode} created (host).");
                break;

            case ControlId.RoomJoined:
                RoomCode = r.GetString(32);
                RoomRole = (RoomRole)r.GetByte();
                Phase = ConnectionPhase.InRoom;
                FrogOnlinePlugin.Log.LogInfo($"Joined room {RoomCode} as {RoomRole}.");
                break;

            case ControlId.PeerReady:
                var otherRole = (RoomRole)r.GetByte();
                Phase = ConnectionPhase.RoomReady;
                FrogOnlinePlugin.Log.LogInfo($"Peer ({otherRole}) is in the room. Room ready.");
                Replicator.Ensure();
                Replicator.Instance.OnRoomReady(RoomRole);
                OnRoomReady?.Invoke();
                break;

            case ControlId.PeerLeft:
                Phase = ConnectionPhase.InRoom;
                FrogOnlinePlugin.Log.LogInfo("Peer left the room.");
                Replicator.Instance?.OnPeerLeft();
                OnPeerLeft?.Invoke();
                break;

            case ControlId.RoomClosed:
                Phase = ConnectionPhase.Connected;
                RoomCode = string.Empty;
                Replicator.Instance?.OnPeerLeft();
                break;

            case ControlId.Error:
                var code = (ErrorCode)r.GetByte();
                LastError = code.ToString();
                FrogOnlinePlugin.Log.LogWarning($"Server error: {code}");
                break;
        }
    }

    private void HandleRelay(NetPacketReader r)
    {
        if (r.AvailableBytes == 0) return;
        var id = (GamePacketId)r.GetByte();
        OnGamePacket?.Invoke(id, r);
    }

    public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) { }
    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}
