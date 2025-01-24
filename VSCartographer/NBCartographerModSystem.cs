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
        ClientPatches clientPatches;

        // Called on server and client
        // Useful for registering block/entity classes on both sides
        public override void Start(ICoreAPI api)
        {
            api.ModLoader.GetModSystem<WorldMapManager>().RegisterMapLayer<SharedWaypointMapLayer>("sharedwaypoints", 1.1);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            clientPatches = new ClientPatches(api);
        }
        public override void Dispose()
        {
            clientPatches?.Dispose();

            base.Dispose();
        }
    }
}
