using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace TeamNPCPlugin
{
    /// <summary>
    /// Housing validation result
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string FailureReason { get; set; }
        public Rectangle RoomBounds { get; set; }
        public Point SuggestedHomeTile { get; set; }
        public int TileCount { get; set; }

        public ValidationResult()
        {
            IsValid = false;
            FailureReason = "";
            RoomBounds = Rectangle.Empty;
            SuggestedHomeTile = Point.Zero;
            TileCount = 0;
        }
    }

    /// <summary>
    /// Housing validator - implements official Terraria housing standards
    /// </summary>
    public class HousingValidator
    {
        public bool EnableDebugLog { get; set; } = false;

        // Lock object for WorldGen.StartRoomCheck() to prevent race conditions
        private static readonly object roomCheckLock = new object();

        // Light source tile IDs
        private static readonly HashSet<int> LightSourceTiles = new()
        {
            TileID.Torches, TileID.Candles, TileID.Chandeliers,
            TileID.Lamps, TileID.ChineseLanterns, TileID.HangingLanterns,
            TileID.Candelabras, TileID.WaterCandle, TileID.PeaceCandle,
            TileID.Campfire, TileID.Fireplace, TileID.ChimneySmoke,
            TileID.Lampposts
        };

        // Flat surface furniture tile IDs
        private static readonly HashSet<int> FlatSurfaceTiles = new()
        {
            TileID.Tables, TileID.WorkBenches, TileID.Dressers,
            TileID.Pianos, TileID.Bookcases, TileID.Bathtubs,
            TileID.AlchemyTable, TileID.Tables2, TileID.PicnicTable
        };

        // Comfort furniture tile IDs
        private static readonly HashSet<int> ComfortTiles = new()
        {
            TileID.Chairs, TileID.Benches, TileID.Beds,
            TileID.Thrones, TileID.Toilets,
            TileID.PicnicTable // Picnic table counts as both surface and comfort
        };

        // Naturally generated walls (invalid for housing)
        private static readonly HashSet<int> NaturalWalls = new()
        {
            WallID.Dirt, WallID.Stone, WallID.EbonstoneUnsafe,
            WallID.BlueDungeonUnsafe, WallID.GreenDungeonUnsafe,
            WallID.PinkDungeonUnsafe, WallID.CaveUnsafe,
            WallID.Cave2Unsafe, WallID.Cave3Unsafe, WallID.Cave4Unsafe,
            WallID.Cave5Unsafe, WallID.Cave6Unsafe, WallID.Cave7Unsafe,
            WallID.Cave8Unsafe, WallID.SpiderUnsafe, WallID.GrassUnsafe,
            WallID.JungleUnsafe, WallID.FlowerUnsafe, WallID.Grass,
            WallID.CorruptGrassUnsafe, WallID.HallowedGrassUnsafe,
            WallID.CrimsonGrassUnsafe
        };

        /// <summary>
        /// Validates housing at specified location
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>Validation result</returns>
        public ValidationResult ValidateHousing(int x, int y)
        {
            var result = new ValidationResult();

            if (EnableDebugLog)
            {
                Console.WriteLine($"[HousingValidator][Debug] Checking housing at ({x}, {y})");
            }

            // Check if coordinates are within world bounds (at least 10 tiles from edge)
            if (x < 10 || x >= Main.maxTilesX - 10 || y < 10 || y >= Main.maxTilesY - 10)
            {
                result.FailureReason = "Location too close to world edge";
                if (EnableDebugLog)
                {
                    Console.WriteLine($"[HousingValidator][Debug] ❌ Failed: {result.FailureReason}");
                }
                return result;
            }

            // Use WorldGen.StartRoomCheck for basic room validation
            if (!CheckRoomWithWorldGen(x, y, out Rectangle bounds))
            {
                result.FailureReason = "Not a valid room structure";
                if (EnableDebugLog)
                {
                    Console.WriteLine($"[HousingValidator][Debug] ❌ Failed: {result.FailureReason} (WorldGen.StartRoomCheck returned false)");
                }
                return result;
            }

            if (EnableDebugLog)
            {
                Console.WriteLine($"[HousingValidator][Debug] ✅ Found room structure: {bounds.Width}x{bounds.Height} = {bounds.Width * bounds.Height} tiles");
            }

            result.RoomBounds = bounds;
            result.TileCount = bounds.Width * bounds.Height;

            // Check room size (60-750 tiles)
            if (!CheckRoomSize(bounds, out string sizeError))
            {
                result.FailureReason = sizeError;
                if (EnableDebugLog)
                {
                    Console.WriteLine($"[HousingValidator][Debug] ❌ Failed: {result.FailureReason}");
                }
                return result;
            }

            if (EnableDebugLog)
            {
                Console.WriteLine($"[HousingValidator][Debug] ✅ Room size OK");
            }

            // Check required furniture
            if (!CheckRequiredFurniture(bounds, out string furnitureError))
            {
                result.FailureReason = furnitureError;
                if (EnableDebugLog)
                {
                    Console.WriteLine($"[HousingValidator][Debug] ❌ Failed: {result.FailureReason}");
                }
                return result;
            }

            if (EnableDebugLog)
            {
                Console.WriteLine($"[HousingValidator][Debug] ✅ Required furniture present");
            }

            // Check background walls (must be player-placed)
            if (!CheckBackgroundWalls(bounds))
            {
                result.FailureReason = "Room has natural walls or missing walls";
                if (EnableDebugLog)
                {
                    Console.WriteLine($"[HousingValidator][Debug] ❌ Failed: {result.FailureReason}");
                }
                return result;
            }

            if (EnableDebugLog)
            {
                Console.WriteLine($"[HousingValidator][Debug] ✅ Background walls OK");
            }

            // Find best home tile position
            Point homeTile = FindBestHomeTile(bounds);
            if (homeTile == Point.Zero)
            {
                result.FailureReason = "No valid home tile found in room";
                if (EnableDebugLog)
                {
                    Console.WriteLine($"[HousingValidator][Debug] ❌ Failed: {result.FailureReason}");
                }
                return result;
            }

            if (EnableDebugLog)
            {
                Console.WriteLine($"[HousingValidator][Debug] ✅ Valid home tile found at ({homeTile.X}, {homeTile.Y})");
                Console.WriteLine($"[HousingValidator][Debug] 🎉 Housing validation PASSED!");
            }

            // All checks passed
            result.IsValid = true;
            result.SuggestedHomeTile = homeTile;
            result.FailureReason = "";

            return result;
        }

        /// <summary>
        /// Use WorldGen.StartRoomCheck for basic room validation
        /// Thread-safe: Uses lock to prevent race conditions on WorldGen static fields
        /// </summary>
        private bool CheckRoomWithWorldGen(int x, int y, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;

            try
            {
                // Lock to prevent race conditions on WorldGen.roomX1/X2/Y1/Y2
                lock (roomCheckLock)
                {
                    // Call Terraria's room check method
                    if (WorldGen.StartRoomCheck(x, y))
                    {
                        // Get room boundaries from WorldGen static fields IMMEDIATELY
                        // Must read within lock before other code can overwrite
                        int x1 = WorldGen.roomX1;
                        int y1 = WorldGen.roomY1;
                        int x2 = WorldGen.roomX2;
                        int y2 = WorldGen.roomY2;

                        bounds = new Rectangle(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"[HousingValidator] WorldGen.StartRoomCheck failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Check room size (60-750 tiles)
        /// </summary>
        private bool CheckRoomSize(Rectangle bounds, out string error)
        {
            error = "";
            int tileCount = bounds.Width * bounds.Height;

            if (tileCount < 60)
            {
                error = $"Room too small ({tileCount} tiles, minimum 60)";
                return false;
            }

            if (tileCount > 750)
            {
                error = $"Room too large ({tileCount} tiles, maximum 750)";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Check required furniture (light source, flat surface, comfort item)
        /// </summary>
        private bool CheckRequiredFurniture(Rectangle bounds, out string missingItem)
        {
            missingItem = "";

            bool hasLight = HasLightSource(bounds);
            bool hasSurface = HasFlatSurface(bounds);
            bool hasComfort = HasComfortItem(bounds);

            if (!hasLight)
            {
                missingItem = "Missing light source (torch, candle, lamp, etc.)";
                return false;
            }

            if (!hasSurface)
            {
                missingItem = "Missing flat surface item (table, workbench, etc.)";
                return false;
            }

            if (!hasComfort)
            {
                missingItem = "Missing comfort item (chair, bed, etc.)";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Check if room has a light source
        /// </summary>
        private bool HasLightSource(Rectangle bounds)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile != null && tile.active() && LightSourceTiles.Contains(tile.type))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Check if room has a flat surface furniture
        /// </summary>
        private bool HasFlatSurface(Rectangle bounds)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile != null && tile.active() && FlatSurfaceTiles.Contains(tile.type))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Check if room has a comfort furniture
        /// </summary>
        private bool HasComfortItem(Rectangle bounds)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile != null && tile.active() && ComfortTiles.Contains(tile.type))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Check if background walls are player-placed (not natural walls)
        /// </summary>
        private bool CheckBackgroundWalls(Rectangle bounds)
        {
            // Check if all positions in room have background walls and they're not natural
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile == null)
                        return false;

                    // No wall or natural wall both count as invalid
                    if (tile.wall == 0 || NaturalWalls.Contains(tile.wall))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Find the best home tile position in the room
        /// </summary>
        private Point FindBestHomeTile(Rectangle bounds)
        {
            int bestScore = -1;
            Point bestTile = Point.Zero;

            // Search floor positions in the center area of the room
            for (int x = bounds.Left + 1; x < bounds.Right - 1; x++)
            {
                for (int y = bounds.Top + 2; y < bounds.Bottom - 1; y++)
                {
                    int score = CalculateHomeTileScore(x, y);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTile = new Point(x, y);
                    }
                }
            }

            // Need at least score of 1 to be valid
            return bestScore >= 1 ? bestTile : Point.Zero;
        }

        /// <summary>
        /// Calculate home tile score based on Terraria official scoring system:
        /// - Base score: 50
        /// - Solid tile: -5
        /// - Empty space: +5
        /// - Closed door: -20
        /// - Must have standing room (3 tiles above with no solid blocks)
        /// </summary>
        private int CalculateHomeTileScore(int x, int y)
        {
            // Check if there's enough standing room (3 tiles above)
            for (int dy = -3; dy < 0; dy++)
            {
                if (y + dy < 0 || y + dy >= Main.maxTilesY)
                    return 0;

                var aboveTile = Main.tile[x, y + dy];
                if (aboveTile != null && aboveTile.active() && Main.tileSolid[aboveTile.type])
                {
                    return 0; // No standing room
                }
            }

            int score = 50; // Base score

            // Check this position and adjacent left/right positions
            for (int dx = -1; dx <= 1; dx++)
            {
                int checkX = x + dx;
                if (checkX < 0 || checkX >= Main.maxTilesX)
                    continue;

                var tile = Main.tile[checkX, y];
                if (tile == null)
                    continue;

                if (tile.active() && Main.tileSolid[tile.type])
                {
                    score -= 5; // Solid tile penalty

                    // Check if it's a closed door
                    if (tile.type == TileID.ClosedDoor)
                    {
                        score -= 20;
                    }
                }
                else if (!tile.active())
                {
                    score += 5; // Empty space bonus
                }
            }

            return score;
        }
    }
}
