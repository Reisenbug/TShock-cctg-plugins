using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace cctgPlugin
{
    [ApiVersion(2, 1)]
    public class CctgPlugin : TerrariaPlugin
    {
        public override string Name => "CctgPlugin";
        public override string Author => "stardust";
        public override string Description => "CCTG Plugin";
        public override Version Version => new Version(1, 0, 0);

        // Module instances
        private HouseBuilder houseBuilder = new HouseBuilder();
        private WorldPainter worldPainter = new WorldPainter();
        private BoundaryChecker boundaryChecker = new BoundaryChecker();
        private TeleportManager teleportManager = new TeleportManager();
        private RestrictItem restrictItem = new RestrictItem();
        private BiomeDetector biomeDetector = null;

        // Scoreboard update counter
        private int scoreboardUpdateCounter = 0;
        private const int SCOREBOARD_UPDATE_INTERVAL = 60; // Update every 60 frames (~1 second)

        // House mob clearing counter
        private int mobClearCounter = 0;
        private const int MOB_CLEAR_INTERVAL = 30; // Check every 30 frames (~0.5 seconds)

        // Item restriction check counter
        private int itemCheckCounter = 0;
        private const int ITEM_CHECK_INTERVAL = 10; // Check every 10 frames (~0.17 seconds)

        // Timer command counters
        private Dictionary<int, PlayerTimerState> playerTimerStates = new Dictionary<int, PlayerTimerState>();
        private int timerUpdateCounter = 0;
        private const int TIMER_UPDATE_INTERVAL = 60; // Update timer display every 1 second (60 frames)

        // Shop modification timer counters
        private Dictionary<int, ShopModificationState> shopModificationStates = new Dictionary<int, ShopModificationState>();
        private const int SHOP_MODIFICATION_INTERVAL = 30; // Every 30 frames (~0.5 seconds)
        private const int SHOP_MODIFICATION_DURATION = 120; // 2 seconds = 120 frames (at 60 FPS)

        // Furniture repair counter

        // Gem lock repair counter
        private int gemLockRepairCounter = 0;
        private const int GEM_LOCK_REPAIR_INTERVAL = 60; // Check every 60 frames (~1 second)

        // Gem pickup detection
        private Dictionary<int, GemPickupState> gemPickupStates = new Dictionary<int, GemPickupState>();
        private int gemPickupCheckCounter = 0;
        private const int GEM_PICKUP_CHECK_INTERVAL = 60; // Check every 60 frames (~1 second)

        // Gem tracking counter (shares the same 1s interval as pickup check)
        private int gemTrackingCheckCounter = 0;
        private const int GEM_TRACKING_CHECK_INTERVAL = 10;
        private const double GEM_GROUND_TIMEOUT_SECONDS = 30.0; // 30 seconds on ground = auto return

        // Game state
        private bool gameStarted = false;
        private bool pvpEnabled = false;           // PVP enabled after first day
        private bool crossingAllowed = false;      // Players can cross sides after first day
        private int gameDayCount = 0;              // Days since game started
        private bool wasNightLastCheck = false;    // Track day/night transition
        private bool broadcast330Sent = false;     // 3:30 warning sent
        private bool broadcast430Sent = false;     // 4:30 crossing allowed sent
        private bool broadcast1700Sent = false;    // 5:00 PM sudden death warning sent
        private bool broadcast1800Sent = false;    // 6:00 PM sudden death enabled sent
        private bool broadcast2000Sent = false;    // 8:00 PM draw warning (2 min)
        private bool broadcast2100Sent = false;    // 9:00 PM draw warning (1 min)
        private bool broadcast2200Sent = false;    // 10:00 PM game ends in draw

        // Auto-cycle state
        private enum CycleState { Idle, Generating, WaitingConfirm, Swapping, StartPending }
        private CycleState _cycleState = CycleState.Idle;
        private DateTime _cycleStateTime;
        private Queue<string> _worldQueue = new Queue<string>();
        private string _generatingFilename;
        private string _prevWorldPath;
        private const int WORLD_QUEUE_TARGET = 40;
        private string _worldQueueFile => Path.Combine(TShock.SavePath, "cctg_world_queue.txt");
        private bool _cycleCancelled;
        private bool _confirmBroadcasted;
        private bool _confirmBroadcasted2;
        private bool _startCountdown;
        private DateTime _startCountdownTime;
        private bool _suddenDeathMode;
        private Point _spectatorBox = new Point(-1, -1);
        private HashSet<int> _pendingSpectators = new HashSet<int>();
        private Dictionary<int, int> _delayedSpectators = new Dictionary<int, int>();

        // Hook drop on death
        private static readonly HashSet<int> HookItemIds = new HashSet<int>
        {
            84, 1236, 4759, 1237, 1238, 1239, 1240, 4257, 1241,
            939, 1273, 2585, 2360, 185, 1800, 1915, 437, 4980, 3022, 3023, 3020
        };
        private static readonly HashSet<int> MountItemIds = new HashSet<int>
        {
            5600, 5640, 5641, 5642,
            5665, 5666,
            5525,
            5510
        };
        private static readonly Dictionary<int, int[]> MountBuffDebuffs = new Dictionary<int, int[]>
        {
            { 378, new[] { 196, 30, 32, 24 } },
            { 379, new[] { 196, 30, 32, 24 } },
            { 380, new[] { 196, 30, 32, 24 } },
            { 381, new[] { 196, 30, 32, 24 } },
            { 387, new[] { 23, 30, 32, 24 } },
            { 388, new[] { 23, 30, 32, 24 } },
            { 374, new[] { 23, 30, 32, 36, 195 } },
            { 370, new[] { 32 } },
        };
        private Dictionary<int, List<HookDropState>> hookDropStates = new Dictionary<int, List<HookDropState>>();
        private int hookDropCheckCounter = 0;
        private const int HOOK_DROP_CHECK_INTERVAL = 30;
        private int hookDropProjCounter = 0;
        private const int HOOK_DROP_PROJ_INTERVAL = 120;
        private static readonly int[] HOOK_DROP_PROJECTILE_IDS = { 511, 512, 513 };

        private Dictionary<int, DateTime> pendingJoinAssignments = new Dictionary<int, DateTime>();

        public CctgPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            // Register events
            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInitialize);

            // Register Tile edit event to protect houses
            GetDataHandlers.TileEdit += OnTileEdit;
            GetDataHandlers.GemLockToggle += OnGemLockToggle;

            GetDataHandlers.NpcTalk += OnNPCTalk;
            GetDataHandlers.KillMe += OnPlayerKillMe;

            // Register network data event to listen for team changes
            ServerApi.Hooks.NetGetData.Register(this, OnGetData);

            // Register game update event for delayed teleport handling
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

            // Register player join event
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnPlayerJoin);

            // Register commands
            Commands.ChatCommands.Add(new Command(PaintWorldCommand, "paintworld"));
            Commands.ChatCommands.Add(new Command(BuildHousesCommand, "buildhouses"));
            Commands.ChatCommands.Add(new Command(StartCommand, "start"));
            Commands.ChatCommands.Add(new Command(EndCommand, "end"));
            Commands.ChatCommands.Add(new Command(DebugBoundaryCommand, "debugbound"));
            Commands.ChatCommands.Add(new Command(DebugItemCommand, "debugitem"));
            Commands.ChatCommands.Add(new Command(DebugBiomeCommand, "debugbiome"));
            Commands.ChatCommands.Add(new Command(DebugShopCommand, "debugshop"));
            Commands.ChatCommands.Add(new Command(TimerCommand, "t"));
            Commands.ChatCommands.Add(new Command(CancelNextRound, "n"));
            Commands.ChatCommands.Add(new Command(ResumeNextRound, "next"));
            Commands.ChatCommands.Add(new Command(StartNextCommand, "startnext"));

                    }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Deregister events
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInitialize);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnPlayerJoin);
                GetDataHandlers.TileEdit -= OnTileEdit;
                GetDataHandlers.GemLockToggle -= OnGemLockToggle;
                GetDataHandlers.NpcTalk -= OnNPCTalk;
                GetDataHandlers.KillMe -= OnPlayerKillMe;
            }
            base.Dispose(disposing);
        }

        private void OnPostInitialize(EventArgs args)
        {
            // Initialize BiomeDetector after world is loaded
            biomeDetector = new BiomeDetector();
            RestoreWorldQueue();
        }

        private void RestoreWorldQueue()
        {
            try
            {
                string worldDir = Main.WorldPath;
                string currentWorld = Main.worldPathName ?? "";
                var hexPattern = new System.Text.RegularExpressions.Regex(@"^[0-9a-f]{8}$");

                // Step 1: restore from queue file (preserves order)
                var ordered = new List<string>();
                if (File.Exists(_worldQueueFile))
                {
                    foreach (var line in File.ReadAllLines(_worldQueueFile))
                    {
                        string filename = line.Trim();
                        if (string.IsNullOrEmpty(filename)) continue;
                        string wldPath = Path.Combine(worldDir, filename + ".wld");
                        if (File.Exists(wldPath) && new FileInfo(wldPath).Length > 0)
                            ordered.Add(filename);
                    }
                }

                // Step 2: scan disk for any generated worlds not in the queue file
                if (Directory.Exists(worldDir))
                {
                    var inQueue = new HashSet<string>(ordered, StringComparer.OrdinalIgnoreCase);
                    foreach (var file in Directory.GetFiles(worldDir, "*.wld"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (!hexPattern.IsMatch(name)) continue;
                        if (string.Equals(file, currentWorld, StringComparison.OrdinalIgnoreCase)) continue;
                        if (inQueue.Contains(name)) continue;
                        if (new FileInfo(file).Length > 0)
                        {
                            ordered.Add(name);
                            inQueue.Add(name);
                        }
                    }
                }

                foreach (var f in ordered)
                    _worldQueue.Enqueue(f);

                TShock.Log.ConsoleInfo($"[CCTG] Restored {_worldQueue.Count} world(s) into queue");
                if (_worldQueue.Count > 0)
                    SaveWorldQueue();
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[CCTG] RestoreWorldQueue failed: {ex.Message}");
            }
        }

        private void SaveWorldQueue()
        {
            try
            {
                File.WriteAllLines(_worldQueueFile, _worldQueue);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[CCTG] SaveWorldQueue failed: {ex.Message}");
            }
        }


        // Command: Manually trigger world painting
        private void PaintWorldCommand(CommandArgs args)
        {
            args.Player.SendInfoMessage("Starting to paint world...");
            worldPainter.PaintWorld();
            args.Player.SendSuccessMessage("Painting complete!");
        }

        // Command: Manually build houses
        private void BuildHousesCommand(CommandArgs args)
        {
            args.Player.SendInfoMessage("Starting to build houses...");
            houseBuilder.BuildHouses();
            args.Player.SendSuccessMessage("House building complete!");
        }

        // Command: Start game
        private void StartCommand(CommandArgs args)
        {
            if (gameStarted)
            {
                args.Player.SendErrorMessage("Game already started, cannot start again!");
                return;
            }

            // 1. Build houses
            houseBuilder.BuildHouses();

            // 1.5. Place gem locks
            houseBuilder.PlaceGemLocks();

            for (int i = 0; i < Main.maxItems; i++)
            {
                if (Main.item[i] != null && Main.item[i].active)
                {
                    Main.item[i].TurnToAir();
                    TSPlayer.All.SendData(PacketTypes.ItemDrop, "", i);
                }
            }

            int boxX = Main.maxTilesX - 42 - 25;
            int boxY = (int)Main.worldSurface - 160;

            // Ensure spectator box is above any liquid (water/lava/honey)
            for (int checkY = boxY + 5; checkY >= 50; checkY--)
            {
                bool hasLiquid = false;
                for (int checkX = boxX; checkX <= boxX + 5; checkX++)
                {
                    if (checkX >= 0 && checkX < Main.maxTilesX && checkY >= 0 && checkY < Main.maxTilesY)
                    {
                        if (Main.tile[checkX, checkY].liquid > 0)
                        {
                            hasLiquid = true;
                            break;
                        }
                    }
                }
                if (!hasLiquid)
                {
                    boxY = checkY - 6;
                    break;
                }
            }

            _spectatorBox = new Point(boxX, boxY);
            BuildSpectatorBox(boxX, boxY);

            // 2. Paint world
            worldPainter.PaintWorld();

            // 3. Replace Starfury with Enchanted Sword in Skyland Chests
            restrictItem.ReplaceStarfuryInSkylandChests();

            // 4. Set time to 10:30
            SetTime(10, 30);

            // 5. Reset player inventory/stats (except players with ignoressc permission)
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    // Clear all buffs
                    for (int b = 0; b < Terraria.Player.maxBuffs; b++)
                    {
                        player.TPlayer.buffType[b] = 0;
                        player.TPlayer.buffTime[b] = 0;
                        player.SendData(PacketTypes.PlayerBuff, "", player.Index, b);
                    }

                    // Check if player has ignoressc permission
                    if (!player.HasPermission("ignoressc"))
                    {
                        ResetPlayerInventoryAndStats(player);
                    }
                }
            }

            // 6. Balanced team assignment via Fisher-Yates shuffle
            var activePlayers = new List<TSPlayer>();
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                    activePlayers.Add(player);
            }

            Random random = new Random();
            for (int i = activePlayers.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var temp = activePlayers[i];
                activePlayers[i] = activePlayers[j];
                activePlayers[j] = temp;
            }

            int half = activePlayers.Count / 2;
            for (int i = 0; i < activePlayers.Count; i++)
            {
                var player = activePlayers[i];
                int team = i < half ? 1 : 3;
                player.SetTeam(team);

                string teamName = team == 1 ? "Red Team" : "Blue Team";
                player.SendSuccessMessage($"You have been assigned to {teamName}!");

                if (!teleportManager.PlayerTeamStates.ContainsKey(player.Index))
                {
                    teleportManager.PlayerTeamStates[player.Index] = new PlayerTeamState();
                }

                var state = teleportManager.PlayerTeamStates[player.Index];
                state.LastTeam = team;
                state.LastTeamChangeTime = DateTime.Now;

            }

            gameStarted = true;
            pvpEnabled = true;
            crossingAllowed = false;
            gameDayCount = 0;
            _suddenDeathMode = false;
            _pendingSpectators.Clear();
            _delayedSpectators.Clear();
            hookDropStates.Clear();
            wasNightLastCheck = false;
            broadcast330Sent = false;
            broadcast430Sent = false;
            broadcast1700Sent = false;
            broadcast1800Sent = false;
            broadcast2000Sent = false;
            broadcast2100Sent = false;
            broadcast2200Sent = false;

            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    player.TPlayer.hostile = true;
                    TSPlayer.All.SendData(PacketTypes.TogglePvp, "", player.Index);
                }
            }

            // Start boundary checking
            boundaryChecker.StartBoundaryCheck();

            // Re-detect biome layout for current world and send info
            biomeDetector = new BiomeDetector();
            string biomeInfo = biomeDetector.GetBiomeInfoMessage();
            TSPlayer.All.SendInfoMessage($"Biome Layout: {biomeInfo}");

            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    player.SetBuff(149, 600);
                }
            }
            TSPlayer.All.SendMessage("Cctg is about to start!", 255, 105, 180);
            _startCountdown = true;
            _startCountdownTime = DateTime.Now;

            TryStartNextGeneration();
        }

        // Command: End game
        private void EndCommand(CommandArgs args)
        {
            EndGame();
        }

        private void StartNextCommand(CommandArgs args)
        {
            if (gameStarted)
                EndGame();

            TryStartNextGeneration();
            args.Player.SendSuccessMessage($"Generating next world, queue={_worldQueue.Count}/{WORLD_QUEUE_TARGET}...");
        }

        // End game logic (called by /end command and auto-draw)
        private void EndGame()
        {
            // 1. Set time to 10:30
            SetTime(10, 30);

            // 2. Clear all NPCs (monsters and bosses)
            int killedCount = 0;
            for (int i = 0; i < Main.npc.Length; i++)
            {
                if (Main.npc[i].active && !Main.npc[i].townNPC)
                {
                    Main.npc[i].active = false;
                    Main.npc[i].type = 0;
                    TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                    killedCount++;
                }
            }
            // 3. Clear houses
            if (houseBuilder.HousesBuilt)
            {
                houseBuilder.ClearHouses();
            }

            ClearSpectatorBox();

            // 4. Reset game state
            gameStarted = false;
            pvpEnabled = false;
            crossingAllowed = false;
            gameDayCount = 0;
            _suddenDeathMode = false;
            _pendingSpectators.Clear();
            _delayedSpectators.Clear();
            hookDropStates.Clear();
            wasNightLastCheck = false;
            broadcast330Sent = false;
            broadcast430Sent = false;
            broadcast1700Sent = false;
            broadcast1800Sent = false;
            broadcast2000Sent = false;
            broadcast2100Sent = false;
            broadcast2200Sent = false;

            // Clear gem pickup states
            gemPickupStates.Clear();

            // Disable PVP for all players
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active)
                {
                    player.TPlayer.hostile = false;
                    TSPlayer.All.SendData(PacketTypes.TogglePvp, "", player.Index);
                }
            }

            // Stop boundary checking
            boundaryChecker.StopBoundaryCheck();

            // Clear teleport manager states
            teleportManager.ClearAllStates();

            TSPlayer.All.SendSuccessMessage("════════════════════════════");
            TSPlayer.All.SendSuccessMessage("       Game Ended!         ");
            TSPlayer.All.SendSuccessMessage("════════════════════════════");

            TShock.Log.ConsoleInfo("[CCTG] Game ended!");

            // Start auto-cycle countdown
            _cycleCancelled = false;
            _confirmBroadcasted = false;
            _confirmBroadcasted2 = false;
            _cycleStateTime = DateTime.Now;

            if (_worldQueue.Count > 0)
            {
                _cycleState = CycleState.WaitingConfirm;
                TShock.Log.ConsoleInfo($"[CCTG] World ready (queue={_worldQueue.Count}), starting countdown...");
            }
            else if (_cycleState == CycleState.Generating || WorldGenPlugin.WorldGenPlugin.Instance?.IsGenerating == true)
            {
                _cycleState = CycleState.Generating;
                TShock.Log.ConsoleInfo($"[CCTG] Queue empty, still generating, will auto-proceed when ready...");
            }
            else
            {
                TryStartNextGeneration();
            }
        }

        private void TryStartNextGeneration()
        {
            if (_cycleState == CycleState.Generating)
                return;
            if (_cycleState == CycleState.WaitingConfirm || _cycleState == CycleState.Swapping || _cycleState == CycleState.StartPending)
                return;
            if (!string.IsNullOrEmpty(_generatingFilename))
            {
                string pendingPath = Path.Combine(Main.WorldPath, _generatingFilename + ".wld");
                if (File.Exists(pendingPath) && new FileInfo(pendingPath).Length > 0)
                {
                    _worldQueue.Enqueue(_generatingFilename);
                    _generatingFilename = null;
                    SaveWorldQueue();
                }
                else
                {
                    return;
                }
            }
            if (_worldQueue.Count >= WORLD_QUEUE_TARGET)
                return;
            if (WorldGenPlugin.WorldGenPlugin.Instance?.IsGenerating == true)
            {
                _cycleState = CycleState.Generating;
                _cycleStateTime = DateTime.Now;
                return;
            }
            _generatingFilename = Guid.NewGuid().ToString("N").Substring(0, 8);
            Commands.HandleCommand(TSPlayer.Server, $"/genworld {_generatingFilename} medium");
            _cycleState = CycleState.Generating;
            _cycleStateTime = DateTime.Now;
            TShock.Log.ConsoleInfo($"[CCTG] Generating world {_generatingFilename} (queue={_worldQueue.Count}/{WORLD_QUEUE_TARGET})");
        }

        private void CancelNextRound(CommandArgs args)
        {
            if (_cycleState == CycleState.WaitingConfirm)
            {
                _cycleCancelled = true;
                _cycleState = CycleState.Idle;
                TSPlayer.All.SendInfoMessage("Next round cancelled.");
                TShock.Log.ConsoleInfo("[CCTG] Auto-cycle cancelled by " + args.Player.Name);
            }
            else
            {
                args.Player.SendErrorMessage("No pending round to cancel.");
            }
        }

        private void ResumeNextRound(CommandArgs args)
        {
            if (!_cycleCancelled)
            {
                args.Player.SendErrorMessage("No cancelled round to resume.");
                return;
            }
            if (_worldQueue.Count == 0)
            {
                args.Player.SendErrorMessage("Next world file not ready yet.");
                return;
            }
            _cycleCancelled = false;
            _confirmBroadcasted = false;
            _confirmBroadcasted2 = false;
            _cycleState = CycleState.WaitingConfirm;
            _cycleStateTime = DateTime.Now;
            TSPlayer.All.SendInfoMessage("Resuming next round countdown...");
            TShock.Log.ConsoleInfo("[CCTG] Auto-cycle resumed by " + args.Player.Name);
        }

        // Debug command: Check boundary detection status
        private void DebugBoundaryCommand(CommandArgs args)
        {
            var player = args.Player;
            string debugInfo = boundaryChecker.GetDebugInfo(player);
            player.SendInfoMessage(debugInfo);
        }

        // Debug command: Check biome layout
        private void DebugBiomeCommand(CommandArgs args)
        {
            var player = args.Player;

            if (biomeDetector == null)
            {
                player.SendErrorMessage("BiomeDetector not initialized yet. Please wait for world to load.");
                return;
            }

            string detailedInfo = biomeDetector.GetDetailedBiomeInfo();
            player.SendInfoMessage(detailedInfo);

            string simpleInfo = biomeDetector.GetBiomeInfoMessage();
            player.SendSuccessMessage($"Biome Layout: {simpleInfo}");

        }

        // Debug command: Check Demolitionist shop items
        private void DebugShopCommand(CommandArgs args)
        {
            var player = args.Player;

            player.SendInfoMessage("Checking Demolitionist shop items...");

            // Call the debug method from RestrictItem
            restrictItem.DebugDemolitionistShop();

            player.SendSuccessMessage("Demolitionist shop debug completed. Check console for details.");
        }

        // Timer command: Start/stop player timer
        private void TimerCommand(CommandArgs args)
        {
            var player = args.Player;
            if (player == null)
                return;

            // Get or create timer state for this player
            if (!playerTimerStates.ContainsKey(player.Index))
            {
                playerTimerStates[player.Index] = new PlayerTimerState();
            }

            var timerState = playerTimerStates[player.Index];

            if (timerState.IsTimerActive)
            {
                // Calculate final time before stopping
                double finalTime = (DateTime.Now - timerState.StartTime).TotalSeconds;

                // Stop the timer
                timerState.IsTimerActive = false;
                playerTimerStates.Remove(player.Index);

                player.SendSuccessMessage($"Timer stopped! Total time: {FormatTime(finalTime)}");
                TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} stopped timer. Total time: {FormatTime(finalTime)}");
            }
            else
            {
                // Start the timer
                timerState.IsTimerActive = true;
                timerState.StartTime = DateTime.Now;
                timerState.TotalSeconds = 0;
                timerState.PlayerIndex = player.Index;

                player.SendSuccessMessage("Timer started! Use /t again to stop.");
                TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} started timer");
            }
        }

        // Format time display (HH:MM:SS format)
        private string FormatTime(double seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        
        // Update active timers display
        private void UpdateActiveTimers()
        {
            // Clean up timer states for disconnected players first
            List<int> playersToRemove = new List<int>();
            foreach (var kvp in playerTimerStates)
            {
                int playerIndex = kvp.Key;
                var player = TShock.Players[playerIndex];

                if (player == null || !player.Active)
                {
                    var timerState = kvp.Value;
                    if (timerState.IsTimerActive)
                    {
                        // Calculate final time
                        double finalSeconds = (DateTime.Now - timerState.StartTime).TotalSeconds;

                        // Send message to all players (if player name is available)
                        string playerName = player?.Name ?? $"Player{playerIndex}";
                        TSPlayer.All.SendInfoMessage($"Timer stopped for {playerName}! Total time: {FormatTime(finalSeconds)} (Player left)");
                        TShock.Log.ConsoleInfo($"[CCTG] Player {playerName} left, timer stopped. Total time: {FormatTime(finalSeconds)}");
                    }

                    playersToRemove.Add(playerIndex);
                }
            }

            // Remove timer states for disconnected players
            foreach (int playerIndex in playersToRemove)
            {
                playerTimerStates.Remove(playerIndex);
            }

            // Update active timers
            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active)
                    continue;

                if (playerTimerStates.ContainsKey(player.Index))
                {
                    var timerState = playerTimerStates[player.Index];
                    if (timerState.IsTimerActive)
                    {
                        // Calculate elapsed time
                        double elapsedSeconds = (DateTime.Now - timerState.StartTime).TotalSeconds;
                        timerState.TotalSeconds = elapsedSeconds;

                        // Send timer update to player every 30 seconds (to avoid spam)
                        if ((int)elapsedSeconds % 30 == 0 && elapsedSeconds > 0)
                        {
                            player.SendInfoMessage($"Timer: {FormatTime(elapsedSeconds)}");
                        }
                    }
                }
            }
        }

        // Debug command: Check item restriction status
        private void DebugItemCommand(CommandArgs args)
        {
            var player = args.Player;
            player.SendInfoMessage("=== Item Restriction Debug ===");
            player.SendInfoMessage("Showing ALL items in inventory:");

            int totalItems = 0;
            int restrictedCount = 0;

            for (int i = 0; i < player.TPlayer.inventory.Length; i++)
            {
                var item = player.TPlayer.inventory[i];
                if (item != null && !item.IsAir)
                {
                    totalItems++;
                    string itemInfo = $"Slot {i}: {item.Name} (ID: {item.type}) x{item.stack}";

                    if (item.type == 61 || item.type == 836)
                    {
                        player.SendWarningMessage(itemInfo + " - RESTRICTED!");
                        restrictedCount++;
                    }
                    else if (item.Name.Contains("stone") || item.Name.Contains("Stone"))
                    {
                        player.SendInfoMessage(itemInfo + " - Contains 'stone'");
                    }
                    else
                    {
                        player.SendInfoMessage(itemInfo);
                    }
                }
            }

            player.SendInfoMessage($"Total items: {totalItems}, Restricted: {restrictedCount}");
            player.SendInfoMessage("Triggering manual check...");
            restrictItem.CheckAndRemoveRestrictedItems(player);
            player.SendSuccessMessage("Manual check complete!");

        }

        // Set game time
        private void SetTime(int hour, int minute)
        {
            // Calculate time (game time starts at 4:30 AM)
            double targetMinutes = hour * 60 + minute;
            double startMinutes = 4 * 60 + 30; // 4:30 AM

            if (targetMinutes < startMinutes)
                targetMinutes += 24 * 60; // Add a day

            double gameMinutes = targetMinutes - startMinutes;
            Main.time = gameMinutes * 60; // Convert to game ticks (1 minute = 60 ticks)

            // Set day/night
            Main.dayTime = hour >= 4 && hour < 19; // Day: 4:30-19:30

            // Sync to all players
            TSPlayer.All.SendData(PacketTypes.TimeSet, "", 0, 0, Main.sunModY, Main.moonModY);
        }

        // Check game time events (PVP, crossing, broadcasts)
        private void CheckGameTimeEvents()
        {
            // Detect day -> night transition to count days
            bool isNight = !Main.dayTime;
            if (isNight && !wasNightLastCheck)
            {
                // Just transitioned to night
                gameDayCount++;
                TShock.Log.ConsoleInfo($"[CCTG] Day {gameDayCount} ended, transitioning to night");
            }
            wasNightLastCheck = isNight;

            // Calculate current game time in hours:minutes
            // Day: Main.time from 0 (4:30 AM) to 54000 (7:30 PM)
            // Night: Main.time from 0 (7:30 PM) to 32400 (4:30 AM)
            double currentHour, currentMinute;
            if (Main.dayTime)
            {
                // Day time: 4:30 AM to 7:30 PM (54000 ticks = 15 hours)
                double dayMinutes = Main.time / 60.0;
                double totalMinutes = 4 * 60 + 30 + dayMinutes; // Start from 4:30 AM
                currentHour = Math.Floor(totalMinutes / 60.0);
                currentMinute = totalMinutes % 60;
            }
            else
            {
                // Night time: 7:30 PM to 4:30 AM (32400 ticks = 9 hours)
                double nightMinutes = Main.time / 60.0;
                double totalMinutes = 19 * 60 + 30 + nightMinutes; // Start from 7:30 PM
                if (totalMinutes >= 24 * 60) totalMinutes -= 24 * 60;
                currentHour = Math.Floor(totalMinutes / 60.0);
                currentMinute = totalMinutes % 60;
            }

            // Pink color for broadcasts
            byte r = 255, g = 105, b = 180; // Pink (Hot Pink)

            // Day 2 events (gameDayCount >= 1 means first night has passed)
            if (gameDayCount >= 1)
            {
                // 3:30 AM warning (during night, before dawn)
                if (!broadcast330Sent && !Main.dayTime && currentHour == 3 && currentMinute >= 30)
                {
                    broadcast330Sent = true;
                    TSPlayer.All.SendMessage("1 minute till everyone can cross the other side!", r, g, b);
                    TShock.Log.ConsoleInfo("[CCTG] Broadcast: 1 minute till crossing allowed");
                }

                // 4:30 AM - Enable PVP and crossing
                if (!broadcast430Sent && Main.dayTime && Main.time < 60) // Just after dawn
                {
                    broadcast430Sent = true;
                    pvpEnabled = true;
                    crossingAllowed = true;

                    // Enable PVP for all players
                    foreach (var player in TShock.Players)
                    {
                        if (player != null && player.Active)
                        {
                            player.TPlayer.hostile = true;
                            TSPlayer.All.SendData(PacketTypes.TogglePvp, "", player.Index);
                        }
                    }

                    TSPlayer.All.SendMessage("Everyone can cross sides now!", r, g, b);
                    TShock.Log.ConsoleInfo("[CCTG] Broadcast: Crossing and PVP enabled");

                    foreach (var p in TShock.Players)
                    {
                        if (p != null && p.Active && (p.Team == 1 || p.Team == 3))
                        {
                            p.GiveItem(2350, 1);
                        }
                    }

                    // Stop boundary checking
                    boundaryChecker.StopBoundaryCheck();
                }

                // 5:00 PM (17:00) - Sudden death warning
                if (!broadcast1700Sent && Main.dayTime && currentHour == 17 && currentMinute >= 0)
                {
                    broadcast1700Sent = true;
                    TSPlayer.All.SendMessage("Sudden death mode will be opened in 1 minute!", r, g, b);
                    TShock.Log.ConsoleInfo("[CCTG] Broadcast: Sudden death warning");
                }

                // 6:00 PM (18:00) - Sudden death enabled
                if (!broadcast1800Sent && Main.dayTime && currentHour == 18 && currentMinute >= 0)
                {
                    broadcast1800Sent = true;
                    TSPlayer.All.SendMessage("Sudden death mode is opened!", r, g, b);
                    TShock.Log.ConsoleInfo("[CCTG] Broadcast: Sudden death mode enabled");
                    _suddenDeathMode = true;
                }

                // Draw events only on second night (gameDayCount >= 2)
                // gameDayCount increments at each day→night transition:
                //   First night (after start) = 1, Second night = 2
                if (gameDayCount >= 2)
                {
                    // 8:00 PM (20:00) - Draw warning 2 minutes
                    if (!broadcast2000Sent && !Main.dayTime && currentHour == 20 && currentMinute >= 0)
                    {
                        broadcast2000Sent = true;
                        TSPlayer.All.SendMessage("The game will end in a draw in 2 minutes.", r, g, b);
                        TShock.Log.ConsoleInfo("[CCTG] Broadcast: Draw warning 2 minutes");
                    }

                    // 9:00 PM (21:00) - Draw warning 1 minute
                    if (!broadcast2100Sent && !Main.dayTime && currentHour == 21 && currentMinute >= 0)
                    {
                        broadcast2100Sent = true;
                        TSPlayer.All.SendMessage("The game will end in a draw in 1 minute.", r, g, b);
                        TShock.Log.ConsoleInfo("[CCTG] Broadcast: Draw warning 1 minute");
                    }

                    // 10:00 PM (22:00) - Game ends in draw
                    if (!broadcast2200Sent && !Main.dayTime && currentHour == 22 && currentMinute >= 0)
                    {
                        broadcast2200Sent = true;
                        TSPlayer.All.SendMessage("The game has ended in a draw!", r, g, b);
                        TShock.Log.ConsoleInfo("[CCTG] Broadcast: Game ended in draw");
                        EndGame();
                    }
                }
            }
        }

        private void OnPlayerKillMe(object sender, GetDataHandlers.KillMeEventArgs args)
        {
            if (!gameStarted)
                return;

            var player = args.Player;
            int playerTeam = player.TPlayer.team;

            if (playerTeam != 1 && playerTeam != 3)
                return;


            if (crossingAllowed && args.Pvp)
            {
                var droppedItems = new List<HookDropItem>();

                for (int i = 0; i < NetItem.InventorySlots; i++)
                {
                    var item = player.TPlayer.inventory[i];
                    if (item != null && item.stack > 0 && (HookItemIds.Contains(item.type) || MountItemIds.Contains(item.type)))
                    {
                        droppedItems.Add(new HookDropItem { Type = item.type, Stack = item.stack });
                        item.SetDefaults(0);
                        player.SendData(PacketTypes.PlayerSlot, "", player.Index, i);
                    }
                }

                for (int i = 0; i < NetItem.ArmorSlots; i++)
                {
                    var item = player.TPlayer.armor[i];
                    if (item != null && item.stack > 0 && (HookItemIds.Contains(item.type) || MountItemIds.Contains(item.type)))
                    {
                        droppedItems.Add(new HookDropItem { Type = item.type, Stack = item.stack });
                        item.SetDefaults(0);
                        player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + i);
                    }
                }

                for (int i = 0; i < NetItem.MiscEquipSlots; i++)
                {
                    var item = player.TPlayer.miscEquips[i];
                    if (item != null && item.stack > 0 && (HookItemIds.Contains(item.type) || MountItemIds.Contains(item.type)))
                    {
                        droppedItems.Add(new HookDropItem { Type = item.type, Stack = item.stack });
                        item.SetDefaults(0);
                        player.SendData(PacketTypes.PlayerSlot, "", player.Index,
                            NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + i);
                    }
                }

                if (droppedItems.Count > 0)
                {
                    var state = new HookDropState
                    {
                        DeathX = player.TPlayer.position.X,
                        DeathY = player.TPlayer.position.Y,
                        DroppedHooks = droppedItems,
                        DropTime = DateTime.Now
                    };
                    if (!hookDropStates.ContainsKey(player.Index))
                        hookDropStates[player.Index] = new List<HookDropState>();
                    hookDropStates[player.Index].Add(state);

                    player.SendMessage("You dropped your hooks/mounts! Return to where you died to get them back.", 255, 200, 0);
                    TShock.Log.ConsoleInfo($"[CCTG] {player.Name} died and dropped {droppedItems.Count} item stack(s) at ({state.DeathX / 16f:F0},{state.DeathY / 16f:F0}), total drops={hookDropStates[player.Index].Count}");
                }
            }

            if (!_suddenDeathMode)
                return;

            string deadTeamName = playerTeam == 1 ? "Red" : "Blue";
            _pendingSpectators.Add(player.Index);

            CheckTeamElimination(playerTeam, deadTeamName);
        }

        private void SendHookDropProjectile(TSPlayer player, HookDropState state)
        {
            int baseTileX = (int)(state.DeathX / 16f);
            int baseTileY = (int)(state.DeathY / 16f);
            int baseProj = 900 + (state.Id % 10) * 9;
            int idx = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -2; dy <= 0; dy++)
                {
                    int projIndex = baseProj + idx;
                    float pixelX = (baseTileX + dx) * 16f + 8f;
                    float pixelY = (baseTileY + dy) * 16f + 8f;
                    int projType = HOOK_DROP_PROJECTILE_IDS[idx % HOOK_DROP_PROJECTILE_IDS.Length];
                    using (var ms = new MemoryStream())
                    using (var writer = new BinaryWriter(ms))
                    {
                        writer.Write((short)0);
                        writer.Write((byte)PacketTypes.ProjectileNew);
                        writer.Write((short)projIndex);
                        writer.Write(pixelX);
                        writer.Write(pixelY);
                        writer.Write(0f);
                        writer.Write(0f);
                        writer.Write((byte)255);
                        writer.Write((short)projType);
                        writer.Write((byte)0);
                        long len = ms.Position;
                        ms.Position = 0;
                        writer.Write((short)len);
                        player.SendRawData(ms.ToArray());
                    }
                    idx++;
                }
            }
        }

        private void KillHookDropProjectile(TSPlayer player, HookDropState state)
        {
            int baseProj = 900 + (state.Id % 10) * 9;
            for (int i = 0; i < 9; i++)
            {
                int projIndex = baseProj + i;
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    writer.Write((short)0);
                    writer.Write((byte)PacketTypes.ProjectileNew);
                    writer.Write((short)projIndex);
                    writer.Write(0f);
                    writer.Write(0f);
                    writer.Write(0f);
                    writer.Write(0f);
                    writer.Write((byte)255);
                    writer.Write((short)0);
                    writer.Write((byte)0);
                    long len = ms.Position;
                    ms.Position = 0;
                    writer.Write((short)len);
                    player.SendRawData(ms.ToArray());
                }
            }
        }

        private void CheckTeamElimination(int deadTeam, string deadTeamName)
        {
            bool anyAlive = false;
            foreach (var p in TShock.Players)
            {
                if (p != null && p.Active && p.TPlayer.team == deadTeam && !_pendingSpectators.Contains(p.Index))
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive)
            {
                TSPlayer.All.SendMessage($"All the players in {deadTeamName} team are dead!", 255, 105, 180);
                string winnerTeam = deadTeam == 1 ? "Blue" : "Red";
                TSPlayer.All.SendMessage($"{winnerTeam} team won the game!", 255, 105, 180);
                EndGame();
            }
        }

        private void MoveToSpectator(TSPlayer player)
        {
            player.SetTeam(4);

            for (int i = 0; i < NetItem.InventorySlots; i++)
                player.TPlayer.inventory[i].SetDefaults(0);
            for (int i = 0; i < NetItem.ArmorSlots; i++)
                player.TPlayer.armor[i].SetDefaults(0);
            for (int i = 0; i < NetItem.DyeSlots; i++)
                player.TPlayer.dye[i].SetDefaults(0);
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
            {
                player.TPlayer.miscEquips[i].SetDefaults(0);
                player.TPlayer.miscDyes[i].SetDefaults(0);
            }

            for (int i = 0; i < NetItem.InventorySlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, i);
            for (int i = 0; i < NetItem.ArmorSlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + i);
            for (int i = 0; i < NetItem.DyeSlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + i);
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + i);
            for (int i = 0; i < NetItem.MiscDyeSlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + NetItem.MiscEquipSlots + i);

            player.Teleport((_spectatorBox.X + 2) * 16f, (_spectatorBox.Y + 1) * 16f);
            player.GiveItem(5644, 1);
            player.SendMessage("You are in spectate mode. You can use Scrying Orb to spectate other players.", 255, 105, 180);
        }

        private void ClearPlayerInventory(TSPlayer player)
        {
            for (int i = 0; i < NetItem.InventorySlots; i++)
                player.TPlayer.inventory[i].SetDefaults(0);
            for (int i = 0; i < NetItem.ArmorSlots; i++)
                player.TPlayer.armor[i].SetDefaults(0);
            for (int i = 0; i < NetItem.DyeSlots; i++)
                player.TPlayer.dye[i].SetDefaults(0);
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
            {
                player.TPlayer.miscEquips[i].SetDefaults(0);
                player.TPlayer.miscDyes[i].SetDefaults(0);
            }

            for (int i = 0; i < NetItem.InventorySlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, i);
            for (int i = 0; i < NetItem.ArmorSlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + i);
            for (int i = 0; i < NetItem.DyeSlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + i);
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + i);
            for (int i = 0; i < NetItem.MiscDyeSlots; i++)
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + NetItem.MiscEquipSlots + i);
        }

        private void BuildSpectatorBox(int x, int y)
        {
            for (int tx = x; tx <= x + 5; tx++)
            {
                for (int ty = y; ty <= y + 5; ty++)
                {
                    if (tx == x || tx == x + 5 || ty == y || ty == y + 5)
                    {
                        Main.tile[tx, ty].type = 226;
                        Main.tile[tx, ty].active(true);
                        Main.tile[tx, ty].slope(0);
                        Main.tile[tx, ty].halfBrick(false);
                    }
                    else
                    {
                        Main.tile[tx, ty].ClearTile();
                    }
                    WorldGen.SquareTileFrame(tx, ty, true);
                }
            }
            TSPlayer.All.SendTileRect((short)(x - 1), (short)(y - 1), 8, 8);
            TShock.Log.ConsoleInfo($"[CCTG] Spectator box built at ({x}, {y})");
        }

        private void ClearSpectatorBox()
        {
            if (_spectatorBox.X == -1)
                return;
            int x = _spectatorBox.X;
            int y = _spectatorBox.Y;
            for (int tx = x; tx <= x + 5; tx++)
            {
                for (int ty = y; ty <= y + 5; ty++)
                {
                    Main.tile[tx, ty].ClearTile();
                    WorldGen.SquareTileFrame(tx, ty, true);
                }
            }
            TSPlayer.All.SendTileRect((short)(x - 1), (short)(y - 1), 8, 8);
            _spectatorBox = new Point(-1, -1);
        }

        // Reset player inventory and stats
        private void ResetPlayerInventoryAndStats(TSPlayer player)
        {
            // Reset to SSC configured new player state
            player.PlayerData.CopyCharacter(player);
            TShock.CharacterDB.InsertPlayerData(player);
            player.IgnoreSSCPackets = false;

            // Set to SSC starting stats
            player.TPlayer.statLife = TShock.ServerSideCharacterConfig.Settings.StartingHealth;
            player.TPlayer.statLifeMax = TShock.ServerSideCharacterConfig.Settings.StartingHealth;
            player.TPlayer.statMana = TShock.ServerSideCharacterConfig.Settings.StartingMana;
            player.TPlayer.statManaMax = TShock.ServerSideCharacterConfig.Settings.StartingMana;

            // Clear inventory
            for (int i = 0; i < NetItem.InventorySlots; i++)
            {
                player.TPlayer.inventory[i].SetDefaults(0);
            }

            // Clear equipment (armor and accessories)
            for (int i = 0; i < NetItem.ArmorSlots; i++)
            {
                player.TPlayer.armor[i].SetDefaults(0);
            }

            // Clear dyes
            for (int i = 0; i < NetItem.DyeSlots; i++)
            {
                player.TPlayer.dye[i].SetDefaults(0);
            }

            // Clear misc equipment (pets, mounts, etc)
            for (int i = 0; i < NetItem.MiscEquipSlots; i++)
            {
                player.TPlayer.miscEquips[i].SetDefaults(0);
            }

            // Clear misc dyes
            for (int i = 0; i < NetItem.MiscDyeSlots; i++)
            {
                player.TPlayer.miscDyes[i].SetDefaults(0);
            }

            // Clear piggy bank
            for (int i = 0; i < NetItem.PiggySlots; i++)
                player.TPlayer.bank.item[i].SetDefaults(0);

            // Clear safe
            for (int i = 0; i < NetItem.SafeSlots; i++)
                player.TPlayer.bank2.item[i].SetDefaults(0);

            // Clear defender's forge
            for (int i = 0; i < NetItem.ForgeSlots; i++)
                player.TPlayer.bank3.item[i].SetDefaults(0);

            // Clear void vault
            for (int i = 0; i < NetItem.VoidSlots; i++)
                player.TPlayer.bank4.item[i].SetDefaults(0);

            // Clear trash
            player.TPlayer.trashItem.SetDefaults(0);

            // Clear loadout equipment slots
            for (int loadout = 0; loadout < player.TPlayer.Loadouts.Length; loadout++)
            {
                var ld = player.TPlayer.Loadouts[loadout];
                if (ld == null) continue;
                for (int i = 0; i < ld.Armor.Length; i++)
                    ld.Armor[i].SetDefaults(0);
                for (int i = 0; i < ld.Dye.Length; i++)
                    ld.Dye[i].SetDefaults(0);
            }

            // Clear all buffs
            for (int b = 0; b < Terraria.Player.maxBuffs; b++)
            {
                player.TPlayer.buffType[b] = 0;
                player.TPlayer.buffTime[b] = 0;
            }

            // Give starting items
            var startingItems = TShock.ServerSideCharacterConfig.Settings.StartingInventory;
            for (int i = 0; i < startingItems.Count && i < NetItem.InventorySlots; i++)
            {
                player.TPlayer.inventory[i] = startingItems[i].ToItem();
            }

            // Sync to client via CopyCharacter + SendServerCharacter
            player.PlayerData.CopyCharacter(player);
            player.SendServerCharacter();
            player.IgnoreSSCPackets = false;

            player.SendData(PacketTypes.PlayerHp, "", player.Index);
            player.SendData(PacketTypes.PlayerMana, "", player.Index);
            player.SendData(PacketTypes.PlayerInfo, "", player.Index);

            for (int b = 0; b < Terraria.Player.maxBuffs; b++)
                player.SendData(PacketTypes.PlayerBuff, "", player.Index, b);
        }

        // Tile edit event handler - protect houses and block indestructible tiles
        private void OnTileEdit(object sender, GetDataHandlers.TileEditEventArgs e)
        {
            if (e.Handled)
                return;

            // Block placement of indestructible tiles (Obsidian, Crimstone, Ebonstone, Hellstone)
            if (e.Action == GetDataHandlers.EditAction.PlaceTile)
            {
                if (e.EditData == 56 || e.EditData == 836 || e.EditData == 25 || e.EditData == 58)
                {
                    e.Handled = true;
                    e.Player.SendTileRect((short)e.X, (short)e.Y, 1, 1);
                    e.Player.SendErrorMessage("You don't have permission to place indestructible tiles!");
                    return;
                }
            }

            if (!houseBuilder.HousesBuilt)
                return;

            // Check if edit position is in any protected area
            foreach (var area in houseBuilder.ProtectedHouseAreas)
            {
                if (area.Contains(e.X, e.Y))
                {
                    if (!e.Player.HasPermission("cctg.edit"))
                    {
                        e.Handled = true;
                        // Send the full protected area to restore multi-tile furniture correctly
                        e.Player.SendTileRect((short)area.X, (short)area.Y,
                            (byte)Math.Min(area.Width + 2, 255), (byte)Math.Min(area.Height + 2, 255));
                    }
                    break;
                }
            }

            // Protect gem lock areas
            int gemLockIdx = houseBuilder.IsInGemLockArea(e.X, e.Y);
            if (gemLockIdx >= 0)
            {
                e.Handled = true;
                var glInfo = houseBuilder.GemLockInfos[gemLockIdx];
                e.Player.SendTileRect((short)(glInfo.X - 2), (short)(glInfo.GroundY - 5), 5, 5);
                return;
            }
        }

        private void OnGemLockToggle(object sender, GetDataHandlers.GemLockToggleEventArgs e)
        {
            if (e.Handled)
                return;

            int idx = houseBuilder.IsInGemLockArea(e.X, e.Y);
            if (idx >= 0)
            {
                e.Handled = true;
                var glInfo = houseBuilder.GemLockInfos[idx];
                e.Player.SendTileRect((short)(glInfo.X - 2), (short)(glInfo.GroundY - 5), 5, 5);
            }
        }

        private void OnNPCTalk(object sender, GetDataHandlers.NpcTalkEventArgs e)
        {
            try
            {
                // Get player and NPC
                var player = TShock.Players[e.PlayerId];
                if (player == null || !player.Active)
                    return;

                int npcIndex = e.NPCTalkTarget;
                if (npcIndex == -1)
                    return;

                var npc = Main.npc[npcIndex];
                if (npc == null || !npc.active)
                    return;

                // Check if this is a demolitionsit
                if (npc.type == NPCID.Demolitionist)
                {
                    // Start or restart the timed shop modification
                    if (!shopModificationStates.ContainsKey(player.Index))
                    {
                        shopModificationStates[player.Index] = new ShopModificationState();
                    }

                    var state = shopModificationStates[player.Index];
                    state.IsModifying = true;
                    state.StartTime = DateTime.Now;
                    state.FrameCounter = 0;
                    state.LastModifiedFrame = 0;
                    state.TargetNPCIndex = npcIndex;

                }
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[CCTG] Error in OnNPCTalk: {ex.Message}");
            }
        }

        // Player joined event
        private void OnPlayerJoin(GreetPlayerEventArgs e)
        {
            var player = TShock.Players[e.Who];
            if (player == null)
                return;


            if (gameStarted && pvpEnabled)
            {
                pendingJoinAssignments[e.Who] = DateTime.Now;
            }
        }

        // Track recent packet types for debugging
        private static HashSet<PacketTypes> loggedPacketTypes = new HashSet<PacketTypes>();

        // Network data event handler - monitor item usage and team changes
        private void OnGetData(GetDataEventArgs e)
        {
            if (e.Handled)
                return;

            var player = TShock.Players[e.Msg.whoAmI];
            if (player == null)
                return;

            // Block all packets during world swap
            if (_cycleState == CycleState.Swapping || _cycleState == CycleState.StartPending)
            {
                e.Handled = true;
                return;
            }

            // Log all packet types once for debugging
            if (!loggedPacketTypes.Contains(e.MsgID))
            {
                loggedPacketTypes.Add(e.MsgID);
            }

            // === Block manual PVP toggle during game ===
            if (e.MsgID == PacketTypes.TogglePvp && gameStarted)
            {
                e.Handled = true;
                player.TPlayer.hostile = pvpEnabled;
                TSPlayer.All.SendData(PacketTypes.TogglePvp, "", player.Index);
                return;
            }

            // === Handle Terraria 1.4.5 team change packet (157) ===
            // TShock doesn't handle packet 157, so Main.player[].team is not updated
            // We need to manually update it here
            if ((int)e.MsgID == 157)
            {
                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    byte playerId = reader.ReadByte();
                    byte newTeam = reader.ReadByte();

                    // Update server-side team value (this is what TShock's SetTeam does)
                    Main.player[player.Index].team = newTeam;

                    string teamName = newTeam == 1 ? "Red" : newTeam == 3 ? "Blue" : newTeam.ToString();
                }
            }

            // === Monitor Recall item usage ===
            if (e.MsgID == PacketTypes.PlayerUpdate)
            {
                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    byte playerId = reader.ReadByte();
                    byte control = reader.ReadByte();
                    byte pulley = reader.ReadByte();
                    byte miscFlags = reader.ReadByte();
                    byte sleepingInfo = reader.ReadByte();
                    byte selectedItem = reader.ReadByte();

                    var selectedItemType = player.TPlayer.inventory[selectedItem].type;

                    // Check if player used a recall item
                    if (teleportManager.IsRecallItem(selectedItemType))
                    {
                        // Check if player is using the item (control flags)
                        bool isUsingItem = (control & 32) != 0; // Bit 5 = using item

                        if (!houseBuilder.HousesBuilt)
                            return;

                        if (isUsingItem)
                        {
                            // Record recall state
                            if (!teleportManager.PlayerRecallStates.ContainsKey(player.Index))
                            {
                                teleportManager.PlayerRecallStates[player.Index] = new RecallTeleportState();
                            }

                            var recallState = teleportManager.PlayerRecallStates[player.Index];

                            if (!recallState.WaitingForTeleport && !recallState.WaitingToTeleportToTeamHouse)
                            {
                                recallState.WaitingForTeleport = true;
                                recallState.LastItemUseTime = DateTime.Now;
                                recallState.LastKnownPosition = player.TPlayer.position;
                            }
                        }
                    }

                    if (teleportManager.IsGemDropTeleportItem(selectedItemType))
                    {
                        bool isUsingItem = (control & 32) != 0;
                        if (isUsingItem)
                        {
                            ForceReturnGemOnTeleport(player);
                        }
                    }
                }
            }

            // === Monitor respawn events ===
            if (e.MsgID == PacketTypes.PlayerSpawn)
            {
                if (_pendingSpectators.Contains(player.Index))
                {
                    _pendingSpectators.Remove(player.Index);
                    _delayedSpectators[player.Index] = 30;
                    return;
                }

                if (!houseBuilder.HousesBuilt)
                    return;

                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(e.Msg.readBuffer, e.Index, e.Length)))
                {
                    byte playerId = reader.ReadByte();
                    short spawnX = reader.ReadInt16();
                    short spawnY = reader.ReadInt16();
                    int respawnTimeRemaining = reader.ReadInt32();
                    byte playerSpawnContext = reader.ReadByte();

                    int playerTeam = player.TPlayer.team;

                    if (!teleportManager.PlayerTeamStates.ContainsKey(player.Index))
                    {
                        teleportManager.PlayerTeamStates[player.Index] = new PlayerTeamState();
                    }

                    var state = teleportManager.PlayerTeamStates[player.Index];
                    state.LastTeam = playerTeam;
                    state.LastTeamChangeTime = DateTime.Now;

                    string teamName = playerTeam == 1 ? "Red Team" : playerTeam == 3 ? "Blue Team" : "No Team";

                    state.RespawnFullHp = player.TPlayer.statLifeMax2;
                    player.SendInfoMessage($"Respawning, teleporting to {teamName} house in 0.5s");
                }
            }
        }

        // Game update event handler
        private void OnGameUpdate(EventArgs args)
        {
            if (_delayedSpectators.Count > 0)
            {
                foreach (var key in new List<int>(_delayedSpectators.Keys))
                {
                    _delayedSpectators[key]--;
                    if (_delayedSpectators[key] <= 0)
                    {
                        _delayedSpectators.Remove(key);
                        var p = TShock.Players[key];
                        if (p != null && p.Active)
                        {
                            MoveToSpectator(p);
                        }
                    }
                }
            }

            if (gameStarted && _spectatorBox.X != -1)
            {
                foreach (var p in TShock.Players)
                {
                    if (p == null || !p.Active || p.TPlayer.team != 4)
                        continue;

                    int tileX = (int)(p.TPlayer.position.X / 16f);
                    int tileY = (int)(p.TPlayer.position.Y / 16f);
                    if (tileX <= _spectatorBox.X || tileX >= _spectatorBox.X + 5 ||
                        tileY <= _spectatorBox.Y || tileY >= _spectatorBox.Y + 5)
                    {
                        p.Teleport((_spectatorBox.X + 2) * 16f, (_spectatorBox.Y + 1) * 16f);
                    }
                }
            }

            // Track day/night transitions and game time events
            if (gameStarted)
            {
                CheckGameTimeEvents();

                if (_startCountdown && (DateTime.Now - _startCountdownTime).TotalSeconds >= 5)
                {
                    _startCountdown = false;
                    foreach (var player in TShock.Players)
                    {
                        if (player != null && player.Active)
                        {
                            for (int b = 0; b < Terraria.Player.maxBuffs; b++)
                            {
                                if (player.TPlayer.buffType[b] == 149)
                                {
                                    player.TPlayer.buffType[b] = 0;
                                    player.TPlayer.buffTime[b] = 0;
                                    player.SendData(PacketTypes.PlayerBuff, "", player.Index, b);
                                }
                            }
                        }
                    }
                    TSPlayer.All.SendMessage("The game has started! You have 18 minutes to prepare.", 255, 105, 180);
                }
            }

            // Update scoreboard (every second)
            scoreboardUpdateCounter++;
            if (scoreboardUpdateCounter >= SCOREBOARD_UPDATE_INTERVAL)
            {
                scoreboardUpdateCounter = 0;
                UpdateScoreboard();
            }

            // Check restricted items in player inventory (every second)
            itemCheckCounter++;
            if (itemCheckCounter >= ITEM_CHECK_INTERVAL)
            {
                itemCheckCounter = 0;
                foreach (var player in TShock.Players)
                {
                    if (player != null && player.Active)
                    {
                        restrictItem.CheckAndRemoveRestrictedItems(player);

                        for (int s = 0; s < player.TPlayer.inventory.Length; s++)
                        {
                            var item = player.TPlayer.inventory[s];
                            if (item != null && item.type == 4870)
                            {
                                item.SetDefaults(0);
                                player.SendData(PacketTypes.PlayerSlot, "", player.Index, s);
                            }
                        }

                        for (int b = 0; b < Terraria.Player.maxBuffs; b++)
                        {
                            int bt = player.TPlayer.buffType[b];
                            if (player.TPlayer.buffTime[b] > 0 && MountBuffDebuffs.ContainsKey(bt))
                            {
                                foreach (int debuff in MountBuffDebuffs[bt])
                                    player.SetBuff(debuff, 120);
                            }
                        }
                    }
                }
            }

            // Update active timers (every second)
            timerUpdateCounter++;
            if (timerUpdateCounter >= TIMER_UPDATE_INTERVAL)
            {
                timerUpdateCounter = 0;
                UpdateActiveTimers();
            }

            // Handle timed shop modifications (every frame for precise timing)
            List<int> shopPlayersToRemove = new List<int>();
            foreach (var kvp in shopModificationStates)
            {
                int playerIndex = kvp.Key;
                var state = kvp.Value;
                var player = TShock.Players[playerIndex];

                // Remove states for disconnected/inactive players
                if (player == null || !player.Active)
                {
                    shopPlayersToRemove.Add(playerIndex);
                    continue;
                }

                if (state.IsModifying)
                {
                    state.FrameCounter++;

                    // Check if 10 seconds have passed
                    if (state.FrameCounter >= SHOP_MODIFICATION_DURATION)
                    {
                        state.IsModifying = false;
                        shopPlayersToRemove.Add(playerIndex);
                        continue;
                    }

                    // Check if 0.5 seconds have passed since last modification
                    if (state.FrameCounter - state.LastModifiedFrame >= SHOP_MODIFICATION_INTERVAL)
                    {
                        state.LastModifiedFrame = state.FrameCounter;

                        // Send shop modification packet
                        try
                        {
                            // Check if player is still near the target NPC
                            var npc = Main.npc[state.TargetNPCIndex];
                            if (npc != null && npc.active && npc.type == NPCID.Demolitionist)
                            {
                                float distance = Vector2.Distance(player.TPlayer.position, npc.position);
                                if (distance < 300) // Within range
                                {
                                    restrictItem.ModifyDemolitionistShop(player);
                                }
                                else
                                {
                                    // Player moved away, stop modification
                                    state.IsModifying = false;
                                    shopPlayersToRemove.Add(playerIndex);
                                }
                            }
                            else
                            {
                                // NPC is no longer active, stop modification
                                state.IsModifying = false;
                                shopPlayersToRemove.Add(playerIndex);
                            }
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleError($"[CCTG] Error in timed shop modification for player {player.Name}: {ex.Message}");
                        }
                    }
                }
            }

            // Remove completed shop states
            foreach (int playerIndex in shopPlayersToRemove)
            {
                shopModificationStates.Remove(playerIndex);
            }

            // Clear mobs in houses (every 0.5s, only after game starts)
            if (gameStarted && houseBuilder.HousesBuilt)
            {
                mobClearCounter++;
                if (mobClearCounter >= MOB_CLEAR_INTERVAL)
                {
                    mobClearCounter = 0;
                    houseBuilder.ClearMobsInHouses();
                }
            }

            // Queen Bee despawn check
            if (gameStarted)
            {
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC npc = Main.npc[i];
                    if (npc == null || !npc.active || npc.type != 222)
                        continue;

                    int targetIndex = npc.target;
                    if (targetIndex < 0 || targetIndex >= Main.maxPlayers)
                    {
                        npc.active = false;
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                        TSPlayer.All.SendSuccessMessage("Queen Bee has been despawned successfully.");
                        TShock.Log.ConsoleInfo("[CCTG] Queen Bee despawned: no valid target");
                        continue;
                    }

                    Terraria.Player target = Main.player[targetIndex];
                    if (target == null || !target.active)
                    {
                        npc.active = false;
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                        TSPlayer.All.SendSuccessMessage("Queen Bee has been despawned successfully.");
                        TShock.Log.ConsoleInfo("[CCTG] Queen Bee despawned: target inactive");
                        continue;
                    }

                    float dx = npc.position.X - target.position.X;
                    float dy = npc.position.Y - target.position.Y;
                    float distTiles = (float)Math.Sqrt(dx * dx + dy * dy) / 16f;
                    if (distTiles > 400f)
                    {
                        npc.active = false;
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                        TSPlayer.All.SendSuccessMessage("Queen Bee has been despawned successfully.");
                        TShock.Log.ConsoleInfo($"[CCTG] Queen Bee despawned: target {target.name} was {distTiles:F0} tiles away");
                    }
                }
            }

            // Repair gem locks periodically (every 1s)
            if (houseBuilder.HousesBuilt)
            {
                gemLockRepairCounter++;
                if (gemLockRepairCounter >= GEM_LOCK_REPAIR_INTERVAL)
                {
                    gemLockRepairCounter = 0;
                    houseBuilder.RepairGemLocks();
                }
            }

            // Gem pickup detection (every 1s, only when game started and houses built)
            if (gameStarted && houseBuilder.HousesBuilt)
            {
                gemPickupCheckCounter++;
                if (gemPickupCheckCounter >= GEM_PICKUP_CHECK_INTERVAL)
                {
                    gemPickupCheckCounter = 0;
                    CheckGemPickups();
                }

                gemTrackingCheckCounter++;
                if (gemTrackingCheckCounter >= GEM_TRACKING_CHECK_INTERVAL)
                {
                    gemTrackingCheckCounter = 0;
                    CheckGemTracking();
                }
            }

            // Hook drop projectile refresh (every 1 second)
            if (gameStarted && hookDropStates.Count > 0)
            {
                hookDropProjCounter++;
                if (hookDropProjCounter >= HOOK_DROP_PROJ_INTERVAL)
                {
                    hookDropProjCounter = 0;
                    foreach (var kvp in hookDropStates)
                    {
                        var player = TShock.Players[kvp.Key];
                        if (player == null || !player.Active)
                            continue;
                        foreach (var state in kvp.Value)
                            SendHookDropProjectile(player, state);
                    }
                }

                hookDropCheckCounter++;
                if (hookDropCheckCounter >= HOOK_DROP_CHECK_INTERVAL)
                {
                    hookDropCheckCounter = 0;
                    var playersToClean = new List<int>();
                    foreach (var kvp in hookDropStates)
                    {
                        var player = TShock.Players[kvp.Key];
                        if (player == null || !player.Active || player.TPlayer.dead)
                            continue;

                        var stateList = kvp.Value;
                        var pickedUp = new List<HookDropState>();

                        foreach (var state in stateList)
                        {
                            float dx = player.TPlayer.position.X - state.DeathX;
                            float dy = player.TPlayer.position.Y - state.DeathY;
                            if (Math.Abs(dx) > 64f || Math.Abs(dy) > 64f)
                                continue;

                            int emptySlots = 0;
                            for (int s = 0; s < 50; s++)
                            {
                                var item = player.TPlayer.inventory[s];
                                if (item == null || item.type == 0 || item.IsAir)
                                    emptySlots++;
                            }

                            if (emptySlots >= state.DroppedHooks.Count)
                            {
                                int slotIdx = 0;
                                foreach (var hook in state.DroppedHooks)
                                {
                                    for (int s = slotIdx; s < 50; s++)
                                    {
                                        var item = player.TPlayer.inventory[s];
                                        if (item == null || item.type == 0 || item.IsAir)
                                        {
                                            player.TPlayer.inventory[s].SetDefaults(hook.Type);
                                            player.TPlayer.inventory[s].stack = hook.Stack;
                                            player.SendData(PacketTypes.PlayerSlot, "", player.Index, s);
                                            slotIdx = s + 1;
                                            break;
                                        }
                                    }
                                }
                                KillHookDropProjectile(player, state);
                                pickedUp.Add(state);
                                TShock.Log.ConsoleInfo($"[CCTG] {player.Name} recovered {state.DroppedHooks.Count} hook stack(s)");
                            }
                            else
                            {
                                if ((DateTime.Now - state.LastMessageTime).TotalSeconds >= 3)
                                {
                                    player.SendMessage("Clear your inventory to get your grappling hooks back.", 255, 200, 0);
                                    state.LastMessageTime = DateTime.Now;
                                }
                            }
                        }

                        foreach (var s in pickedUp)
                            stateList.Remove(s);
                        if (stateList.Count == 0)
                            playersToClean.Add(kvp.Key);
                    }
                    foreach (int key in playersToClean)
                        hookDropStates.Remove(key);
                }
            }

            if (pendingJoinAssignments.Count > 0)
            {
                var toRemove = new List<int>();
                foreach (var kvp in pendingJoinAssignments)
                {
                    if ((DateTime.Now - kvp.Value).TotalSeconds >= 1.5)
                    {
                        toRemove.Add(kvp.Key);
                        var player = TShock.Players[kvp.Key];
                        if (player == null || !player.Active || !gameStarted || !pvpEnabled)
                            continue;
                        if (player.Group.Name == "guest")
                            continue;

                        player.TPlayer.hostile = true;
                        TSPlayer.All.SendData(PacketTypes.TogglePvp, "", player.Index);

                        int redCount = 0;
                        int blueCount = 0;
                        foreach (var p in TShock.Players)
                        {
                            if (p != null && p.Active && p.Index != player.Index)
                            {
                                if (p.Team == 1) redCount++;
                                else if (p.Team == 3) blueCount++;
                            }
                        }

                        int team;
                        if (redCount < blueCount) team = 1;
                        else if (blueCount < redCount) team = 3;
                        else team = new Random().Next(2) == 0 ? 1 : 3;

                        player.SetTeam(team);

                        if (!player.HasPermission("ignoressc"))
                        {
                            ResetPlayerInventoryAndStats(player);
                        }

                        player.SetBuff(149, 120);

                        string teamName = team == 1 ? "Red Team" : "Blue Team";
                        player.SendSuccessMessage($"You have been assigned to {teamName}!");

                        if (!teleportManager.PlayerTeamStates.ContainsKey(player.Index))
                        {
                            teleportManager.PlayerTeamStates[player.Index] = new PlayerTeamState();
                        }

                        var state = teleportManager.PlayerTeamStates[player.Index];
                        state.LastTeam = team;
                        state.LastTeamChangeTime = DateTime.Now;

                    }
                }
                foreach (int key in toRemove)
                    pendingJoinAssignments.Remove(key);
            }

            // Auto-cycle state machine
            if (_cycleState == CycleState.Generating && string.IsNullOrEmpty(_generatingFilename))
            {
                // _generatingFilename unknown (IsGenerating was true at start) — wait for it to finish
                if (WorldGenPlugin.WorldGenPlugin.Instance?.IsGenerating != true)
                {
                    TShock.Log.ConsoleInfo("[CCTG] External generation finished, resuming...");
                    // Scan worlds dir for newly generated worlds not yet in queue
                    try
                    {
                        foreach (var f in Directory.GetFiles(Main.WorldPath, "*.wld"))
                        {
                            string name = Path.GetFileNameWithoutExtension(f);
                            if (name.Length == 8 && System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-f]{8}$")
                                && !_worldQueue.Contains(name)
                                && f != Main.worldPathName)
                            {
                                _worldQueue.Enqueue(name);
                                TShock.Log.ConsoleInfo($"[CCTG] Recovered world {name} into queue");
                            }
                        }
                        SaveWorldQueue();
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.ConsoleError($"[CCTG] Failed to scan worlds: {ex.Message}");
                    }
                    _cycleState = CycleState.Idle;
                    TryStartNextGeneration();
                }
            }
            else if (_cycleState == CycleState.Generating && !string.IsNullOrEmpty(_generatingFilename))
            {
                string worldPath = Path.Combine(Main.WorldPath, _generatingFilename + ".wld");
                if (File.Exists(worldPath) && new FileInfo(worldPath).Length > 0)
                {
                    _worldQueue.Enqueue(_generatingFilename);
                    TShock.Log.ConsoleInfo($"[CCTG] World {_generatingFilename} ready, queue={_worldQueue.Count}");
                    _generatingFilename = null;
                    SaveWorldQueue();

                    if (!gameStarted)
                    {
                        _cycleState = CycleState.WaitingConfirm;
                        _cycleStateTime = DateTime.Now;
                        _confirmBroadcasted = false;
                        _confirmBroadcasted2 = false;
                    }
                    else
                    {
                        _cycleState = CycleState.Idle;
                    }
                    TryStartNextGeneration();
                }
                else if ((DateTime.Now - _cycleStateTime).TotalMinutes > 10)
                {
                    TShock.Log.ConsoleError("[CCTG] Generation timed out. Killing and retrying...");
                    Commands.HandleCommand(TSPlayer.Server, "/killgenworld");
                    _generatingFilename = null;
                    _cycleState = CycleState.Idle;
                    TryStartNextGeneration();
                }
                else if (WorldGenPlugin.WorldGenPlugin.Instance?.IsGenerating != true)
                {
                    _generatingFilename = null;
                    _cycleState = CycleState.Idle;
                    TryStartNextGeneration();
                }
            }
            else if (_cycleState == CycleState.Idle)
            {
                TryStartNextGeneration();
            }
            else if (_cycleState == CycleState.WaitingConfirm)
            {
                double elapsed = (DateTime.Now - _cycleStateTime).TotalSeconds;
                if (!_confirmBroadcasted && elapsed >= 0.1)
                {
                    _confirmBroadcasted = true;
                    TSPlayer.All.SendInfoMessage("Entering next world in 10 seconds. Type /n to cancel.");
                }
                if (!_confirmBroadcasted2 && elapsed >= 5)
                {
                    _confirmBroadcasted2 = true;
                    TSPlayer.All.SendInfoMessage("Entering next world in 5 seconds...");
                }
                if (elapsed >= 10)
                {
                    if (_cycleCancelled)
                    {
                        _cycleState = CycleState.Idle;
                        TSPlayer.All.SendInfoMessage("Next round cancelled.");
                    }
                    else
                    {
                        bool hasPlayers = false;
                        foreach (var p in TShock.Players)
                        {
                            if (p != null && p.Active)
                            {
                                hasPlayers = true;
                                break;
                            }
                        }
                        if (!hasPlayers)
                        {
                            _cycleState = CycleState.Idle;
                        }
                        else
                        {
                            _cycleState = CycleState.Swapping;
                            foreach (var p in TShock.Players)
                            {
                                if (p != null && p.Active)
                                    p.SetBuff(149, 60 * 60); // Webbed, 60 seconds
                            }
                        }
                    }
                }
            }
            else if (_cycleState == CycleState.Swapping)
            {
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (Main.npc[i].active && Main.npc[i].townNPC)
                    {
                        Main.npc[i].active = false;
                        Main.npc[i].type = 0;
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                    }
                }

                _prevWorldPath = Main.worldPathName;
                string nextFilename = _worldQueue.Dequeue();
                SaveWorldQueue();
                string worldPath = Path.Combine(Main.WorldPath, nextFilename + ".wld");
                TShock.Log.ConsoleInfo($"[CCTG] Swapping to world: {worldPath} (queue remaining={_worldQueue.Count})");
                Commands.HandleCommand(TSPlayer.Server, $"/swapworld {worldPath}");
                _cycleState = CycleState.StartPending;
                _cycleStateTime = DateTime.Now;
            }
            else if (_cycleState == CycleState.StartPending)
            {
                if ((DateTime.Now - _cycleStateTime).TotalSeconds >= 2)
                {
                    _cycleState = CycleState.Idle;

                    // Delete previous world files
                    if (!string.IsNullOrEmpty(_prevWorldPath))
                    {
                        try
                        {
                            bool wldExists = File.Exists(_prevWorldPath);
                            if (wldExists)
                                File.Delete(_prevWorldPath);
                            string twldPath = Path.ChangeExtension(_prevWorldPath, ".twld");
                            if (File.Exists(twldPath))
                                File.Delete(twldPath);
                            string bakPath = _prevWorldPath + ".bak";
                            if (File.Exists(bakPath))
                                File.Delete(bakPath);
                            string bak2Path = _prevWorldPath + ".bak2";
                            if (File.Exists(bak2Path))
                                File.Delete(bak2Path);
                            TShock.Log.ConsoleInfo($"[CCTG] Deleted old world: {_prevWorldPath}");
                        }
                        catch (Exception ex)
                        {
                            TShock.Log.ConsoleError($"[CCTG] Failed to delete old world: {ex.Message}");
                        }
                        _prevWorldPath = null;
                    }

                    for (int i = 0; i < Main.maxProjectiles; i++)
                    {
                        if (Main.projectile[i].active)
                        {
                            Main.projectile[i].active = false;
                            Main.projectile[i].type = 0;
                            TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", i);
                        }
                    }
                    for (int i = 0; i < 400; i++)
                    {
                        TSPlayer.All.SendData(PacketTypes.ItemDrop, "", i, 0f, 0f, 0f, 0);
                    }

                    foreach (var player in TShock.Players)
                    {
                        if (player != null && player.ConnectionAlive)
                        {
                            player.Teleport(Main.spawnTileX * 16f, Main.spawnTileY * 16f);
                            NetMessage.SendData((int)PacketTypes.WorldInfo, player.Index);
                            RemoteClient.CheckSection(player.Index, new Microsoft.Xna.Framework.Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f), 1);
                        }
                    }

                    TShock.Log.ConsoleInfo("[CCTG] Auto-starting next round...");
                    StartCommand(new CommandArgs("start", TSPlayer.Server, new List<string>()));
                }
            }

            // Team change detection and teleport (only when houses are built)
            if (!houseBuilder.HousesBuilt)
                return;

            foreach (var player in TShock.Players)
            {
                if (player == null || !player.Active)
                    continue;
                if (player.Group.Name == "guest")
                    continue;

                if (pvpEnabled && player.TPlayer.team == 0 && !pendingJoinAssignments.ContainsKey(player.Index))
                {
                    pendingJoinAssignments[player.Index] = DateTime.Now;
                    continue;
                }

                // === Detect team changes by polling ===
                if (!teleportManager.PlayerTeamStates.ContainsKey(player.Index))
                {
                    teleportManager.PlayerTeamStates[player.Index] = new PlayerTeamState();
                    teleportManager.PlayerTeamStates[player.Index].LastTeam = player.Team;
                }

                var state = teleportManager.PlayerTeamStates[player.Index];
                int currentTeam = player.Team;

                // Check if team changed
                if (currentTeam != state.LastTeam)
                {
                    int previousTeam = state.LastTeam;
                    string teamName = currentTeam == 1 ? "Red Team" : currentTeam == 3 ? "Blue Team" : "Other";

                    state.LastTeam = currentTeam;

                    if (currentTeam == 4)
                    {
                        MoveToSpectator(player);
                    }
                    else if (previousTeam == 4 && (currentTeam == 1 || currentTeam == 3))
                    {
                        ResetPlayerInventoryAndStats(player);
                        state.LastTeamChangeTime = DateTime.Now;
                    }
                    else if (currentTeam == 1 || currentTeam == 3)
                    {
                        ResetPlayerInventoryAndStats(player);
                        state.LastTeamChangeTime = DateTime.Now;
                    }
                    else if (currentTeam == 0 || currentTeam == 2 || currentTeam == 5)
                    {
                        ClearPlayerInventory(player);
                    }
                }

                // === Handle teleport after team change ===
                if (state.LastTeamChangeTime != DateTime.MinValue)
                {
                    var timeSinceChange = (DateTime.Now - state.LastTeamChangeTime).TotalSeconds;

                    if (timeSinceChange >= 0.5)
                    {
                        ForceReturnGemOnTeleport(player);
                        teleportManager.TeleportToTeamHouse(player, houseBuilder.LeftHouseSpawn, houseBuilder.RightHouseSpawn);

                        // Clear teleport marker and respawn HP guard
                        state.LastTeamChangeTime = DateTime.MinValue;
                        state.RespawnFullHp = 0;
                    }
                    else if (state.RespawnFullHp > 0 && player.TPlayer.statLife < state.RespawnFullHp)
                    {
                        int lost = state.RespawnFullHp - player.TPlayer.statLife;
                        player.TPlayer.statLife = state.RespawnFullHp;
                        player.TPlayer.Heal(lost);
                        player.SendData(PacketTypes.PlayerHp, "", player.Index);
                    }
                }

                // === Handle recall teleport ===
                if (teleportManager.PlayerRecallStates.ContainsKey(player.Index))
                {
                    var recallState = teleportManager.PlayerRecallStates[player.Index];

                    // Stage 1: Wait for vanilla teleport
                    if (recallState.WaitingForTeleport)
                    {
                        var timeSinceItemUse = (DateTime.Now - recallState.LastItemUseTime).TotalSeconds;

                        if (timeSinceItemUse < 3.0)
                        {
                            float distance = Vector2.Distance(player.TPlayer.position, recallState.LastKnownPosition);

                            // If position changed > 200 pixels, teleport occurred
                            if (distance > 200f)
                            {

                                // Force return gem if player is carrying one
                                ForceReturnGemOnTeleport(player);

                                recallState.WaitingForTeleport = false;
                                recallState.WaitingToTeleportToTeamHouse = true;
                                recallState.TeleportDetectedTime = DateTime.Now;

                                // Set recall grace period for boundary check
                                boundaryChecker.SetRecallGrace(player.Index);
                                continue;
                            }

                            recallState.LastKnownPosition = player.TPlayer.position;
                        }
                        else
                        {
                            recallState.WaitingForTeleport = false;
                        }
                    }

                    // Stage 2: Teleport to team house
                    if (recallState.WaitingToTeleportToTeamHouse)
                    {
                        var timeSinceDetect = (DateTime.Now - recallState.TeleportDetectedTime).TotalSeconds;

                        if (timeSinceDetect >= 0.5)
                        {
                            teleportManager.TeleportToTeamHouse(player, houseBuilder.LeftHouseSpawn, houseBuilder.RightHouseSpawn);

                            recallState.WaitingToTeleportToTeamHouse = false;
                        }
                    }
                }

                // === Boundary violation check (only during first 18 minutes of game) ===
                if (gameStarted)
                {
                    boundaryChecker.CheckBoundaryViolation(player);
                }
            }
        }

        // Check gem pickup for all gem locks
        private void CheckGemPickups()
        {
            var gemLocks = houseBuilder.GemLockInfos;
            for (int i = 0; i < gemLocks.Count; i++)
            {
                // Get or create pickup state for this gem lock
                if (!gemPickupStates.ContainsKey(i))
                {
                    gemPickupStates[i] = new GemPickupState();
                }
                var state = gemPickupStates[i];

                // Skip if already picked up
                if (state.Completed)
                    continue;

                var info = gemLocks[i];

                // Determine which team can pick up this gem lock
                // Style 0 (Ruby/Red) → only Blue team (3) can pick up
                // Style 1 (Sapphire/Blue) → only Red team (1) can pick up
                int requiredTeam = info.Style == 0 ? 3 : 1;
                string gemTeamName = info.Style == 0 ? "Red" : "Blue";
                byte msgR = info.Style == 0 ? (byte)255 : (byte)50;
                byte msgG = info.Style == 0 ? (byte)50 : (byte)100;
                byte msgB = info.Style == 0 ? (byte)50 : (byte)255;
                // Large gem item: style 0 (red gem lock) → Large Ruby 1526, style 1 (blue) → Large Sapphire 1524
                int gemItemId = info.Style == 0 ? 1526 : 1524;

                // Find a valid player within ±2 tiles of the 3x3 bounds
                int foundPlayerIndex = -1;
                foreach (var player in TShock.Players)
                {
                    if (player == null || !player.Active || player.TPlayer.dead)
                        continue;

                    if (player.TPlayer.team != requiredTeam)
                        continue;

                    // Player tile position
                    int ptX = (int)(player.TPlayer.position.X / 16f);
                    int ptY = (int)(player.TPlayer.position.Y / 16f);

                    // Check if within expanded bounds (±2 tiles)
                    if (ptX >= info.WallLeft - 2 && ptX <= info.WallRight + 2 &&
                        ptY >= info.WallTop - 2 && ptY <= info.WallBottom + 2)
                    {
                        foundPlayerIndex = player.Index;
                        break;
                    }
                }

                if (foundPlayerIndex >= 0)
                {
                    var picker = TShock.Players[foundPlayerIndex];
                    if (picker == null || !picker.Active)
                    {
                        // Player disconnected mid-pickup
                        state.PlayerIndex = -1;
                        state.Countdown = 0;
                        continue;
                    }

                    // Check blocking conditions (buffs and inventory space)
                    // These checks apply both at start and during countdown
                    bool hasFeatherfall = false;
                    bool hasInvisible = false;
                    bool hasBeeMountBuff = false;
                    bool hasMinecartBuff = false;
                    bool hasMountBuff = false;
                    for (int b = 0; b < Terraria.Player.maxBuffs; b++)
                    {
                        int bt = picker.TPlayer.buffType[b];
                        if (picker.TPlayer.buffTime[b] <= 0)
                            continue;
                        if (bt == 8)
                            hasFeatherfall = true;
                        if (bt == 10)
                            hasInvisible = true;
                        if (bt == 132)
                            hasBeeMountBuff = true;
                        if (bt == 118 || bt == 166 || bt == 184 || bt == 208 || bt == 210 ||
                            bt == 220 || bt == 222 || bt == 224 || bt == 226 || bt == 228 ||
                            bt == 231 || bt == 233 || bt == 235 || bt == 237 || bt == 239 ||
                            bt == 241 || bt == 243 || bt == 245 || bt == 247 || bt == 249 ||
                            bt == 251 || bt == 253 || bt == 255 || bt == 269 || bt == 272 ||
                            bt == 338 || bt == 346)
                            hasMinecartBuff = true;
                        if (bt == 370 || bt == 130 || bt == 374 || bt == 378 || bt == 379 ||
                            bt == 380 || bt == 381 || bt == 387 || bt == 388)
                            hasMountBuff = true;
                    }

                    if (hasFeatherfall)
                    {
                        picker.SendMessage("Clear your Featherfall buff to pick up gem.", msgR, msgG, msgB);
                        state.PlayerIndex = -1;
                        state.Countdown = 0;
                        continue;
                    }

                    if (hasInvisible)
                    {
                        picker.SendMessage("Clear your Invisibility buff to pick up gem.", msgR, msgG, msgB);
                        state.PlayerIndex = -1;
                        state.Countdown = 0;
                        continue;
                    }

                    if (hasBeeMountBuff)
                    {
                        picker.SendMessage("Clear your bee mount buff to pick up gem.", msgR, msgG, msgB);
                        state.PlayerIndex = -1;
                        state.Countdown = 0;
                        continue;
                    }

                    if (hasMinecartBuff)
                    {
                        picker.SendMessage("Clear your minecart buff to pick up gem.", msgR, msgG, msgB);
                        state.PlayerIndex = -1;
                        state.Countdown = 0;
                        continue;
                    }

                    if (hasMountBuff)
                    {
                        picker.SendMessage("Get off your mount to pick up the gem.", msgR, msgG, msgB);
                        state.PlayerIndex = -1;
                        state.Countdown = 0;
                        continue;
                    }

                    // Check for empty inventory slot (main inventory only: slots 0-49)
                    int emptySlot = -1;
                    for (int slot = 0; slot < 50; slot++)
                    {
                        if (picker.TPlayer.inventory[slot] == null || picker.TPlayer.inventory[slot].IsAir)
                        {
                            emptySlot = slot;
                            break;
                        }
                    }

                    if (emptySlot < 0)
                    {
                        picker.SendMessage("Clear your inventory to pick up gem.", msgR, msgG, msgB);
                        state.PlayerIndex = -1;
                        state.Countdown = 0;
                        continue;
                    }

                    if (state.PlayerIndex == foundPlayerIndex && state.Countdown > 0)
                    {
                        // Same player, decrement countdown
                        state.Countdown--;

                        if (state.Countdown > 0)
                        {
                            // Send countdown to picker
                            picker.SendMessage($"{state.Countdown}...", msgR, msgG, msgB);
                        }
                        else
                        {
                            // Countdown reached 0 — gem picked up!
                            TSPlayer.All.SendMessage($"{picker.Name} has picked up {gemTeamName} team's gem!", msgR, msgG, msgB);
                            TShock.Log.ConsoleInfo($"[CCTG] {picker.Name} picked up {gemTeamName} team's gem lock (index {i})");

                            // Place gem directly into inventory slot
                            picker.TPlayer.inventory[emptySlot].SetDefaults(gemItemId);
                            picker.TPlayer.inventory[emptySlot].stack = 1;
                            picker.SendData(PacketTypes.PlayerSlot, "", picker.Index, emptySlot);

                            // Mark as completed — stop detecting this gem lock
                            state.Completed = true;
                            state.CarrierPlayerIndex = picker.Index;
                            state.PlayerIndex = -1;
                            state.Countdown = 0;

                            // Visual: deactivate gem lock (remove gem)
                            houseBuilder.SetGemLockActivated(i, false);
                        }
                    }
                    else
                    {
                        // New player or new pickup attempt — start countdown
                        state.PlayerIndex = foundPlayerIndex;
                        state.Countdown = 7;
                        state.LastTickTime = DateTime.Now;

                        picker.SendMessage($"Picking up the gem...7...", msgR, msgG, msgB);

                        // Notify others
                        foreach (var p in TShock.Players)
                        {
                            if (p != null && p.Active && p.Index != foundPlayerIndex)
                            {
                                p.SendMessage($"{picker.Name} is picking up {gemTeamName} team's gem", msgR, msgG, msgB);
                            }
                        }
                    }
                }
                else
                {
                    // No valid player — reset silently
                    state.PlayerIndex = -1;
                    state.Countdown = 0;
                }
            }
        }

        // Track gems after pickup: carrier drop, teammate return, ground timeout, distance warnings, victory
        private void CheckGemTracking()
        {
            var gemLocks = houseBuilder.GemLockInfos;
            for (int i = 0; i < gemLocks.Count; i++)
            {
                if (!gemPickupStates.ContainsKey(i))
                    continue;

                var state = gemPickupStates[i];
                if (!state.Completed)
                    continue;

                var info = gemLocks[i];
                string gemTeamName = info.Style == 0 ? "Red" : "Blue";
                byte msgR = info.Style == 0 ? (byte)255 : (byte)50;
                byte msgG = info.Style == 0 ? (byte)50 : (byte)100;
                byte msgB = info.Style == 0 ? (byte)50 : (byte)255;
                int gemItemId = info.Style == 0 ? 1526 : 1524;
                // The team that owns this gem lock (style 0=red lock → red team=1, style 1=blue lock → blue team=3)
                int ownerTeam = info.Style == 0 ? 1 : 3;

                // === Check if carrier still has the gem ===
                if (state.CarrierPlayerIndex >= 0)
                {
                    var carrier = TShock.Players[state.CarrierPlayerIndex];
                    if (carrier == null || !carrier.Active)
                    {
                        // Carrier disconnected — treat as drop, gem returns
                        TSPlayer.All.SendMessage($"{gemTeamName} gem has returned to the base!", msgR, msgG, msgB);
                        TShock.Log.ConsoleInfo($"[CCTG] {gemTeamName} gem returned (carrier disconnected)");
                        ReturnGem(i, state);
                        continue;
                    }

                    if (carrier.TPlayer.dead)
                        continue;

                    bool carrierHasGem = PlayerHasGemItem(carrier.TPlayer, gemItemId);

                    if (!carrierHasGem)
                    {
                        // Carrier no longer has the gem — dropped (death or manual)
                        TSPlayer.All.SendMessage($"{carrier.Name} has dropped the gem!", msgR, msgG, msgB);
                        TShock.Log.ConsoleInfo($"[CCTG] {carrier.Name} dropped {gemTeamName} gem");
                        state.CarrierPlayerIndex = -1;
                        state.IsOnGround = true;
                        state.DroppedTime = DateTime.Now;
                        continue;
                    }

                    // === Check illegal buffs/items on carrier — force drop + return ===
                    string dropReason = null;
                    for (int b = 0; b < Terraria.Player.maxBuffs; b++)
                    {
                        int bt = carrier.TPlayer.buffType[b];
                        if (carrier.TPlayer.buffTime[b] <= 0)
                            continue;

                        if (bt == 8)  { dropReason = "Drop because of Featherfall."; break; }
                        if (bt == 10) { dropReason = "Drop because of Invisibility."; break; }
                        if (bt == 132) { dropReason = "Drop because of bee mount."; break; }
                        if (bt == 370 || bt == 130 || bt == 374 || bt == 378 || bt == 379 ||
                            bt == 380 || bt == 381 || bt == 387 || bt == 388)
                        { dropReason = "Drop because of using mount."; break; }
                        if (bt == 18 && carrier.TPlayer.gravDir == -1f)
                        {
                            dropReason = "Drop because of reversing gravity.";
                            break;
                        }
                        // Minecart buffs
                        if (bt == 118 || bt == 166 || bt == 184 || bt == 208 || bt == 210 ||
                            bt == 220 || bt == 222 || bt == 224 || bt == 226 || bt == 228 ||
                            bt == 231 || bt == 233 || bt == 235 || bt == 237 || bt == 239 ||
                            bt == 241 || bt == 243 || bt == 245 || bt == 247 || bt == 249 ||
                            bt == 251 || bt == 253 || bt == 255 || bt == 269 || bt == 272 ||
                            bt == 338 || bt == 346)
                        {
                            dropReason = "Drop because of taking minecart.";
                            break;
                        }
                    }

                    // Check if carrier is actively using a bucket (controlUseItem = true)
                    if (dropReason == null && carrier.TPlayer.controlUseItem)
                    {
                        int selectedSlot = carrier.TPlayer.selectedItem;
                        if (selectedSlot >= 0 && selectedSlot < carrier.TPlayer.inventory.Length)
                        {
                            var heldItem = carrier.TPlayer.inventory[selectedSlot];
                            if (heldItem != null && !heldItem.IsAir)
                            {
                                if (heldItem.type == 206)
                                    dropReason = "Drop because of using Water Bucket.";
                                else if (heldItem.type == 207)
                                    dropReason = "Drop because of using Lava Bucket.";
                                else if (heldItem.type == 1128)
                                    dropReason = "Drop because of using Honey Bucket.";
                            }
                        }
                    }

                    if (dropReason != null)
                    {
                        // Yellow color for penalty messages
                        carrier.SendMessage(dropReason, 255, 255, 0);
                        TShock.Log.ConsoleInfo($"[CCTG] {carrier.Name} forced gem drop: {dropReason}");

                        RemoveGemItemFromPlayer(carrier, gemItemId);
                        TSPlayer.All.SendMessage($"{gemTeamName} gem has returned to the base!", msgR, msgG, msgB);
                        ReturnGem(i, state);
                        continue;
                    }

                    // === Carrier has gem — check distance to their own house and victory ===
                    int carrierTeam = carrier.TPlayer.team;
                    // Carrier's own house: Red(1)→leftHouseSpawn, Blue(3)→rightHouseSpawn
                    Point carrierHouse = carrierTeam == 1 ? houseBuilder.LeftHouseSpawn : houseBuilder.RightHouseSpawn;

                    if (carrierHouse.X == -1)
                        continue;

                    // Calculate distance in tiles, then convert to feet (1 tile = 2 feet)
                    int carrierTileX = (int)(carrier.TPlayer.position.X / 16f);
                    int distanceTiles = Math.Abs(carrierTileX - carrierHouse.X);
                    int distanceFeet = distanceTiles * 2;

                    // Check if carrier is near their own house spawn point (within 5 tiles)
                    int carrierTileY = (int)(carrier.TPlayer.position.Y / 16f);
                    bool inOwnHouse = Math.Abs(carrierTileX - carrierHouse.X) <= 3 &&
                                     Math.Abs(carrierTileY - carrierHouse.Y) <= 1;

                    if (inOwnHouse)
                    {
                        // Victory! Remove gem from carrier inventory
                        RemoveGemItemFromPlayer(carrier, gemItemId);

                        string winTeamName = carrierTeam == 1 ? "Red" : "Blue";
                        TSPlayer.All.SendMessage($"{winTeamName} team won the game!", msgR, msgG, msgB);
                        TShock.Log.ConsoleInfo($"[CCTG] {winTeamName} team won! {carrier.Name} captured {gemTeamName} gem");
                        ReturnGem(i, state);
                        EndGame();
                        return; // Stop processing further gem locks
                    }

                    // Distance milestone warnings
                    if (!state.Warned450 && distanceFeet <= 450)
                    {
                        state.Warned450 = true;
                        TSPlayer.All.SendMessage($"{carrier.Name} is 450 feet from their base!", msgR, msgG, msgB);
                    }
                    if (!state.Warned300 && distanceFeet <= 300)
                    {
                        state.Warned300 = true;
                        TSPlayer.All.SendMessage($"{carrier.Name} is 300 feet from their base!", msgR, msgG, msgB);
                    }
                    if (!state.Warned150 && distanceFeet <= 150)
                    {
                        state.Warned150 = true;
                        TSPlayer.All.SendMessage($"{carrier.Name} is 150 feet from their base!", msgR, msgG, msgB);
                    }

                    continue;
                }

                // === Gem is on the ground — check for teammate pickup or timeout ===
                if (state.IsOnGround)
                {
                    // Check if a player from the owning team picked up the gem
                    foreach (var player in TShock.Players)
                    {
                        if (player == null || !player.Active)
                            continue;
                        if (player.TPlayer.team != ownerTeam)
                            continue;

                        bool playerHasGem = PlayerHasGemItem(player.TPlayer, gemItemId);
                        if (playerHasGem)
                        {
                            RemoveGemItemFromPlayer(player, gemItemId);
                            // Same team picked up — return to base silently (no "picked up" message)
                            TSPlayer.All.SendMessage($"{gemTeamName} gem has returned to the base!", msgR, msgG, msgB);
                            TShock.Log.ConsoleInfo($"[CCTG] {gemTeamName} gem returned by {player.Name}");
                            ReturnGem(i, state);
                            break;
                        }
                    }

                    // If gem was returned above, skip remaining checks
                    if (!state.Completed)
                        continue;

                    // Check if an opposing team player picked up the gem from ground
                    int opposingTeam = ownerTeam == 1 ? 3 : 1;
                    foreach (var player in TShock.Players)
                    {
                        if (player == null || !player.Active)
                            continue;
                        if (player.TPlayer.team != opposingTeam)
                            continue;

                        if (PlayerHasGemItem(player.TPlayer, gemItemId))
                        {
                            // Opposing player picked up the dropped gem — broadcast pickup
                            state.CarrierPlayerIndex = player.Index;
                            state.IsOnGround = false;
                            // Reset distance milestones for new carrier
                            state.Warned450 = false;
                            state.Warned300 = false;
                            state.Warned150 = false;
                            TSPlayer.All.SendMessage($"{player.Name} has picked up {gemTeamName} team's gem!", msgR, msgG, msgB);
                            TShock.Log.ConsoleInfo($"[CCTG] {player.Name} picked up dropped {gemTeamName} gem from ground");
                            break;
                        }
                    }

                    // If someone picked it up, skip timeout
                    if (state.CarrierPlayerIndex >= 0)
                        continue;

                    // Check ground timeout (2 minutes)
                    if ((DateTime.Now - state.DroppedTime).TotalSeconds >= GEM_GROUND_TIMEOUT_SECONDS)
                    {
                        // Remove any dropped gem items from the world
                        for (int itemIdx = 0; itemIdx < Main.maxItems; itemIdx++)
                        {
                            if (Main.item[itemIdx].active && Main.item[itemIdx].type == gemItemId)
                            {
                                Main.item[itemIdx].TurnToAir();
                                TSPlayer.All.SendData(PacketTypes.ItemDrop, "", itemIdx);
                            }
                        }

                        TSPlayer.All.SendMessage($"{gemTeamName} gem has returned to the base!", msgR, msgG, msgB);
                        TShock.Log.ConsoleInfo($"[CCTG] {gemTeamName} gem returned (ground timeout 2 min)");
                        ReturnGem(i, state);
                    }
                }
            }
        }

        // Reset gem state back to "not picked up" and reactivate gem lock visual
        private bool PlayerHasGemItem(Terraria.Player tplayer, int gemItemId)
        {
            for (int slot = 0; slot < 50; slot++)
            {
                var item = tplayer.inventory[slot];
                if (item != null && !item.IsAir && item.type == gemItemId)
                    return true;
            }
            // slot 58 = mouseItem (dragged with cursor)
            var mouse = tplayer.inventory[58];
            return mouse != null && !mouse.IsAir && mouse.type == gemItemId;
        }

        private void RemoveGemItemFromPlayer(TSPlayer player, int gemItemId)
        {
            for (int slot = 0; slot < 50; slot++)
            {
                var item = player.TPlayer.inventory[slot];
                if (item != null && !item.IsAir && item.type == gemItemId)
                {
                    player.TPlayer.inventory[slot].SetDefaults(0);
                    player.SendData(PacketTypes.PlayerSlot, "", player.Index, slot);
                    return;
                }
            }
            // slot 58 = mouseItem
            var mouse = player.TPlayer.inventory[58];
            if (mouse != null && !mouse.IsAir && mouse.type == gemItemId)
            {
                player.TPlayer.inventory[58].SetDefaults(0);
                player.SendData(PacketTypes.PlayerSlot, "", player.Index, 58);
            }
        }

        private void ReturnGem(int gemLockIndex, GemPickupState state)
        {
            state.Completed = false;
            state.CarrierPlayerIndex = -1;
            state.IsOnGround = false;
            state.DroppedTime = DateTime.MinValue;
            state.PlayerIndex = -1;
            state.Countdown = 0;
            state.Warned450 = false;
            state.Warned300 = false;
            state.Warned150 = false;

            // Visual: reactivate gem lock (gem inserted)
            houseBuilder.SetGemLockActivated(gemLockIndex, true);
        }

        private void ForceReturnGemOnTeleport(TSPlayer player)
        {
            var gemLocks = houseBuilder.GemLockInfos;
            for (int i = 0; i < gemLocks.Count; i++)
            {
                if (!gemPickupStates.ContainsKey(i))
                    continue;
                var state = gemPickupStates[i];
                if (state.CarrierPlayerIndex != player.Index)
                    continue;

                var info = gemLocks[i];
                int gemItemId = info.Style == 0 ? 1526 : 1524;
                string gemTeamName = info.Style == 0 ? "Red" : "Blue";

                RemoveGemItemFromPlayer(player, gemItemId);

                player.SendMessage("Gem returned to base because of teleporting!", 255, 255, 0);
                TSPlayer.All.SendMessage($"{gemTeamName} gem has returned to the base!", 255, 105, 180);
                TShock.Log.ConsoleInfo($"[CCTG] {player.Name} forced gem return on teleport");
                ReturnGem(i, state);
                break;
            }
        }

        // Get current game time string in HH:MM format
        private string GetGameTimeString()
        {
            double currentTime = Main.dayTime ? Main.time : Main.time + 54000.0;
            double totalMinutes = (currentTime / 60.0) + 30; // +30 to match real time (game starts at 4:30 not 4:00)

            int hours = (int)(totalMinutes / 60.0) + 4;
            int minutes = (int)(totalMinutes % 60.0);

            if (hours >= 24)
                hours -= 24;

            return $"{hours:D2}:{minutes:D2}";
        }

        // Get scoreboard text
        private string GetScoreboardText()
        {
            double ticksUntilDawn;

            if (Main.dayTime)
            {
                // Currently day, calculate time to night + entire night
                double dayTicksRemaining = 54000 - Main.time;
                double nightTicks = 32400;
                ticksUntilDawn = dayTicksRemaining + nightTicks;
            }
            else
            {
                // Currently night, calculate remaining time to dawn
                ticksUntilDawn = 32400 - Main.time;
            }

            // Convert ticks to real seconds (60 ticks = 1 real second), then to MM:SS
            int realSecondsUntilDawn = (int)(ticksUntilDawn / 60.0);
            int prepMin = realSecondsUntilDawn / 60;
            int prepSec = realSecondsUntilDawn % 60;

            string currentGameTime = GetGameTimeString();
            string timeUntilDawn = $"{prepMin:D2}:{prepSec:D2}";

            // Return formatted scoreboard text with line breaks
            int redCount = 0, blueCount = 0;
            foreach (var p in TShock.Players)
            {
                if (p == null || !p.Active) continue;
                if (p.TPlayer.team == 1) redCount++;
                else if (p.TPlayer.team == 3) blueCount++;
            }

            string crossingLine = crossingAllowed ? "Everyone can cross sides now!\n" : "";

            string extraLine = "";
            if (!crossingAllowed && !_suddenDeathMode)
            {
                extraLine = $"Prepare Time: {timeUntilDawn}\n";
            }
            else if (crossingAllowed && !_suddenDeathMode)
            {
                double ticksTo1800;
                double target1800 = 48600;
                if (Main.dayTime)
                {
                    if (Main.time < target1800)
                        ticksTo1800 = target1800 - Main.time;
                    else
                        ticksTo1800 = 0;
                }
                else
                {
                    ticksTo1800 = 32400 - Main.time + target1800;
                }
                if (ticksTo1800 > 0 && ticksTo1800 <= 3600)
                {
                    int totalRealSeconds = (int)(ticksTo1800 / 60.0);
                    int sdMin = totalRealSeconds / 60;
                    int sdSec = totalRealSeconds % 60;
                    extraLine = $"Sudden Death: {sdMin:D2}:{sdSec:D2}\n";
                }
            }
            else if (_suddenDeathMode)
            {
                double ticksToEnd;
                double nightTarget = 9000;
                if (Main.dayTime)
                {
                    ticksToEnd = (54000 - Main.time) + nightTarget;
                }
                else
                {
                    if (Main.time < nightTarget)
                        ticksToEnd = nightTarget - Main.time;
                    else
                        ticksToEnd = 0;
                }
                int totalRealSeconds = (int)(ticksToEnd / 60.0);
                int endMin = totalRealSeconds / 60;
                int endSec = totalRealSeconds % 60;
                extraLine = $"Game Ends: {endMin:D2}:{endSec:D2}\n";
            }

            string padding = new string('\n', 12);
            return padding +
                   $"=========================\n" +
                   $"Time: {currentGameTime}\n" +
                   extraLine +
                   $"Red: {redCount}  Blue: {blueCount}\n" +
                   crossingLine +
                   $"=========================\n";
        }

        // Update scoreboard
        private void UpdateScoreboard()
        {
            string scoreboardText = GetScoreboardText();

            // Send to all players
            foreach (var player in TShock.Players)
            {
                if (player != null && player.Active && player.ConnectionAlive)
                {
                    NetMessage.SendData((int)PacketTypes.Status, player.Index, -1,
                        Terraria.Localization.NetworkText.FromLiteral(scoreboardText),
                        0, 0f, 0f, 0f, 0, 0, 0);
                }
            }
        }

    }
}
