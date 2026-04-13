using FrogOnline.Net;
using UnityEngine;

namespace FrogOnline.UI;

public sealed class LobbyUI
{
    public static LobbyUI Instance { get; } = new();

    private string _serverAddr = "127.0.0.1";
    private string _serverPort = "27015";
    private string _joinCode = string.Empty;
    private bool _visible = true;
    private bool _focusRequested = true;

    private const int W = 360;
    private const int H = 260;

    public bool Visible => _visible;

    public void Toggle()
    {
        _visible = !_visible;
        if (_visible) _focusRequested = true;
    }

    public void Show()
    {
        _visible = true;
        _focusRequested = true;
    }

    public void Draw()
    {
        if (!_visible) return;
        if (NetClient.Instance == null) NetClient.Ensure();

        var rect = new Rect(20, 20, W, H);
        GUI.Box(rect, "Frog Online");

        GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 25, rect.width - 20, rect.height - 35));

        var net = NetClient.Instance;
        var phase = net.Phase;

        GUILayout.Label($"Status: {phase}    Ping: {(net.Ping < 0 ? "--" : net.Ping + "ms")}");
        if (!string.IsNullOrEmpty(net.LastError))
            GUILayout.Label($"<color=red>Error: {net.LastError}</color>");

        GUILayout.Space(4);

        if (phase == ConnectionPhase.Idle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Server:", GUILayout.Width(60));
            GUI.SetNextControlName("serverAddr");
            _serverAddr = GUILayout.TextField(_serverAddr, GUILayout.Width(160));
            GUILayout.Label(":", GUILayout.Width(8));
            _serverPort = GUILayout.TextField(_serverPort, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            if (_focusRequested && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl("serverAddr");
                _focusRequested = false;
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Connect"))
            {
                if (ushort.TryParse(_serverPort, out var p))
                {
                    FrogOnlinePlugin.Instance.LastHostAddress.Value = _serverAddr;
                    net.ConnectToServer(_serverAddr.Trim(), p);
                }
            }
        }
        else if (phase == ConnectionPhase.Connecting)
        {
            GUILayout.Label("Connecting…");
            if (GUILayout.Button("Cancel")) net.Disconnect();
        }
        else if (phase == ConnectionPhase.Connected)
        {
            GUILayout.Space(6);
            if (GUILayout.Button("Create Room", GUILayout.Height(28)))
                net.CreateRoom();

            GUILayout.Space(10);
            GUILayout.Label("Join Room");
            GUILayout.BeginHorizontal();
            _joinCode = GUILayout.TextField(_joinCode ?? string.Empty, 8, GUILayout.Width(120)).ToUpperInvariant();
            if (GUILayout.Button("Join", GUILayout.Width(80)))
            {
                if (!string.IsNullOrWhiteSpace(_joinCode))
                    net.JoinRoom(_joinCode.Trim());
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("Disconnect")) net.Disconnect();
        }
        else if (phase == ConnectionPhase.InRoom)
        {
            GUILayout.Label($"Room: <b>{net.RoomCode}</b>  (you: {net.RoomRole})");
            GUILayout.Label(net.RoomRole == Shared.RoomRole.Host
                ? "Share the room code with your friend."
                : "Waiting for host to continue…");
            if (GUILayout.Button("Leave Room")) net.LeaveRoom();
        }
        else if (phase == ConnectionPhase.RoomReady)
        {
            GUILayout.Label($"Room {net.RoomCode} — live ({net.RoomRole}).");
            GUILayout.Label("Close this overlay and play.");
            if (GUILayout.Button("Hide overlay (F9)")) _visible = false;
            if (GUILayout.Button("Leave Room")) net.LeaveRoom();
        }

        GUILayout.EndArea();
    }
}
