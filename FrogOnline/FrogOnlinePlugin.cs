using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FrogOnline.Net;
using FrogOnline.Sync;
using FrogOnline.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrogOnline;

[BepInPlugin(GUID, "FrogOnline", "0.1.0")]
public class FrogOnlinePlugin : BaseUnityPlugin
{
    public const string GUID = "org.froggin.online";

    public static FrogOnlinePlugin Instance { get; private set; }
    public static ManualLogSource Log => Instance.Logger;

    public ConfigEntry<ushort> Port;
    public ConfigEntry<int> TickRate;
    public ConfigEntry<string> LastHostAddress;

    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;

        Port = Config.Bind("Net", "Port", (ushort)27015, "UDP port used for host + client.");
        TickRate = Config.Bind("Net", "TickRate", 30, "Snapshot broadcast rate (Hz). Clamp 10-60.");
        LastHostAddress = Config.Bind("Net", "LastHostAddress", "127.0.0.1", "Remembered host address in the join UI.");

        _harmony = new Harmony(GUID);
        _harmony.PatchAll(typeof(FrogOnlinePlugin).Assembly);

        SceneManager.sceneLoaded += OnSceneLoaded;
        Logger.LogInfo("FrogOnline loaded.");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        NetClient.Instance?.Shutdown();
        _harmony?.UnpatchSelf();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Replicator.Instance?.OnSceneLoaded(scene, mode);
    }

    private void Update()
    {
        NetClient.Ensure();
        NetClient.Instance.Poll();
        if (Input.GetKeyDown(KeyCode.F9)) LobbyUI.Instance.Toggle();

        if (LobbyUI.Instance.Visible && Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void FixedUpdate()
    {
        Replicator.Instance?.FixedTick();
        Patches.TonguePatches.Tick();
    }

    private void LateUpdate()
    {
        Replicator.Instance?.LateRenderTick();
    }

    private void OnGUI()
    {
        DebugHud.Instance?.Draw();
        LobbyUI.Instance?.Draw();
    }
}
