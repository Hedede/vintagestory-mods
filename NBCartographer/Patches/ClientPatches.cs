
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using static HarmonyLib.Code;

namespace NB.Cartographer
{
    public class ClientPatches
    {
        Harmony Harmony;
        public ClientPatches(ICoreAPI api)
        {
            api.Logger.Notification("Applying Harmony patches...");
            Harmony = new Harmony("NB.Cartographer");

            var PatcherType = typeof(ClientPatches);
            
            Harmony.Patch(typeof(GuiDialogAddWayPoint).GetMethod("onSave", BindingFlags.Instance | BindingFlags.NonPublic),
                postfix: PatcherType.GetMethod("Post_GuiDialogAddWayPoint_onSave"));
            Harmony.Patch(typeof(GuiDialogAddWayPoint).GetMethod("ComposeDialog", BindingFlags.Instance | BindingFlags.NonPublic),
                transpiler: PatcherType.GetMethod("ComposeDialog_Transpiler"));

            Harmony.Patch(typeof(GuiDialogEditWayPoint).GetMethod("onSave", BindingFlags.Instance | BindingFlags.NonPublic),
                postfix: PatcherType.GetMethod("Post_GuiDialogEditWayPoint_onSave"));
            Harmony.Patch(typeof(GuiDialogEditWayPoint).GetMethod("ComposeDialog", BindingFlags.Instance | BindingFlags.NonPublic),
                transpiler: PatcherType.GetMethod("ComposeDialog_Transpiler"));

            api.Logger.Notification("Applying Harmony patches... OK");
        }
        public void Dispose()
        {
            Harmony.UnpatchAll("NB.Cartographer");
        }
        static void onSharedToggled(bool on) { }
        public static GuiComposer AddShareComponent(GuiComposer composer, ref ElementBounds leftColumn, ref ElementBounds rightColumn)
        {
            return composer
                .AddStaticText(Lang.Get("Shared"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 9))
                .AddSwitch(onSharedToggled, rightColumn = rightColumn.BelowCopy(0, 5).WithFixedWidth(200), "sharedSwitch");
        }

        public static IEnumerable<CodeInstruction> ComposeDialog_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var found = false;

            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldstr && (string)instruction.operand == "waypoint-color")
                {
                    //yield return new CodeInstruction(OpCodes.Call, typeof(GuiDialog).GetMethod("get_SingleComposer", BindingFlags.Instance | BindingFlags.Public));

                    yield return new CodeInstruction(OpCodes.Ldloca_S, 0);
                    yield return new CodeInstruction(OpCodes.Ldloca_S, 1);
                    yield return new CodeInstruction(OpCodes.Call, typeof(ClientPatches).GetMethod("AddShareComponent", BindingFlags.Static | BindingFlags.Public));

                    found = true;
                }

                yield return instruction;
            }

            if (!found)
            {
                throw new ArgumentException("Couldn't find Ldstr \"waypoint-color\"");
            }
        }

        public static void Post_GuiDialogAddWayPoint_onSave(GuiDialogAddWayPoint __instance)
        {
            bool shared = __instance.SingleComposer.GetSwitch("sharedSwitch").On;
            if (shared)
            {
                var capi = typeof(GuiDialog)
                    .GetField("capi", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance)
                    .GetValue(__instance) as ICoreClientAPI;
                capi.SendChatMessage(string.Format("/waypoint share"));
            }
        }

        public static void Post_GuiDialogEditWayPoint_onSave(GuiDialogEditWayPoint __instance)
        {
            bool shared = __instance.SingleComposer.GetSwitch("sharedSwitch").On;
            if (shared)
            {
                var capi = typeof(GuiDialog)
                    .GetField("capi", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance)
                    .GetValue(__instance) as ICoreClientAPI;
                var wpIndex = (int)typeof(GuiDialogEditWayPoint)
                    .GetField("wpIndex", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance)
                    .GetValue(__instance);
                capi.SendChatMessage(string.Format("/waypoint share {0}", wpIndex));
            }
        }
    }
}