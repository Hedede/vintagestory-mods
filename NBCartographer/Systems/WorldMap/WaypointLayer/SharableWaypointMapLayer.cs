
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace NB.Cartographer
{
    public class SharableWaypointMapLayer : WaypointMapLayer
    {
        // Server side
        public List<Waypoint> SharedWaypoints = new List<Waypoint>();
        ICoreServerAPI sapi;

        public SharableWaypointMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
            api.Logger.Notification("Creating SharableWaypointMapLayer");

            if (api.Side == EnumAppSide.Server)
            {
                sapi = api as ICoreServerAPI;

                var parsers = sapi.ChatCommands.Parsers;

                sapi.Event.GameWorldSave += OnSaveGameGettingSaved;

                //sapi.ChatCommands.Get("waypoint")
                sapi.ChatCommands.Create("wp")
                    .WithDescription("Put a waypoint at this location which will be visible for you on the map")
                    .RequiresPrivilege(Privilege.chat)

                    .BeginSubCommand("add")
                        .WithDescription("Add a shared waypoint to the map")
                        .RequiresPlayer()
                        .WithArgs(parsers.Word("title"), parsers.OptionalWord("color"), parsers.OptionalWord("icon"))
                        .HandleWith(OnCmdWayPointAddShared)
                    .EndSubCommand()

                    .BeginSubCommand("share")
                        .WithDescription("Share an existing waypoint to other players")
                        .RequiresPlayer()
                        .WithArgs(parsers.Int("waypoint_id"))
                        .HandleWith(OnCmdWayPointShare)
                    .EndSubCommand()

                    .BeginSubCommand("unshare")
                        .WithDescription("Unshare the specified waypoint")
                        .RequiresPlayer()
                        .WithArgs(parsers.Int("waypoint_id"))
                        .HandleWith(OnCmdWayPointUnshare)
                    .EndSubCommand();
                ;
            } else
            {
                quadModel = (api as ICoreClientAPI).Render.UploadMesh(QuadMeshUtil.GetQuad());
            }
        }

        private TextCommandResult OnCmdWayPointAddShared(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            var title = args.Parsers[0].GetValue() as string;
            var colorString = args.Parsers[1].GetValue() as string ?? "gray";
            var icon = args.Parsers[2].GetValue() as string ?? "circle";

            Color color;

            if (colorString.StartsWith("#"))
            {
                try
                {
                    var argb = int.Parse(colorString.Replace("#", ""), NumberStyles.HexNumber);
                    color = Color.FromArgb(argb);
                }
                catch (FormatException)
                {
                    return TextCommandResult.Success(Lang.Get("command-waypoint-invalidcolor"));
                }
            }
            else
            {
                color = Color.FromName(colorString);
            }

            var waypoint = new Waypoint()
            {
                Color = color.ToArgb() | (255 << 24),
                OwningPlayerUid = player.PlayerUID,
                Position = player.Entity.Pos.XYZ,
                Title = title,
                Icon = icon,
                Pinned = false,
                Guid = Guid.NewGuid().ToString()
            };

            var nr = AddWaypoint(waypoint, player);
            SharedWaypoints.Add(waypoint);
            ResendWaypointsToOtherPlayers(player);
            return TextCommandResult.Success(Lang.Get("Ok, waypoint nr. {0} added", nr));

        }
        private TextCommandResult OnCmdWayPointShare(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            var id = (int)args.Parsers[0].GetValue();

            Waypoint[] ownwpaypoints = Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray();

            if (ownwpaypoints.Length == 0)
            {
                return TextCommandResult.Success(Lang.Get("You have no waypoints to share"));
            }

            if (args.Parsers[0].IsMissing || id < 0 || id >= ownwpaypoints.Length)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint number, valid ones are 0..{0}", ownwpaypoints.Length - 1));
            }

            if (SharedWaypoints.Contains(ownwpaypoints[id]))
            {
                return TextCommandResult.Success(Lang.Get("Waypoint {0} is already shared", id));
            }

            SharedWaypoints.Add(ownwpaypoints[id]);
            ResendWaypointsToOtherPlayers(player);

            return TextCommandResult.Success(Lang.Get("Ok, waypoint {0} is now shared.", id));
        }
        private TextCommandResult OnCmdWayPointUnshare(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player as IServerPlayer;
            var id = (int)args.Parsers[0].GetValue();

            Waypoint[] ownwpaypoints = Waypoints
                .Where((p) => p.OwningPlayerUid == player.PlayerUID)
                .Concat(
                    SharedWaypoints
                    .Where((p) => p.OwningPlayerUid != player.PlayerUID))
                .ToArray();

            if (ownwpaypoints.Length == 0)
            {
                return TextCommandResult.Success(Lang.Get("You have no waypoints to unshare"));
            }

            if (args.Parsers[0].IsMissing || id < 0 || id >= ownwpaypoints.Length)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint number, valid ones are 0..{0}", ownwpaypoints.Length - 1));
            }

            if (!SharedWaypoints.Contains(ownwpaypoints[id]))
            {
                return TextCommandResult.Success(Lang.Get("Waypoint {0} is not shared", id));

            }

            SharedWaypoints.Remove(ownwpaypoints[id]);
            RebuildMapComponents();
            ResendWaypointsToOtherPlayers(player);
            return TextCommandResult.Success(Lang.Get("Ok, unshared waypoint {0}.", id));
        }

        public void OnRemoveWaypoint(IServerPlayer player, int id)
        {
            Waypoint[] ownwpaypoints = Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray();

            if (id < 0 || id >= ownwpaypoints.Length)
                return;

            int sharedIndex = SharedWaypoints.IndexOf(ownwpaypoints[id]);

            if (sharedIndex != -1)
            {
                SharedWaypoints.RemoveAt(sharedIndex);
                ResendWaypointsToOtherPlayers(player);
            }
        }

        public void OnSaveGameGettingSaved()
        {
            var sharedIndices = new List<int>();

            // This is O(n*m) ==> Slow, but hopefully we don't have THAT many waypoints to impact save times significantly
            foreach (Waypoint wp in SharedWaypoints)
            {
                sharedIndices.Add(Waypoints.IndexOf(wp));
            }

            sapi.WorldManager.SaveGame.StoreData("nb_sharedWaypoints", SerializerUtil.Serialize(sharedIndices));

            sapi.World.Logger.Event("Saved " + sharedIndices.Count + " shared waypoints");
        }

        public override void OnLoaded()
        {
            base.OnLoaded();
            if (sapi != null)
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData("nb_sharedWaypoints");
                if (data != null)
                {
                    var sharedIndices = SerializerUtil.Deserialize<List<int>>(data);
                    foreach (int index in sharedIndices)
                    {
                        if (index < 0 || index >= Waypoints.Count)
                        {
                            sapi.World.Logger.Error("Oops, shared waypoint index is out of range: {}", index);
                            continue;

                        }
                        SharedWaypoints.Add(Waypoints[index]);
                    }

                    sapi.World.Logger.Notification("Successfully loaded " + SharedWaypoints.Count + " shared waypoints");
                }
            }
        }
        public void ResendWaypointsToOtherPlayers(IPlayer exceptPlayer)
        {
            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                if (player != exceptPlayer)
                    ResendWaypoints(player as IServerPlayer);
            }
        }

        public void ResendWaypoints(IServerPlayer toPlayer)
        {
            // TODO: Verify that this is still currect upon future updates
            Dictionary<int, PlayerGroupMembership> memberOfGroups = toPlayer.ServerData.PlayerGroupMemberships;
            List<Waypoint> hisMarkers = new List<Waypoint>();

            foreach (Waypoint marker in Waypoints)
            {
                if (toPlayer.PlayerUID == marker.OwningPlayerUid || memberOfGroups.ContainsKey(marker.OwningPlayerGroupId))
                    hisMarkers.Add(marker);
            }

            foreach (Waypoint marker in SharedWaypoints)
            {
                if (toPlayer.PlayerUID != marker.OwningPlayerUid && !memberOfGroups.ContainsKey(marker.OwningPlayerGroupId))
                    hisMarkers.Add(marker);
            }

            mapSink.SendMapDataToClient(this, toPlayer, SerializerUtil.Serialize(hisMarkers));
        }

        public void RebuildMapComponents()
        {
            typeof(WaypointMapLayer)
                .GetMethod("RebuildMapComponents", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(this, null);
        }

    }
}
