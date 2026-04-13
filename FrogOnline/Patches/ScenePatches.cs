using FrogOnline.Net;
using FrogOnline.Shared;
using FrogOnline.Sync;
using HarmonyLib;
using LiteNetLib;
using UnityEngine.SceneManagement;
using Utilities;

namespace FrogOnline.Patches;

[HarmonyPatch(typeof(GameLogicManager))]
internal static class GameLogicManagerPatches
{
    public static bool IsApplyingRemote;

    [HarmonyPrefix]
    [HarmonyPatch("LoadSpecificScene")]
    private static bool LoadSpecificScene_Prefix(string sceneName, LoadSceneMode mode, bool resetPositions)
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return true;
        if (IsApplyingRemote) return true;

        if (repl.IsHost)
        {
            NetClient.Instance.SendGame(
                GamePacketId.SceneChange,
                new SceneChange
                {
                    Scene = sceneName,
                    ResetPositions = resetPositions,
                    Mode = (byte)mode,
                },
                DeliveryMethod.ReliableOrdered);
            return true;
        }

        FrogOnlinePlugin.Log.LogInfo($"[online] suppressed local LoadSpecificScene({sceneName}) on guest; waiting on host");
        return false;
    }

    public static void ApplyFromRemote(SceneChange sc)
    {
        var glm = Singleton<GameLogicManager>.Instance;
        if (glm == null)
        {
            FrogOnlinePlugin.Log.LogWarning("Received SceneChange but GameLogicManager singleton is missing.");
            return;
        }

        var method = AccessTools.Method(typeof(GameLogicManager), "LoadSpecificScene");
        if (method == null)
        {
            FrogOnlinePlugin.Log.LogError("Could not resolve GameLogicManager.LoadSpecificScene via reflection.");
            return;
        }

        IsApplyingRemote = true;
        try
        {
            method.Invoke(glm, new object[] { sc.Scene, (LoadSceneMode)sc.Mode, sc.ResetPositions });
            FrogOnlinePlugin.Log.LogInfo($"[online] guest applied host scene change: {sc.Scene}");
        }
        finally
        {
            IsApplyingRemote = false;
        }
    }

    public static void ApplyAdvanceFromRemote()
    {
        var glm = Singleton<GameLogicManager>.Instance;
        if (glm == null) return;
        IsApplyingRemote = true;
        try
        {
            glm.StartCoroutine(glm.AsyncLoadNextScene());
            FrogOnlinePlugin.Log.LogInfo("[online] guest advanced scene to follow host");
        }
        finally
        {
            IsApplyingRemote = false;
        }
    }
}

[HarmonyPatch(typeof(LoadNextSceneAction))]
internal static class LoadNextSceneActionPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(LoadNextSceneAction.ExecuteAction))]
    private static bool ExecuteAction_Prefix()
    {
        var repl = Replicator.Instance;
        if (repl == null || !repl.Active) return true;
        if (GameLogicManagerPatches.IsApplyingRemote) return true;

        if (repl.IsHost)
        {
            NetClient.Instance.SendGameRaw(
                GamePacketId.AdvanceScene,
                DeliveryMethod.ReliableOrdered,
                _ => { });
            return true;
        }

        FrogOnlinePlugin.Log.LogInfo("[online] suppressed LoadNextSceneAction on guest; waiting on host");
        return false;
    }
}
