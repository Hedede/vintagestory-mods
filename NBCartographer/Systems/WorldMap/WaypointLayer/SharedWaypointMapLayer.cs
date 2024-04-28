using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace NB.Cartographer
{
    [ProtoContract]
    public class SharedWaypoint
    {
        [ProtoMember(1)]
        public Vec3d Position = new Vec3d();
        [ProtoMember(2)]
        public string Title;
        [ProtoMember(3)]
        public int Color;
        [ProtoMember(4)]
        public string Icon = "circle";
        [ProtoMember(5)]
        public bool ShowInWorld;

        [ProtoMember(6)]
        public string OwningPlayerUid = null;
        [ProtoMember(7)]
        public int OwningPlayerGroupId = -1;

        [ProtoMember(8)]
        public HashSet<string> Pins = new HashSet<string>();
        [ProtoMember(9)]
        public bool AllowOthersToEdit = true; // TODO

        [ProtoMember(12)]
        public string Guid { get; set; }

        [ProtoMember(10)]
        public string PlayerName; // client only
        [ProtoMember(11)]
        public int Index; // client only
    }

    public class SharedWaypointMapLayer : MarkerMapLayer
    {
        // Server side
        ICoreServerAPI sapi;
        Dictionary<string, List<SharedWaypoint>> Waypoints = new Dictionary<string, List<SharedWaypoint>>();

        // Client side
        public MeshRef quadModel;
        List<MapComponent> wayPointComponents = new List<MapComponent>();
        List<SharedWaypoint> clientWaypoints = new List<SharedWaypoint>();

        // Both sides
        public WaypointMapLayer WaypointLayer;

        public Dictionary<string, LoadedTexture> texturesByIcon
        {
            get { return WaypointLayer.texturesByIcon; }
        }

        //
        public override string Title => "Shared Markers";
        public override EnumMapAppSide DataSide => EnumMapAppSide.Server;
        public override string LayerGroupCode => "sharedwaypoints";


        public SharedWaypointMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
        {
            api.Logger.Notification("Creating SharedWaypointMapLayer");

            var worldMapManager = mapSink as WorldMapManager;
            if (worldMapManager == null)
            {
                throw new ArgumentException("Map manager is of unexpected type. Expected WorldMapManager.");
            }

            WaypointLayer = worldMapManager.MapLayers.FirstOrDefault((MapLayer ml) => ml is WaypointMapLayer) as WaypointMapLayer;
            if (WaypointLayer == null)
            {
                throw new ArgumentException("Could not find WaypointMapLayer.");
            }

            if (api.Side == EnumAppSide.Server)
            {
                InitServer(api);
            }
            else
            {
                quadModel = (api as ICoreClientAPI).Render.UploadMesh(QuadMeshUtil.GetQuad());
            }

        }

        private void InitServer(ICoreAPI api)
        {
            sapi = api as ICoreServerAPI;

            var parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands.Get("waypoint")
                .BeginSubCommand("shared")
                    .BeginSubCommand("add")
                        .WithDescription("Add a shared waypoint")
                        .WithArgs(parsers.Word("title"), parsers.OptionalWord("color"), parsers.OptionalWord("icon"))
                        .HandleWith(OnCmdAddSharedWayPoint)
                    .EndSubCommand()
                    .BeginSubCommand("list")
                        .WithDescription("List all shared waypoints")
                        .WithArgs(parsers.OptionalWord("player"))
                        .HandleWith(OnCmdWayPointList)
                    .EndSubCommand()
                    .BeginSubCommand("modify")
                        .WithDescription("Modify a shared waypoint")
                        .WithArgs(parsers.Word("waypoint_id"), parsers.Color("color"), parsers.Word("icon"), parsers.Bool("pinned"), parsers.All("title"))
                        .HandleWith(OnCmdWayPointSharedModify)
                    .EndSubCommand()
                    .BeginSubCommand("remove")
                        .WithDescription("Remove a shared waypoint")
                        .WithArgs(parsers.Word("waypoint_id"))
                        .HandleWith(OnCmdWayPointSharedRemove)
                    .EndSubCommand()
                .EndSubCommand()
                .BeginSubCommand("share")
                    .WithDescription("Share an existing waypoint to other players")
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalInt("waypoint_id"))
                    .HandleWith(OnCmdWayPointShare)
                .EndSubCommand()
                .BeginSubCommand("unshare")
                    .WithDescription("Unshare the specified waypoint")
                    .RequiresPlayer()
                    .WithArgs(parsers.Word("waypoint_id"))
                    .HandleWith(OnCmdWayPointUnshare)
                .EndSubCommand();

            if (sapi.ChatCommands.Get("wp") == null)
                sapi.ChatCommands.Create("wp");

            sapi.ChatCommands.Get("wp")
                .WithDescription("Put a waypoint at this location which will be visible for you on the map")
                .RequiresPrivilege(Privilege.chat)

                .BeginSubCommand("add")
                    .WithDescription("Add a shared waypoint to the map")
                    .RequiresPlayer()
                    .WithArgs(parsers.Word("title"), parsers.OptionalWord("color"), parsers.OptionalWord("icon"))
                    .HandleWith(OnCmdAddSharedWayPoint)
                .EndSubCommand()

                .BeginSubCommand("list")
                    .WithDescription("List all shared waypoints")
                    .WithArgs(parsers.OptionalWord("player"))
                    .HandleWith(OnCmdWayPointList)
                .EndSubCommand()

                .BeginSubCommand("modify")
                    .WithDescription("Modify a waypoint")
                    .RequiresPlayer()
                    .WithArgs(parsers.Word("waypoint_id"), parsers.Color("color"), parsers.Word("icon"), parsers.Bool("pinned"), parsers.All("title"))
                    .HandleWith(OnCmdWayPointModify)
                .EndSubCommand()

                .BeginSubCommand("remove")
                    .WithDescription("Remove a waypoint")
                    .RequiresPlayer()
                    .WithArgs(parsers.Word("waypoint_id"))
                    .HandleWith(OnCmdWayPointRemove)
                .EndSubCommand()

                .BeginSubCommand("share")
                    .WithDescription("Share an existing waypoint to other players")
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalInt("waypoint_id"))
                    .HandleWith(OnCmdWayPointShare)
                .EndSubCommand()

                .BeginSubCommand("unshare")
                    .WithDescription("Unshare the specified waypoint")
                    .RequiresPlayer()
                    .WithArgs(parsers.Word("waypoint_id"))
                    .HandleWith(OnCmdWayPointUnshare)
                .EndSubCommand();
            ;

            sapi.Event.GameWorldSave += OnSaveGameGettingSaved;
        }

        // Accessors
        public void WaypointLayer_Rebuild()
        {
            typeof(WaypointMapLayer)
                .GetMethod("RebuildMapComponents", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(WaypointLayer, null);
        }

        public void WaypointLayer_Resend(IServerPlayer player)
        {
            typeof(WaypointMapLayer)
                .GetMethod("ResendWaypoints", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(WaypointLayer, new object[] { player });
        }

        // Serialization
        public void OnSaveGameGettingSaved()
        {
            sapi.WorldManager.SaveGame.StoreData("nb_sharedWaypoints_v2", SerializerUtil.Serialize(Waypoints));

            sapi.World.Logger.Event("Saved " + Waypoints.Count + " shared waypoints");
        }

        public override void OnLoaded()
        {
            base.OnLoaded();
            if (sapi == null)
                return;

            byte[] data = sapi.WorldManager.SaveGame.GetData("nb_sharedWaypoints_v2");
            if (data != null)
            {
                Waypoints = SerializerUtil.Deserialize<Dictionary<string, List<SharedWaypoint>>>(data);

                int totalCount = 0;
                foreach (var entry in Waypoints)
                {
                    if (entry.Value == null)
                    {
                        Waypoints[entry.Key] = new List<SharedWaypoint>();
                        api.World.Logger.Warning("Waypoint list for {0} is null!", entry.Key);
                    }
                    else
                    {
                        totalCount += entry.Value.Count;
                    }
                }

                sapi.World.Logger.Event("Successfully loaded {0} shared waypoints from {1} players", totalCount, Waypoints.Count );
            }
            else
            {
                // Convert waypoints from 1.0/1.1
                data = sapi.WorldManager.SaveGame.GetData("nb_sharedWaypoints");
                if (data != null && WaypointLayer != null)
                {
                    var sharedIndices = SerializerUtil.Deserialize<List<int>>(data);
                    foreach (int index in sharedIndices)
                    {
                        if (index < 0 || index >= WaypointLayer.Waypoints.Count)
                        {
                            sapi.World.Logger.Error("Oops, shared waypoint index is out of range: {0}", index);
                            continue;

                        }
                        ShareWaypoint(WaypointLayer.Waypoints[index]);
                    }

                    // Iterate backwards to avoid shifting indices
                    sharedIndices.Sort();
                    sharedIndices.Reverse();

                    foreach (int index in sharedIndices)
                    {
                        WaypointLayer.Waypoints.RemoveAt(index);
                    }

                    sapi.World.Logger.Notification("Successfully converted " + sharedIndices.Count + " shared waypoints from an older version");
                }
            }
        }

        // Chat commands
        private TextCommandResult OnCmdWayPointShare(TextCommandCallingArgs args)
        {
            if (IsMapDisallowed(out var textCommandResult)) return textCommandResult;

            var player = args.Caller.Player as IServerPlayer;
            var id = (int)args.Parsers[0].GetValue();

            Waypoint[] ownwpaypoints = WaypointLayer.Waypoints.Where((p) => p.OwningPlayerUid == player.PlayerUID).ToArray();

            if (ownwpaypoints.Length == 0)
            {
                return TextCommandResult.Success(Lang.Get("You have no waypoints to share"));
            }

            if (args.Parsers[0].IsMissing)
            {
                id = ownwpaypoints.Length - 1;
            }
            else if (id < 0 || id >= ownwpaypoints.Length)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint number, valid ones are 0..{0}", ownwpaypoints.Length - 1));
            }

            var sid = ShareWaypoint(ownwpaypoints[id]);

            WaypointLayer.Waypoints.Remove(ownwpaypoints[id]);

            ResendWaypoints();
            WaypointLayer_Resend(player);

            return TextCommandResult.Success(Lang.Get("Ok, waypoint {0} is now shared as {1}.", id, sid));
        }

        public string AddWaypoint(SharedWaypoint wp)
        {
            var PlayerData = sapi.PlayerData.GetPlayerDataByUid(wp.OwningPlayerUid);
            var PlayerName = (PlayerData?.LastKnownPlayername.ToLower()) ?? "unknown";

            List<SharedWaypoint> wpList;
            if (!Waypoints.TryGetValue(PlayerName, out wpList))
            {
                wpList = new List<SharedWaypoint>();
                Waypoints.Add(PlayerName, wpList);
            }
            wpList.Add(wp);

            return String.Format("{0}.{1}", PlayerName, wpList.Count - 1);
        }

        public string ShareWaypoint(Waypoint wp)
        {
            SharedWaypoint sharedWp = new SharedWaypoint()
            {
                Color = wp.Color,
                OwningPlayerUid = wp.OwningPlayerUid,
                OwningPlayerGroupId = wp.OwningPlayerGroupId,
                Position = wp.Position,
                Title = wp.Title,
                Icon = wp.Icon,
                Guid = wp.Guid,
            };

            if (wp.Pinned)
            {
                sharedWp.Pins.Add(wp.OwningPlayerUid);
            }

            return AddWaypoint(sharedWp);
        }  

        private TextCommandResult OnCmdWayPointUnshare(TextCommandCallingArgs args)
        {
            if (IsMapDisallowed(out var textCommandResult)) return textCommandResult;

            var player = args.Caller.Player as IServerPlayer;
            var id = args.Parsers[0].GetValue() as string;

            if (Waypoints.Count == 0)
            {
                return TextCommandResult.Success(Lang.Get("There are no shared waypoints"));
            }

            var id_parts = id.Split('.');

            if (args.Parsers[0].IsMissing || id_parts.Length != 2)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint id, must be of format [Player].[Number]", Waypoints.Count));
            }

            var player_name = id_parts[0].ToLower();

            var wp_number = id_parts[1].ToInt(-1);
            var wpList = Waypoints.Get(player_name);
            if (wpList == null)
            {
                return TextCommandResult.Success(Lang.Get("No waypoints with such id"));
            }

            if (wp_number < 0 || wp_number >= wpList.Count)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint number, valid ones are {0}.0..{0}.{1}", player_name, wpList.Count - 1));
            }

            var wpid = UnshareWaypoint(wpList[wp_number]);
            wpList.RemoveAt(wp_number);

            ResendWaypoints();
            WaypointLayer_Resend(player);

            return TextCommandResult.Success(Lang.Get("Ok, unshared waypoint {0}. Now its number is {1}.", id, wpid));
        }

        private TextCommandResult OnCmdWayPointSharedModify(TextCommandCallingArgs args)
        {
            if (IsMapDisallowed(out var textCommandResult)) return textCommandResult;

            var id = args.Parsers[0].GetValue() as string;

            var parsedColor = (System.Drawing.Color)args.Parsers[1].GetValue();
            var icon = args.Parsers[2].GetValue() as string;
            var pinned = (bool)args.Parsers[3].GetValue();
            var title = args.Parsers[4].GetValue() as string;

            var player = args.Caller.Player as IServerPlayer;

            if (Waypoints.Count == 0)
            {
                return TextCommandResult.Success(Lang.Get("There are no shared waypoints"));
            }

            var id_parts = id.Split('.');

            if (args.Parsers[0].IsMissing || id_parts.Length != 2)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint id, must be of format [Player].[Number]", Waypoints.Count));
            }

            var player_name = id_parts[0].ToLower();

            var wp_number = id_parts[1].ToInt(-1);
            var wpList = Waypoints.Get(player_name);
            if (wpList == null)
            {
                return TextCommandResult.Success(Lang.Get("No waypoints with such id"));
            }

            if (wp_number < 0 || wp_number >= wpList.Count)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint number, valid ones are {0}.0..{0}.{1}", player_name, wpList.Count - 1));
            }

            if (string.IsNullOrEmpty(title))
            {
                return TextCommandResult.Success(Lang.Get("command-waypoint-notext"));
            }

            wpList[wp_number].Color = parsedColor.ToArgb() | (255 << 24);
            wpList[wp_number].Title = title;
            if (pinned)
            {
                wpList[wp_number].Pins.Add(player.PlayerUID);
            }
            else
            {
                wpList[wp_number].Pins.Remove(player.PlayerUID);
            }

            if (icon != null)
            {
                wpList[wp_number].Icon = icon;
            }

            ResendWaypoints();
            return TextCommandResult.Success(Lang.Get("Ok, waypoint {0} modified", id));
        }
        private TextCommandResult OnCmdWayPointSharedRemove(TextCommandCallingArgs args)
        {
            if (IsMapDisallowed(out var textCommandResult)) return textCommandResult;
            
            var player = args.Caller.Player as IServerPlayer;
            var id = args.Parsers[0].GetValue() as string;

            if (Waypoints.Count == 0)
            {
                return TextCommandResult.Success(Lang.Get("There are no shared waypoints"));
            }

            var id_parts = id.Split('.');

            if (args.Parsers[0].IsMissing || id_parts.Length != 2)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint id, must be of format [Player].[Number]", Waypoints.Count));
            }

            var player_name = id_parts[0].ToLower();

            var wp_number = id_parts[1].ToInt(-1);
            var wpList = Waypoints.Get(player_name);
            if (wpList == null)
            {
                return TextCommandResult.Success(Lang.Get("No waypoints with such id"));
            }

            if (wp_number < 0 || wp_number >= wpList.Count)
            {
                return TextCommandResult.Success(Lang.Get("Invalid waypoint number, valid ones are {0}.0..{0}.{1}", player_name, wpList.Count - 1));
            }

            wpList.RemoveAt(wp_number);
            RebuildMapComponents();
            ResendWaypoints();
            return TextCommandResult.Success(Lang.Get("Ok, deleted waypoint {0}.", id));
        }

        private TextCommandResult OnCmdWayPointModify(TextCommandCallingArgs args)
        {
            // TODO:
            //var id = args.Parsers[0].GetValue() as string;
            //if (id.Contains('.'))
            return OnCmdWayPointSharedRemove(args);
        }
        private TextCommandResult OnCmdWayPointRemove(TextCommandCallingArgs args)
        {
            // TODO:
            //var id = args.Parsers[0].GetValue() as string;
            //if (id.Contains('.'))
            return OnCmdWayPointSharedModify(args);
        }


        public int UnshareWaypoint(SharedWaypoint sharedWp)
        {
            Waypoint wp = new Waypoint()
            {
                Color = sharedWp.Color,
                OwningPlayerUid = sharedWp.OwningPlayerUid,
                OwningPlayerGroupId = sharedWp.OwningPlayerGroupId,
                Position = sharedWp.Position,
                Title = sharedWp.Title,
                Icon = sharedWp.Icon,
                Pinned = sharedWp.Pins.Contains(sharedWp.OwningPlayerUid),
                Guid = sharedWp.Guid,
            };

            WaypointLayer.Waypoints.Add(wp);

            return WaypointLayer.Waypoints.Count - 1;
        }

        private TextCommandResult OnCmdWayPointList(TextCommandCallingArgs args)
        {
            if (IsMapDisallowed(out var textCommandResult)) return textCommandResult;

            var player = args.Parsers[0].GetValue() as string;
            if (player != null)
                player = player.ToLower();

            var list = new StringBuilder();
            foreach (var entry in Waypoints)
            {
                if (player != null && entry.Key != player)
                    continue;

                int i = 0;
                foreach (var wp in entry.Value)
                {
                    var pos = wp.Position.Clone();
                    pos.X -= api.World.DefaultSpawnPosition.X;
                    pos.Z -= api.World.DefaultSpawnPosition.Z;
                    list.AppendLine(string.Format("{0}.{1}: {2} at {3}", entry.Key, i++, wp.Title, pos.AsBlockPos));
                }
            }

            if (list.Length == 0)
            {
                return TextCommandResult.Success(Lang.Get("No shared waypoints."));
            }

            return TextCommandResult.Success(Lang.Get("Shared waypoints:") + "\n" + list.ToString());
        }

        private TextCommandResult OnCmdAddSharedWayPoint(TextCommandCallingArgs args)
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

            SharedWaypoint sharedWp = new SharedWaypoint()
            {
                Color = color.ToArgb() | (255 << 24),
                OwningPlayerUid = player.PlayerUID,
                Position = player.Entity.Pos.XYZ,
                Title = title,
                Icon = icon,
                Guid = Guid.NewGuid().ToString()
            };

            var id = AddWaypoint(sharedWp);
            ResendWaypoints();
            return TextCommandResult.Success(Lang.Get("Ok, waypoint {0} added", id));

        }

        private bool IsMapDisallowed(out TextCommandResult response)
        {
            if (!api.World.Config.GetBool("allowMap", true))
            {
                response = TextCommandResult.Success(Lang.Get("Maps are disabled on this server"));
                return true;
            }

            response = null;
            return false;
        }

        // Misc
        public override void OnViewChangedServer(IServerPlayer fromPlayer, List<Vec2i> nowVisible, List<Vec2i> nowHidden)
        {
            ResendWaypoints(fromPlayer);
        }

        public override void OnDataFromServer(byte[] data)
        {
            clientWaypoints.Clear();
            clientWaypoints.AddRange(SerializerUtil.Deserialize<List<SharedWaypoint>>(data));
            RebuildMapComponents();
        }

        void ResendWaypoints()
        {
            List<SharedWaypoint> wpList = new List<SharedWaypoint>();
            foreach (var entry in Waypoints)
            {
                int i = 0;
                foreach (var wp in entry.Value)
                {
                    wp.PlayerName = entry.Key;
                    wp.Index = i++;
                    wpList.Add(wp);
                }
            }
            foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
            {
                mapSink.SendMapDataToClient(this, player, SerializerUtil.Serialize(wpList));
            }
        }

        void ResendWaypoints(IServerPlayer player)
        {
            List<SharedWaypoint> wpList = new List<SharedWaypoint>();
            foreach (var entry in Waypoints)
            {
                if (entry.Value == null)
                {
                    sapi.World.Logger.Error("Waypoint list is null!");
                    continue;
                }

                int i = 0;
                foreach (var wp in entry.Value)
                {
                    wp.PlayerName = entry.Key;
                    wp.Index = i++;
                    wpList.Add(wp);
                }
            }
            mapSink.SendMapDataToClient(this, player, SerializerUtil.Serialize(wpList));
        }

        public override void Dispose()
        {
            /*if (texturesByIcon != null)
            {
                foreach (var val in texturesByIcon)
                {
                    val.Value.Dispose();
                }
            }
            texturesByIcon = null;*/
            quadModel?.Dispose();

            base.Dispose();
        }


        // Client
        public override void OnMapOpenedClient()
        {
            //reloadIconTextures();

            //ensureIconTexturesLoaded();

            RebuildMapComponents();
        }
        private void RebuildMapComponents()
        {
            if (!mapSink.IsOpened) return;

            foreach (var comp in wayPointComponents)
            {
                comp.Dispose();
            }

            wayPointComponents.Clear();
            foreach (var wp in clientWaypoints)
            {
                var comp = new SharedWaypointMapComponent(wp, this, api as ICoreClientAPI);
                wayPointComponents.Add(comp);
            }
        }
        public override void Render(GuiElementMap mapElem, float dt)
        {
            if (!Active) return;

            foreach (var val in wayPointComponents)
            {
                val.Render(mapElem, dt);
            }
        }

        public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
        {
            if (!Active) return;

            foreach (var val in wayPointComponents)
            {
                val.OnMouseMove(args, mapElem, hoverText);
            }
        }

        public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
        {
            if (!Active) return;

            foreach (var val in wayPointComponents)
            {
                val.OnMouseUpOnElement(args, mapElem);
                if (args.Handled) break;
            }
        }
    }
}
