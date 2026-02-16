using System;
using System.IO;
using Terraria;
using Terraria.IO;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;

namespace MapChangePlugin
{
    [ApiVersion(2, 1)]
    public class MapChangePlugin : TerrariaPlugin
    {
        public override string Name => "MapChangePlugin";
        public override string Author => "stardust";
        public override string Description => "Swap the current world with another .wld file without restarting the server.";
        public override Version Version => new Version(1, 0, 0);

        private bool _swapping;
        private string _pendingWorldPath;

        public MapChangePlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command("mapchange.swap", SwapWorld, "swapworld"));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == SwapWorld);
            }
            base.Dispose(disposing);
        }

        private void SwapWorld(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Usage: /swapworld <world file path>");
                return;
            }

            string worldPath = string.Join(" ", args.Parameters);

            if (!File.Exists(worldPath))
            {
                args.Player.SendErrorMessage($"World file not found: {worldPath}");
                return;
            }

            if (_swapping)
            {
                args.Player.SendErrorMessage("A world swap is already in progress.");
                return;
            }

            _swapping = true;
            _pendingWorldPath = worldPath;

            TSPlayer.All.SendInfoMessage("World swap in progress, please wait...");

            // Schedule the swap on the main game thread via a one-shot GameUpdate hook
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdateDoSwap);
        }

        private void OnGameUpdateDoSwap(EventArgs args)
        {
            // Deregister immediately so this only runs once
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdateDoSwap);

            string newWorldPath = _pendingWorldPath;
            string oldWorldPath = Main.worldPathName;

            try
            {
                // Step 1: Load metadata for the new world file and update path
                TShock.Log.ConsoleInfo($"[MapChangePlugin] Loading world: {newWorldPath}");
                Main.ActiveWorldFileData = WorldFile.GetAllMetadata(newWorldPath, false);

                // Step 2: Load the new world into memory
                WorldFile.LoadWorld();

                TShock.Log.ConsoleInfo($"[MapChangePlugin] World loaded: {Main.maxTilesX}x{Main.maxTilesY}");

                // Step 3: Update section manager for new world size
                Main.maxSectionsX = Main.maxTilesX / 200;
                Main.maxSectionsY = Main.maxTilesY / 150;
                Netplay.ResetSections();

                // Step 4: Sync all connected players
                foreach (var player in TShock.Players)
                {
                    if (player == null || !player.ConnectionAlive)
                        continue;

                    player.SendData(PacketTypes.WorldInfo);

                    for (int x = 0; x < Main.maxSectionsX; x++)
                    {
                        for (int y = 0; y < Main.maxSectionsY; y++)
                        {
                            Netplay.Clients[player.Index].TileSections[x, y] = false;
                        }
                    }

                    player.Teleport(Main.spawnTileX * 16f, Main.spawnTileY * 16f);
                }

                // Step 5: Sync NPC data
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, NetworkText.Empty, i);
                }

                TShock.Log.ConsoleInfo("[MapChangePlugin] World swap completed successfully.");
                TSPlayer.All.SendSuccessMessage("World swap complete! Welcome to the new world.");
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[MapChangePlugin] World swap failed: {ex.Message}");
                TShock.Log.ConsoleError(ex.StackTrace);

                // Attempt rollback
                try
                {
                    Main.ActiveWorldFileData = WorldFile.GetAllMetadata(oldWorldPath, false);
                    WorldFile.LoadWorld();
                    TSPlayer.All.SendErrorMessage("World swap failed. Reverted to original world.");
                }
                catch (Exception rollbackEx)
                {
                    TShock.Log.ConsoleError($"[MapChangePlugin] Rollback also failed: {rollbackEx.Message}");
                    TSPlayer.All.SendErrorMessage("World swap failed and rollback failed! Server may be unstable.");
                }
            }
            finally
            {
                _swapping = false;
                _pendingWorldPath = null;
            }
        }
    }
}
