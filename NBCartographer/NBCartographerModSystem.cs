using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace NB.Cartographer
{
    public class NBCartographerModSystem : ModSystem
    {
        WaypointMapLayerPatches patches;

        // Called on server and client
        // Useful for registering block/entity classes on both sides
        public override void Start(ICoreAPI api)
        {
        }

        private TextCommandResult OnCmdWayPointAddShared(TextCommandCallingArgs args)
        {
            return TextCommandResult.Success(Lang.Get("Hren"));

        }
        public override void StartServerSide(ICoreServerAPI api)
        {
            patches = new WaypointMapLayerPatches(api);

            api.ModLoader.GetModSystem<WorldMapManager>().RegisterMapLayer<SharableWaypointMapLayer>("waypoints", 1.0);

        }

        public override void StartClientSide(ICoreClientAPI api)
        {
        }
    }
}
