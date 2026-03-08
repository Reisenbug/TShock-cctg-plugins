using System;
using Terraria;
using TShockAPI;

namespace cctgPlugin
{
    public struct LocationResult
    {
        public int X;
        public int GroundLevel;
        public int[] SurfaceHeights;
        public bool Success;
    }

    /// <summary>
    /// House location finder - responsible for finding suitable positions for house placement
    /// </summary>
    public class HouseLocationFinder
    {
        private Random _random = new Random();

        private const int MinDistance = 150;
        private const int MaxDistance = 350;
        private const int MaxFlatnessVariation = 2;
        private const int SkyClearHeight = 20;
        private const int SurfaceSearchRange = 50;

        /// <summary>
        /// Find suitable location for house construction
        /// Returns LocationResult with position info, or Success=false if not found
        /// </summary>
        public LocationResult FindLocation(int spawnX, int spawnY, int foundationWidth, int maxHeight, int direction, string side)
        {
            var result = new LocationResult { Success = false };

            // Pick a random start distance within 150-250, then expand outward to 350, then back to 150
            int randomStart = _random.Next(MinDistance, 251);

            // Phase 1: Search outward from randomStart to MaxDistance
            for (int dist = randomStart; dist <= MaxDistance; dist++)
            {
                int candidateX = spawnX + (direction * dist);
                var check = TryCandidate(candidateX, spawnY, foundationWidth, maxHeight, MaxFlatnessVariation, 1.0, side);
                if (check.Success)
                    return check;
            }

            // Phase 2: Search inward from randomStart-1 back to MinDistance
            for (int dist = randomStart - 1; dist >= MinDistance; dist--)
            {
                int candidateX = spawnX + (direction * dist);
                var check = TryCandidate(candidateX, spawnY, foundationWidth, maxHeight, MaxFlatnessVariation, 1.0, side);
                if (check.Success)
                    return check;
            }

            // Phase 3: Forced build mode with relaxed requirements
            TShock.Log.ConsoleWarn($"[CCTG] {side} No suitable position found in normal search, using forced build mode");
            return ForcedBuildMode(spawnX, spawnY, foundationWidth, maxHeight, direction, side);
        }

        /// <summary>
        /// Try a single candidate X position
        /// </summary>
        private LocationResult TryCandidate(int candidateX, int spawnY, int foundationWidth, int maxHeight, int maxFlatness, double skyClearRatio, string side)
        {
            var result = new LocationResult { Success = false };

            int[] surfaceHeights;
            int groundLevel;
            if (!FindFlatGround(candidateX, spawnY, foundationWidth, maxFlatness, out surfaceHeights, out groundLevel))
                return result;

            // House interior must be free of solid blocks — no clearing allowed
            if (!IsInteriorClear(candidateX, groundLevel, maxHeight, foundationWidth))
                return result;

            if (!IsSkyCleared(candidateX, groundLevel, foundationWidth, maxHeight, SkyClearHeight, skyClearRatio))
                return result;

            if (HasExcessiveLiquid(candidateX, groundLevel, foundationWidth, maxHeight))
                return result;

            int distanceToSpawn = Math.Abs(candidateX - Main.spawnTileX);
            TShock.Log.ConsoleInfo($"[CCTG] {side} Found suitable position: X={candidateX}, groundLevel={groundLevel}, distance={distanceToSpawn}");

            result.X = candidateX;
            result.GroundLevel = groundLevel;
            result.SurfaceHeights = surfaceHeights;
            result.Success = true;
            return result;
        }

        /// <summary>
        /// Find flat ground at a candidate X position
        /// Scans each of foundationWidth columns vertically (±SurfaceSearchRange from spawnY) to find surface
        /// Surface = first solid tile with air above
        /// Flatness check: max(surfaces) - min(surfaces) <= maxFlatness
        /// groundLevel = max(surfaceHeights) (deepest surface point)
        /// </summary>
        private bool FindFlatGround(int startX, int searchCenterY, int width, int maxFlatness, out int[] surfaceHeights, out int groundLevel)
        {
            surfaceHeights = new int[width];
            groundLevel = -1;

            int minSurface = int.MaxValue;
            int maxSurface = int.MinValue;

            for (int i = 0; i < width; i++)
            {
                int x = startX + i;
                int surfaceY = -1;

                // Scan vertically from above to below to find surface (first solid tile with air above)
                int scanStart = searchCenterY - SurfaceSearchRange;
                int scanEnd = searchCenterY + SurfaceSearchRange;

                for (int y = scanStart; y <= scanEnd; y++)
                {
                    if (!IsValidCoord(x, y) || !IsValidCoord(x, y - 1))
                        continue;

                    var tile = Main.tile[x, y];
                    var above = Main.tile[x, y - 1];

                    // Must be solid with air above
                    if (tile == null || !tile.active() || !Main.tileSolid[tile.type])
                        continue;
                    if (above != null && above.active() && Main.tileSolid[above.type])
                        continue;

                    // Verify this is real ground, not a floating island:
                    // at least 3 solid blocks below (including this one)
                    const int requiredDepth = 3;
                    bool isRealGround = true;
                    for (int dy = 1; dy < requiredDepth; dy++)
                    {
                        if (!IsValidCoord(x, y + dy))
                        {
                            isRealGround = false;
                            break;
                        }
                        var below = Main.tile[x, y + dy];
                        if (below == null || !below.active() || !Main.tileSolid[below.type])
                        {
                            isRealGround = false;
                            break;
                        }
                    }

                    if (isRealGround && HasClearSkyAbove(x, y))
                    {
                        surfaceY = y;
                        break;
                    }
                }

                if (surfaceY == -1)
                    return false;

                surfaceHeights[i] = surfaceY;

                if (surfaceY < minSurface) minSurface = surfaceY;
                if (surfaceY > maxSurface) maxSurface = surfaceY;
            }

            // Flatness check
            if (maxSurface - minSurface > maxFlatness)
                return false;

            // Ground level = deepest surface point (max Y value, since Y increases downward)
            groundLevel = maxSurface;
            return true;
        }

        /// <summary>
        /// Verify that the sky above the house area is clear of solid blocks
        /// Checks skyClearHeight blocks above the house ceiling
        /// </summary>
        private bool IsSkyCleared(int startX, int groundLevel, int width, int maxHeight, int skyClearHeight, double requiredRatio)
        {
            int ceilingY = groundLevel - maxHeight;
            int checkStartY = ceilingY - skyClearHeight;
            int totalChecked = 0;
            int clearCount = 0;

            for (int x = startX; x < startX + width; x++)
            {
                for (int y = checkStartY; y < ceilingY; y++)
                {
                    if (!IsValidCoord(x, y))
                        continue;

                    totalChecked++;
                    var tile = Main.tile[x, y];
                    if (tile == null || !tile.active() || !Main.tileSolid[tile.type] || tile.type == 189 || tile.type == 196)
                    {
                        clearCount++;
                    }
                }
            }

            if (totalChecked == 0)
                return false;

            double ratio = (double)clearCount / totalChecked;
            return ratio >= requiredRatio;
        }

        /// <summary>
        /// Verify that the house interior area is free of solid blocks.
        /// The house body (excluding foundation) must not contain any blocks —
        /// if it does, this location is rejected rather than clearing them.
        /// Checks the same area as ClearHouseInterior: startX-2 to startX+TotalWidth+2,
        /// from groundLevel-maxHeight-1 to groundLevel-1.
        /// </summary>
        private bool IsInteriorClear(int startX, int groundLevel, int maxHeight, int width = 14)
        {
            int clearStartX = startX - 2;
            int clearEndX = startX + width + 2;

            for (int x = clearStartX; x < clearEndX; x++)
            {
                for (int y = groundLevel - maxHeight - 1; y < groundLevel; y++)
                {
                    if (!IsValidCoord(x, y))
                        continue;

                    var tile = Main.tile[x, y];
                    if (tile != null && tile.active() && Main.tileSolid[tile.type])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Check for excessive liquid in the house area
        /// </summary>
        private bool HasExcessiveLiquid(int startX, int groundLevel, int width, int maxHeight)
        {
            const int liquidCheckHeight = 5;
            int liquidCount = 0;
            int totalChecked = 0;

            for (int x = startX; x < startX + width; x++)
            {
                for (int y = groundLevel - liquidCheckHeight; y < groundLevel; y++)
                {
                    if (!IsValidCoord(x, y))
                        continue;

                    totalChecked++;
                    var tile = Main.tile[x, y];
                    if (tile != null && tile.liquid > 0)
                    {
                        liquidCount++;
                    }
                }
            }

            // If more than 30% of checked tiles have liquid, it's too wet
            return totalChecked > 0 && liquidCount > totalChecked * 0.3;
        }

        /// <summary>
        /// Forced build mode - check center 8 columns for flatness ≤ 3, 50% sky clearance
        /// If interior has obstacles, shift groundLevel up so house sits above them
        /// Searches 150-350 range
        /// </summary>
        private LocationResult ForcedBuildMode(int spawnX, int spawnY, int foundationWidth, int maxHeight, int direction, string side)
        {
            const int relaxedFlatness = 3;
            const int relaxedCheckWidth = 8;
            const double relaxedSkyClearance = 0.5;

            TShock.Log.ConsoleWarn($"[CCTG] {side} Forced build: center {relaxedCheckWidth} cols flatness={relaxedFlatness}, sky clearance=50%");

            for (int dist = MinDistance; dist <= MaxDistance; dist++)
            {
                int candidateX = spawnX + (direction * dist);

                int[] surfaceHeights;
                int groundLevel;
                int centerOffset = (foundationWidth - relaxedCheckWidth) / 2;
                if (!FindFlatGround(candidateX + centerOffset, spawnY, relaxedCheckWidth, relaxedFlatness, out surfaceHeights, out groundLevel))
                    continue;

                int adjustedGroundLevel = AdjustGroundLevelForClearInterior(candidateX, groundLevel, maxHeight, foundationWidth);
                if (adjustedGroundLevel == -1)
                    continue;

                // Reject if the house would be floating: require solid ground within 3 tiles below adjustedGroundLevel
                if (!HasGroundBelow(candidateX, adjustedGroundLevel, foundationWidth, 3))
                    continue;

                if (!IsSkyCleared(candidateX, adjustedGroundLevel, foundationWidth, maxHeight, SkyClearHeight, relaxedSkyClearance))
                    continue;

                if (HasExcessiveLiquid(candidateX, adjustedGroundLevel, foundationWidth, maxHeight))
                    continue;

                int[] fullSurfaceHeights = new int[foundationWidth];
                for (int i = 0; i < foundationWidth; i++)
                    fullSurfaceHeights[i] = adjustedGroundLevel;

                if (adjustedGroundLevel != groundLevel)
                    TShock.Log.ConsoleInfo($"[CCTG] {side} Forced build: shifted groundLevel from {groundLevel} to {adjustedGroundLevel} to avoid obstacles");

                TShock.Log.ConsoleInfo($"[CCTG] {side} Forced build found position at X={candidateX}, groundLevel={adjustedGroundLevel}");
                return new LocationResult
                {
                    X = candidateX,
                    GroundLevel = adjustedGroundLevel,
                    SurfaceHeights = fullSurfaceHeights,
                    Success = true
                };
            }

            TShock.Log.ConsoleError($"[CCTG] {side} Cannot find any suitable position in forced build mode");
            return new LocationResult { Success = false };
        }

        /// <summary>
        /// Find the lowest groundLevel at which the house interior is clear of solid blocks.
        /// Starts from the given groundLevel and shifts upward (decreasing Y) until clear.
        /// Returns -1 if no valid position found within reasonable range.
        /// </summary>
        private int AdjustGroundLevelForClearInterior(int startX, int originalGroundLevel, int maxHeight, int width)
        {
            const int maxShift = 20;

            for (int shift = 0; shift <= maxShift; shift++)
            {
                int testGroundLevel = originalGroundLevel - shift;
                if (testGroundLevel - maxHeight - 1 < 0)
                    return -1;

                if (IsInteriorClear(startX, testGroundLevel, maxHeight, width))
                    return testGroundLevel;
            }

            return -1;
        }

        /// <summary>
        /// Check if coordinates are within world bounds
        /// </summary>
        private bool HasClearSkyAbove(int x, int groundY, int clearHeight = 100)
        {
            for (int dy = 1; dy <= clearHeight; dy++)
            {
                int checkY = groundY - dy;
                if (!IsValidCoord(x, checkY))
                    break;
                var tile = Main.tile[x, checkY];
                if (tile == null || !tile.active() || !Main.tileSolid[tile.type])
                    continue;
                return tile.type == 189 || tile.type == 196;
            }
            return true;
        }

        private bool HasGroundBelow(int startX, int groundLevel, int width, int maxGap)
        {
            for (int i = 0; i < width; i++)
            {
                int x = startX + i;
                bool foundSolid = false;
                for (int dy = 1; dy <= maxGap; dy++)
                {
                    int y = groundLevel + dy;
                    if (!IsValidCoord(x, y)) break;
                    var tile = Main.tile[x, y];
                    if (tile != null && tile.active() && Main.tileSolid[tile.type])
                    {
                        foundSolid = true;
                        break;
                    }
                }
                if (!foundSolid) return false;
            }
            return true;
        }

        private bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }
    }
}
