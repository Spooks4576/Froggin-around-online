using External_Packages.KinematicCharacterController.Examples;
using FrogOnline.UI;
using HarmonyLib;
using UnityEngine;

namespace FrogOnline.Patches;

[HarmonyPatch(typeof(ExamplePlayer))]
internal static class ExamplePlayerCursorPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("Update")]
    private static void Update_Postfix()
    {
        if (LobbyUI.Instance?.Visible == true && Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch("Start")]
    private static void Start_Prefix()
    {
        if (LobbyUI.Instance?.Visible == true)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
