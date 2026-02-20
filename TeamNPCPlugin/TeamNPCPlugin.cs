using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace TeamNPCPlugin
{
    [ApiVersion(2, 1)]
    public class TeamNPCPlugin : TerrariaPlugin
    {
        public override string Name => "TeamNPC";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "stardust";
        public override string Description => "Team NPC separation plugin - Red and Blue teams each have independent town NPCs";

        private Configuration config;
        private NPCTeamManager npcTeamManager;
        private NPCSpawnController spawnController;
        private ArrivalConditionChecker conditionChecker;
        private Dictionary<string, TeamState> teamStates;

        // Cached cctgPlugin house positions (updated periodically via reflection)
        private Point cachedRedHouseSpawn = new Point(-1, -1);
        private Point cachedBlueHouseSpawn = new Point(-1, -1);
        private bool cctgHousePositionsResolved = false;
        private int houseCheckTicks = 0;
        private const int HouseCheckInterval = 300; // Check every 5 seconds

        public TeamNPCPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            // Load configuration
            config = Configuration.Load();

            if (!config.EnablePlugin)
            {
                TShock.Log.Info("[TeamNPC] Plugin disabled");
                return;
            }

            // Housing validator disabled
            // var housingValidator = new HousingValidator
            // {
            //     EnableDebugLog = config.EnableDebugLog
            // };

            // Initialize modules
            npcTeamManager = new NPCTeamManager();
            spawnController = new NPCSpawnController(npcTeamManager, config);

            // Initialize team states with placeholder spawn points (will be updated in OnPostInit)
            teamStates = new Dictionary<string, TeamState>
            {
                ["Red"] = new TeamState
                {
                    TeamName = "Red",
                    TeamId = 1,
                    SpawnCenter = new Point(0, 0),  // Placeholder
                    MinX = 0,
                    MaxX = 0,  // Will be updated in OnPostInit
                    HousingValidator = null,  // Disabled
                    NPCManager = npcTeamManager,
                    EnableDebugLog = config.EnableDebugLog
                },
                ["Blue"] = new TeamState
                {
                    TeamName = "Blue",
                    TeamId = 3,
                    SpawnCenter = new Point(0, 0),  // Placeholder
                    MinX = 0,
                    MaxX = Main.maxTilesX,
                    HousingValidator = null,  // Disabled
                    NPCManager = npcTeamManager,
                    EnableDebugLog = config.EnableDebugLog
                }
            };

            conditionChecker = new ArrivalConditionChecker(
                npcTeamManager,
                spawnController,
                teamStates,
                config.CheckIntervalSeconds,
                config
            );

            // Housing persistence disabled
            // if (config.EnableHousingPersistence)
            // {
            //     try
            //     {
            //         var persistedData = HousingPersistence.Load();
            //         HousingPersistence.ApplyToTeamStates(persistedData, teamStates, housingValidator);
            //         TShock.Log.Info("[TeamNPC] Housing assignments loaded from persistence");
            //     }
            //     catch (Exception ex)
            //     {
            //         TShock.Log.Error($"[TeamNPC] Failed to load housing persistence: {ex.Message}");
            //     }
            // }

            // Register event hooks
            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInit);
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
            ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
            ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
            GetDataHandlers.NPCHome += OnUpdateNPCHome;

            // Monitor player commands to allow command-spawned NPCs
            PlayerHooks.PlayerCommand += OnPlayerCommand;

            TShock.Log.Info($"[TeamNPC] Plugin loaded! Spawn positions will be calculated after world initialization.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Save housing persistence before disposing
                if (config != null && config.EnableHousingPersistence && teamStates != null)
                {
                    try
                    {
                        HousingPersistence.Save(teamStates);
                        TShock.Log.Info("[TeamNPC] Housing assignments saved");
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.Error($"[TeamNPC] Failed to save housing persistence: {ex.Message}");
                    }
                }

                // Deregister event hooks
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInit);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
                ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
                GetDataHandlers.NPCHome -= OnUpdateNPCHome;
                PlayerHooks.PlayerCommand -= OnPlayerCommand;
            }
            base.Dispose(disposing);
        }

        private void OnPostInit(EventArgs args)
        {
            // Now that the world is loaded, calculate actual spawn positions
            int redSpawnX = Main.spawnTileX + config.RedTeamSpawnX;
            int redSpawnY = Main.spawnTileY + config.RedTeamSpawnY;
            int blueSpawnX = Main.spawnTileX + config.BlueTeamSpawnX;
            int blueSpawnY = Main.spawnTileY + config.BlueTeamSpawnY;

            // Calculate partition boundary
            int partitionBoundary = config.PartitionBoundary >= 0
                ? config.PartitionBoundary
                : Main.spawnTileX;

            // Update team states with actual spawn positions and partition boundaries
            teamStates["Red"].SpawnCenter = new Point(redSpawnX, redSpawnY);
            teamStates["Red"].MinX = 0;
            teamStates["Red"].MaxX = partitionBoundary;

            teamStates["Blue"].SpawnCenter = new Point(blueSpawnX, blueSpawnY);
            teamStates["Blue"].MinX = partitionBoundary + 1;
            teamStates["Blue"].MaxX = Main.maxTilesX;

            TShock.Log.Info($"[TeamNPC] World spawn: ({Main.spawnTileX}, {Main.spawnTileY})");
            TShock.Log.Info($"[TeamNPC] Red team spawn: ({redSpawnX}, {redSpawnY}) [offset: ({config.RedTeamSpawnX}, {config.RedTeamSpawnY})]");
            TShock.Log.Info($"[TeamNPC] Red team partition: X = {teamStates["Red"].MinX} to {teamStates["Red"].MaxX}");
            TShock.Log.Info($"[TeamNPC] Blue team spawn: ({blueSpawnX}, {blueSpawnY}) [offset: ({config.BlueTeamSpawnX}, {config.BlueTeamSpawnY})]");
            TShock.Log.Info($"[TeamNPC] Blue team partition: X = {teamStates["Blue"].MinX} to {teamStates["Blue"].MaxX}");
            TShock.Log.Info($"[TeamNPC] Partition boundary: {partitionBoundary}");

            if (config.ClearNPCsOnLoad)
            {
                TShock.Log.Info("[TeamNPC] Clearing existing town NPCs...");
                spawnController.ClearAllTownNPCs();
            }
        }

        private void OnGameUpdate(EventArgs args)
        {
            // Periodically try to resolve cctgPlugin house positions
            if (!cctgHousePositionsResolved)
            {
                houseCheckTicks++;
                if (houseCheckTicks >= HouseCheckInterval)
                {
                    houseCheckTicks = 0;
                    TryResolveCctgHousePositions();
                }
            }

            conditionChecker.OnGameUpdate(args);
        }

        /// <summary>
        /// Try to read house spawn positions from cctgPlugin via reflection
        /// </summary>
        private void TryResolveCctgHousePositions()
        {
            try
            {
                foreach (var pluginContainer in ServerApi.Plugins)
                {
                    var plugin = pluginContainer.Plugin;
                    if (plugin.Name != "CctgPlugin")
                        continue;

                    // Get the private houseBuilder field
                    var houseBuilderField = plugin.GetType().GetField("houseBuilder",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (houseBuilderField == null)
                        break;

                    var houseBuilder = houseBuilderField.GetValue(plugin);
                    if (houseBuilder == null)
                        break;

                    var hbType = houseBuilder.GetType();

                    // Read HousesBuilt
                    var housesBuiltProp = hbType.GetProperty("HousesBuilt");
                    if (housesBuiltProp == null || !(bool)housesBuiltProp.GetValue(houseBuilder))
                        break; // Houses not built yet, try again later

                    // Read LeftHouseSpawn (Red team) and RightHouseSpawn (Blue team)
                    var leftProp = hbType.GetProperty("LeftHouseSpawn");
                    var rightProp = hbType.GetProperty("RightHouseSpawn");
                    if (leftProp == null || rightProp == null)
                        break;

                    cachedRedHouseSpawn = (Point)leftProp.GetValue(houseBuilder);
                    cachedBlueHouseSpawn = (Point)rightProp.GetValue(houseBuilder);

                    if (cachedRedHouseSpawn.X <= 0 || cachedBlueHouseSpawn.X <= 0)
                        break;

                    teamStates["Red"].SpawnCenter = cachedRedHouseSpawn;
                    teamStates["Blue"].SpawnCenter = cachedBlueHouseSpawn;

                    cctgHousePositionsResolved = true;
                    TShock.Log.ConsoleInfo($"[TeamNPC] Resolved cctgPlugin house positions: Red=({cachedRedHouseSpawn.X},{cachedRedHouseSpawn.Y}), Blue=({cachedBlueHouseSpawn.X},{cachedBlueHouseSpawn.Y})");
                    return;
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"[TeamNPC] Error resolving cctgPlugin house positions: {ex.Message}");
            }
        }

        /// <summary>
        /// Find a safe spawn point near a house, offset ~10 tiles in the given direction.
        /// xOffset: negative = left, positive = right
        /// </summary>
        private Point FindSafeSpawnNearHouse(Point houseSpawn, int xOffset)
        {
            // Target position: house spawn + offset
            int targetX = houseSpawn.X + xOffset;
            int targetY = houseSpawn.Y;

            // Try the target position first
            if (IsSafeSpawnTile(targetX, targetY))
                return new Point(targetX, targetY);

            // Search in expanding radius around the target, staying away from blocks
            for (int radius = 1; radius <= 15; radius++)
            {
                // Prefer same direction as offset (further from house)
                int preferred = xOffset < 0 ? -radius : radius;
                if (IsSafeSpawnTile(targetX + preferred, targetY))
                    return new Point(targetX + preferred, targetY);

                // Then try the opposite direction
                int opposite = -preferred;
                if (IsSafeSpawnTile(targetX + opposite, targetY))
                    return new Point(targetX + opposite, targetY);

                // Then try with vertical variation
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dy == 0) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (IsSafeSpawnTile(targetX + dx, targetY + dy))
                            return new Point(targetX + dx, targetY + dy);
                    }
                }
            }

            // Fallback: return the original house spawn
            return houseSpawn;
        }

        /// <summary>
        /// Check if a tile position is safe for NPC spawning (2 tiles of air for NPC to stand)
        /// </summary>
        private bool IsSafeSpawnTile(int x, int y)
        {
            if (x < 0 || x >= Main.maxTilesX || y < 2 || y >= Main.maxTilesY)
                return false;

            // The NPC stands at (x, y) and (x, y-1) — both must be non-solid
            // The tile at (x, y+1) should be solid (ground to stand on)
            var feet = Main.tile[x, y];
            var head = Main.tile[x, y - 1];
            var ground = Main.tile[x, y + 1];

            bool feetClear = feet == null || !feet.active() || !Main.tileSolid[feet.type];
            bool headClear = head == null || !head.active() || !Main.tileSolid[head.type];
            bool hasGround = ground != null && ground.active() && Main.tileSolid[ground.type];

            return feetClear && headClear && hasGround;
        }

        private void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            spawnController.OnNpcSpawn(args);
        }

        private void OnNpcKilled(NpcKilledEventArgs args)
        {
            int npcIndex = args.npc.whoAmI;

            if (!npcTeamManager.IsNPCRegistered(npcIndex))
                return;

            string team = npcTeamManager.GetNPCTeam(npcIndex);
            string npcName = args.npc.TypeName;

            // Don't release house immediately - preserve assignment for respawn
            // teamStates[team].ReleaseHouse(npcName);

            // Unregister from team manager
            npcTeamManager.UnregisterNPC(npcIndex);

            // Save persistence data
            if (config.EnableHousingPersistence)
            {
                HousingPersistence.Save(teamStates);
            }

            if (config.EnableDebugLog)
            {
                TShock.Log.Info($"[TeamNPC] {npcName} (team: {team}) killed, house preserved for respawn");
            }

            // Condition checker will automatically respawn if conditions are still met
        }

        private void OnPlayerCommand(PlayerCommandEventArgs args)
        {
            if (args.Handled)
                return;

            // Check if this is an NPC-related command
            string command = args.CommandName.ToLower();
            if (command == "spawnmob" || command == "sm")
            {
                // Mark that a command was used, allowing next NPC spawn
                spawnController.MarkCommandUsed();
                Console.WriteLine($"[TeamNPC] Player {args.Player.Name} used /{command} - allowing next NPC spawn");
            }
        }

        private void OnUpdateNPCHome(object sender, GetDataHandlers.NPCHomeChangeEventArgs args)
        {
            if (args.HouseholdStatus == GetDataHandlers.HouseholdStatus.Homeless)
            {
                conditionChecker.RemovePlayerLockedHome(args.ID);
                return;
            }

            // Determine player team
            int playerTeam = args.Player?.TPlayer?.team ?? 0;
            string playerTeamName = playerTeam == 1 ? "Red" : playerTeam == 3 ? "Blue" : null;

            // Determine NPC team
            string npcTeamName = npcTeamManager.GetNPCTeam(args.ID);

            if (playerTeamName != null && npcTeamName != null && npcTeamName != playerTeamName)
            {
                // Player is assigning an enemy NPC — kick it out
                if (args.ID >= 0 && args.ID < Main.maxNPCs && Main.npc[args.ID].active)
                {
                    Main.npc[args.ID].homeless = true;
                    TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", args.ID);
                    TShock.Log.ConsoleInfo($"[TeamNPC] {args.Player.Name} tried to assign enemy {Main.npc[args.ID].TypeName}, kicked out");
                }
                args.Handled = true;
                return;
            }

            conditionChecker.SetPlayerLockedHome(args.ID, new Point(args.X, args.Y));
        }

    }
}
