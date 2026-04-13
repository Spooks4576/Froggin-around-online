using FrogOnline.Net;
using FrogOnline.Sync;
using UnityEngine;

namespace FrogOnline.UI;

public sealed class DebugHud
{
    public static DebugHud Instance { get; } = new();

    private GUIStyle _style;

    public void Draw()
    {
        if (NetClient.Instance == null) return;
        if (NetClient.Instance.Phase == ConnectionPhase.Idle) return;

        _style ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = Color.white },
            alignment = TextAnchor.UpperRight,
        };

        var net  = NetClient.Instance;
        var repl = Replicator.Instance;

        var line1 = $"Frog Online · {net.Phase}  ({net.RoomRole})";
        var line2 = $"room {net.RoomCode}  ping {(net.Ping < 0 ? "--" : net.Ping + "ms")}";
        var line3 = repl != null && repl.Active
            ? $"snap#{(repl.HasSnapshot ? repl.LatestSnapshot.Tick.ToString() : "--")}  " +
              $"inp {(repl.HasRemoteInput ? "ok" : "--")}"
            : "";

        var w = 300f;
        var h = string.IsNullOrEmpty(line3) ? 40f : 56f;
        var rect = new Rect(Screen.width - w - 10, 10, w, h);

        var bg = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = bg;

        var inner = new Rect(rect.x + 6, rect.y + 4, rect.width - 12, rect.height - 8);
        GUI.Label(inner, $"{line1}\n{line2}\n{line3}", _style);
    }
}
