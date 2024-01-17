
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace NB.Cartographer
{
    public class WaypointMapLayerPatches
    {
        Harmony Harmony;
        public WaypointMapLayerPatches(ICoreAPI api)
        {
            api.Logger.Notification("Applying Harmony patches...");
            Harmony = new Harmony("NBCartographer");

            var Type = typeof(WaypointMapLayer);
            var PatcherType = typeof(WaypointMapLayerPatches);

            Harmony.Patch(Type.GetMethod("ResendWaypoints", BindingFlags.Instance | BindingFlags.NonPublic),
                prefix: PatcherType.GetMethod("Pre_ResendWaypoints"));

            Harmony.Patch(Type.GetMethod("OnCmdWayPointRemove", BindingFlags.Instance | BindingFlags.NonPublic),
                prefix: PatcherType.GetMethod("Pre_OnCmdWayPointRemove"));

            api.Logger.Notification("Applying Harmony patches... OK");
        }
        public void Dispose()
        {
            Harmony.UnpatchAll("NBCartographer");
        }
        public static bool Pre_ResendWaypoints(WaypointMapLayer __instance, IServerPlayer toPlayer)
        {
            var inst = __instance as SharableWaypointMapLayer;
            if (inst != null)
            {
                inst.ResendWaypoints(toPlayer);

                return false;
            }
            return true;
        }

        public static bool Pre_OnCmdWayPointRemove(WaypointMapLayer __instance, TextCommandCallingArgs args)
        {
            var inst = __instance as SharableWaypointMapLayer;
            if (inst != null)
            {
                var player = args.Caller.Player as IServerPlayer;
                var id = args.Parsers[0].GetValue() as int?;

                if (id.HasValue)
                {
                    inst.OnRemoveWaypoint(player, (int)id);

                }
            }
            return true;
        }
    }
}