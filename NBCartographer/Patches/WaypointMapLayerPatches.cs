
using System.Collections.Generic;
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

            Harmony.Patch(typeof(WaypointMapLayer).GetMethod("ResendWaypoints", BindingFlags.Instance | BindingFlags.NonPublic),
                prefix: typeof(WaypointMapLayerPatches).GetMethod("_PreResendWaypoints"));
            api.Logger.Notification("Applying Harmony patches... OK");
        }
        public void Dispose()
        {
            Harmony.UnpatchAll("NBCartographer");
        }
        public static bool _PreResendWaypoints(WaypointMapLayer __instance, IServerPlayer toPlayer)
        {
            var inst = __instance as SharableWaypointMapLayer;
            if (inst != null)
            {
                inst.ResendWaypoints(toPlayer);

                return false;
            }
            return true;
        }
    }
}