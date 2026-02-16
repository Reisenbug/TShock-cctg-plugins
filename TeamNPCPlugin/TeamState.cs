using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace TeamNPCPlugin
{
    public class TeamState
    {
        public string TeamName { get; set; }  // "Red" or "Blue"
        public int TeamId { get; set; }        // 1 (Red) or 3 (Blue)

        // List of players on this team
        public List<TSPlayer> Players { get; set; } = new List<TSPlayer>();

        // Team house spawn center coordinates
        public Point SpawnCenter { get; set; }

        // Partition boundaries
        public int MinX { get; set; }
        public int MaxX { get; set; }

        // Housing validator reference
        public HousingValidator HousingValidator { get; set; }

        // NPC manager reference for checking NPC existence
        public NPCTeamManager NPCManager { get; set; }

        // Configuration reference for debug logging
        public bool EnableDebugLog { get; set; } = false;

        // Valid houses assigned to NPCs (NPC name -> house coordinates)
        private Dictionary<string, Point> validHouses = new Dictionary<string, Point>();

        // House assignments with full details
        private Dictionary<string, HouseAssignment> npcHouseAssignments = new Dictionary<string, HouseAssignment>();

        // Computed property: Team's total money (highest among all players)
        public int Money => Players.Any() ?
            Players.Max(p => GetPlayerMoney(p)) : 0;

        // Computed property: Any player used life crystal
        public bool PlayerUsedLifeCrystal => Players.Any(p => p.TPlayer.statLifeMax > 100);

        // Computed property: Any player has a gun
        public bool PlayerHasGun => Players.Any(p =>
            p.TPlayer.inventory.Any(i => i?.useAmmo == AmmoID.Bullet));

        // Computed property: Any player has explosives
        public bool PlayerHasExplosive => Players.Any(p =>
            p.TPlayer.inventory.Any(i => i != null && (
                i.type == ItemID.Bomb ||
                i.type == ItemID.Dynamite ||
                i.type == ItemID.Grenade
            )));

        // Computed property: Any player in dungeon
        public bool PlayerInDungeon => Players.Any(p => p.TPlayer.ZoneDungeon);

        // Computed property: Any player in jungle
        public bool PlayerInJungle => Players.Any(p => p.TPlayer.ZoneJungle);

        // Computed property: Any player in snow biome
        public bool PlayerInSnow => Players.Any(p => p.TPlayer.ZoneSnow);

        // Computed property: Any player in mushroom biome
        public bool PlayerInMushroom => Players.Any(p => p.TPlayer.ZoneGlowshroom);

        // Computed property: Number of NPCs spawned for this team
        public int NPCCount { get; set; } = 0;

        // Check if a specific NPC exists for this team
        public bool HasNPC(string npcName)
        {
            if (NPCManager == null)
                return false;

            return NPCManager.GetNPCCount(npcName, TeamName) > 0;
        }

        // Check if there's a valid, unoccupied house available
        public bool HasValidHouse(string npcName)
        {
            Console.WriteLine($"[TeamState] HasValidHouse called for {npcName} in {TeamName} team");

            // If already assigned a house for this NPC, return true
            if (validHouses.ContainsKey(npcName))
            {
                Console.WriteLine($"[TeamState] {npcName} already has assigned house at {validHouses[npcName]}");
                if (EnableDebugLog)
                {
                    TShock.Log.Info($"[TeamNPC][Debug] {TeamName} team: {npcName} already has assigned house at {validHouses[npcName]}");
                }
                return true;
            }

            Console.WriteLine($"[TeamState] Searching for new house for {npcName}...");
            if (EnableDebugLog)
            {
                TShock.Log.Info($"[TeamNPC][Debug] {TeamName} team: Searching for valid house for {npcName}...");
            }

            // Search for a new valid house
            Point house = FindValidHouse(npcName);
            Console.WriteLine($"[TeamState] FindValidHouse returned: {house}");

            if (house != Point.Zero)
            {
                validHouses[npcName] = house;
                Console.WriteLine($"[TeamState] Found valid house for {npcName} at ({house.X}, {house.Y})");
                if (EnableDebugLog)
                {
                    TShock.Log.Info($"[TeamNPC][Debug] {TeamName} team: Found valid house for {npcName} at ({house.X}, {house.Y})");
                }
                return true;
            }

            Console.WriteLine($"[TeamState] No valid house found for {npcName}");
            if (EnableDebugLog)
            {
                TShock.Log.Warn($"[TeamNPC][Debug] {TeamName} team: No valid house found for {npcName}");
            }

            return false;
        }

        public Point GetHouseLocation(string npcName)
        {
            return validHouses.ContainsKey(npcName) ? validHouses[npcName] : Point.Zero;
        }

        private Point FindValidHouse(string npcName)
        {
            // Generate spiral search pattern from spawn center
            int searchRadius = 150;
            var searchPoints = GenerateSpiralPattern(SpawnCenter, searchRadius);

            if (EnableDebugLog)
            {
                TShock.Log.Info($"[TeamNPC][Debug] {TeamName} team: Searching {searchPoints.Count} points for {npcName}, partition: X={MinX}-{MaxX}");
            }

            int pointsChecked = 0;
            int pointsInBounds = 0;
            int validHouses = 0;
            int occupiedHouses = 0;

            foreach (var point in searchPoints)
            {
                pointsChecked++;

                // Check if point is within partition boundaries
                if (point.X < MinX || point.X > MaxX)
                    continue;

                pointsInBounds++;

                // Use HousingValidator for complete validation
                if (HousingValidator != null)
                {
                    var result = HousingValidator.ValidateHousing(point.X, point.Y);
                    if (result.IsValid)
                    {
                        validHouses++;
                        // Check if this house is already occupied
                        if (!IsHouseOccupied(result.SuggestedHomeTile))
                        {
                            if (EnableDebugLog)
                            {
                                TShock.Log.Info($"[TeamNPC][Debug] {TeamName} team: Found unoccupied valid house for {npcName} at ({result.SuggestedHomeTile.X}, {result.SuggestedHomeTile.Y})");
                            }
                            return result.SuggestedHomeTile;
                        }
                        else
                        {
                            occupiedHouses++;
                        }
                    }
                }
                else
                {
                    // Fallback to simple validation if validator not available
                    if (IsValidHousingAt(point.X, point.Y))
                    {
                        if (EnableDebugLog)
                        {
                            TShock.Log.Info($"[TeamNPC][Debug] {TeamName} team: Found valid house (simple check) for {npcName} at ({point.X}, {point.Y})");
                        }
                        return new Point(point.X, point.Y);
                    }
                }
            }

            if (EnableDebugLog)
            {
                TShock.Log.Warn($"[TeamNPC][Debug] {TeamName} team: House search failed for {npcName}. Stats: {pointsChecked} checked, {pointsInBounds} in bounds, {validHouses} valid houses found, {occupiedHouses} occupied");
            }

            return Point.Zero;  // Not found
        }

        /// <summary>
        /// Generate spiral search pattern from center outward
        /// </summary>
        private List<Point> GenerateSpiralPattern(Point center, int radius)
        {
            List<Point> points = new List<Point>();

            // Spiral outward: right, down, left, up
            for (int r = 1; r <= radius; r++)
            {
                // Top row (left to right)
                for (int x = -r; x <= r; x++)
                    points.Add(new Point(center.X + x, center.Y - r));

                // Right column (top to bottom, excluding corners)
                for (int y = -r + 1; y <= r; y++)
                    points.Add(new Point(center.X + r, center.Y + y));

                // Bottom row (right to left, excluding right corner)
                for (int x = r - 1; x >= -r; x--)
                    points.Add(new Point(center.X + x, center.Y + r));

                // Left column (bottom to top, excluding corners)
                for (int y = r - 1; y > -r; y--)
                    points.Add(new Point(center.X - r, center.Y + y));
            }

            return points;
        }

        private bool IsValidHousingAt(int x, int y)
        {
            // Make sure coordinates are within world bounds
            if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY)
                return false;

            // Check if there's already an NPC living here
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.active && npc.townNPC && !npc.homeless)
                {
                    // Check if this NPC is living at this location
                    if (Math.Abs(npc.homeTileX - x) < 10 && Math.Abs(npc.homeTileY - y) < 10)
                    {
                        return false; // House is occupied
                    }
                }
            }

            // Basic validation: check if there's a solid platform
            // A proper house needs walls, a light source, furniture, etc.
            // For simplicity, we check if there's a solid tile below
            if (y + 1 < Main.maxTilesY)
            {
                var tile = Main.tile[x, y + 1];
                if (tile != null && tile.active() && Main.tileSolid[tile.type])
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetPlayerMoney(TSPlayer player)
        {
            int copper = 0;
            foreach (var item in player.TPlayer.inventory)
            {
                if (item == null) continue;

                if (item.type == ItemID.CopperCoin)
                    copper += item.stack;
                else if (item.type == ItemID.SilverCoin)
                    copper += item.stack * 100;
                else if (item.type == ItemID.GoldCoin)
                    copper += item.stack * 10000;
                else if (item.type == ItemID.PlatinumCoin)
                    copper += item.stack * 1000000;
            }
            return copper;
        }

        /// <summary>
        /// Assign a house to an NPC
        /// </summary>
        public void AssignHouse(string npcName, Point location, Rectangle bounds)
        {
            npcHouseAssignments[npcName] = new HouseAssignment
            {
                NPCName = npcName,
                LocationX = location.X,
                LocationY = location.Y,
                BoundsX = bounds.X,
                BoundsY = bounds.Y,
                BoundsWidth = bounds.Width,
                BoundsHeight = bounds.Height,
                AssignedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Release a house assignment for an NPC
        /// </summary>
        public void ReleaseHouse(string npcName)
        {
            npcHouseAssignments.Remove(npcName);
            validHouses.Remove(npcName);
        }

        /// <summary>
        /// Check if a house location is already occupied
        /// </summary>
        public bool IsHouseOccupied(Point location)
        {
            foreach (var assignment in npcHouseAssignments.Values)
            {
                // Consider occupied if within 5 tiles
                if (Math.Abs(assignment.LocationX - location.X) < 5 &&
                    Math.Abs(assignment.LocationY - location.Y) < 5)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Restore a house assignment (used for persistence loading)
        /// </summary>
        public void RestoreHouseAssignment(string npcName, Point location)
        {
            validHouses[npcName] = location;
        }

        /// <summary>
        /// Get all house assignments (used for persistence saving)
        /// </summary>
        public Dictionary<string, HouseAssignment> GetHouseAssignments()
        {
            return npcHouseAssignments;
        }
    }
}
