using System;
using System.Collections.Generic;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace HouseSidePlugin
{
    [ApiVersion(2, 1)]
    public class HouseSidePlugin : TerrariaPlugin
    {
        public override string Name => "HouseSide";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "stardust";
        public override string Description => "Detects and classifies valid Town NPC houses into LEFT/RIGHT world halves";

        private WorldHouseScanner houseScanner;
        private bool isDirty = false;
        private int rescanTicks = 0;
        private const int RescanIntervalSeconds = 30;
        private const int RescanIntervalTicks = RescanIntervalSeconds * 60;

        public HouseSidePlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            houseScanner = new WorldHouseScanner();

            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInit);
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
            GetDataHandlers.TileEdit += OnTileEdit;

            TShock.Log.Info("[HouseSide] Plugin loaded! House scanning will begin after world initialization.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInit);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                GetDataHandlers.TileEdit -= OnTileEdit;
            }
            base.Dispose(disposing);
        }

        private void OnPostInit(EventArgs args)
        {
            Console.WriteLine("[HouseSide] World loaded, starting initial house scan...");
            TShock.Log.Info("[HouseSide] World loaded, starting initial house scan...");

            houseScanner.ScanWorld();

            Console.WriteLine($"[HouseSide] Initial scan complete:");
            Console.WriteLine($"[HouseSide]   LEFT houses: {houseScanner.LeftHouses.Count}");
            Console.WriteLine($"[HouseSide]   RIGHT houses: {houseScanner.RightHouses.Count}");
            Console.WriteLine($"[HouseSide]   World boundary: X < {houseScanner.WorldBoundaryX} = LEFT, X >= {houseScanner.WorldBoundaryX} = RIGHT");

            TShock.Log.Info($"[HouseSide] Initial scan complete: {houseScanner.LeftHouses.Count} LEFT, {houseScanner.RightHouses.Count} RIGHT");
        }

        private void OnGameUpdate(EventArgs args)
        {
            rescanTicks++;

            if (isDirty && rescanTicks >= RescanIntervalTicks)
            {
                rescanTicks = 0;
                isDirty = false;

                Console.WriteLine("[HouseSide] World marked dirty, rescanning houses...");
                houseScanner.ScanWorld();

                Console.WriteLine($"[HouseSide] Rescan complete:");
                Console.WriteLine($"[HouseSide]   LEFT houses: {houseScanner.LeftHouses.Count}");
                Console.WriteLine($"[HouseSide]   RIGHT houses: {houseScanner.RightHouses.Count}");
            }
        }

        private void OnTileEdit(object sender, GetDataHandlers.TileEditEventArgs args)
        {
            if (args.Handled)
                return;

            // Mark dirty when tiles or walls are modified
            isDirty = true;
        }

        public bool CanNPCArriveOnSide(string npcName, string side)
        {
            if (side == "LEFT")
            {
                return houseScanner.LeftHouses.Count > 0;
            }
            else if (side == "RIGHT")
            {
                return houseScanner.RightHouses.Count > 0;
            }
            return false;
        }

        public List<HouseData> GetHousesForSide(string side)
        {
            if (side == "LEFT")
                return new List<HouseData>(houseScanner.LeftHouses);
            else if (side == "RIGHT")
                return new List<HouseData>(houseScanner.RightHouses);
            return new List<HouseData>();
        }
    }
}
