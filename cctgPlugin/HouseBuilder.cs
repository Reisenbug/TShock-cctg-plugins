using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// Stores gem lock position and derived wall bounds
    /// </summary>
    public struct GemLockInfo
    {
        public int X;
        public int GroundY;
        public int Style;
        public int WallTop => GroundY - 4;
        public int WallBottom => GroundY - 2;
        public int WallLeft => X - 1;
        public int WallRight => X + 1;
    }

    /// <summary>
    /// House builder manager - coordinates location finding and house construction
    /// </summary>
    public class HouseBuilder
    {
        // Protected house areas
        private List<Rectangle> protectedHouseAreas = new List<Rectangle>();

        // Left/right house positions for team teleportation
        private Point leftHouseSpawn = new Point(-1, -1);
        private Point rightHouseSpawn = new Point(-1, -1);

        // House building status
        private bool housesBuilt = false;

        // Gem lock info (position, style, derived bounds)
        private List<GemLockInfo> gemLockInfos = new List<GemLockInfo>();

        // Helper modules
        private HouseLocationFinder locationFinder = new HouseLocationFinder();
        private HouseStructure houseStructure = new HouseStructure();

        // Property accessors
        public List<Rectangle> ProtectedHouseAreas => protectedHouseAreas;
        public Point LeftHouseSpawn => leftHouseSpawn;
        public Point RightHouseSpawn => rightHouseSpawn;
        public bool HousesBuilt => housesBuilt;
        public List<GemLockInfo> GemLockInfos => gemLockInfos;

        /// <summary>
        /// Build houses on both sides of spawn
        /// </summary>
        public void BuildHouses()
        {
            int spawnX = Main.spawnTileX;
            int spawnY = Main.spawnTileY;

            TShock.Log.ConsoleInfo($"[CCTG] Starting house construction at spawn: ({spawnX}, {spawnY})");

            // Left house (Red team) — FindLocation handles distance 150-350
            var leftLocation = BuildSingleHouse(spawnX, spawnY, "left", -1);
            if (leftLocation.X != -1)
            {
                leftHouseSpawn = leftLocation;
                TShock.Log.ConsoleInfo($"[CCTG] Left house (Red team) spawn: ({leftHouseSpawn.X}, {leftHouseSpawn.Y})");
            }

            // Right house (Blue team) — FindLocation handles distance 150-350
            var rightLocation = BuildSingleHouse(spawnX, spawnY, "right", 1);
            if (rightLocation.X != -1)
            {
                rightHouseSpawn = rightLocation;
                TShock.Log.ConsoleInfo($"[CCTG] Right house (Blue team) spawn: ({rightHouseSpawn.X}, {rightHouseSpawn.Y})");
            }

            housesBuilt = true;
            TShock.Log.ConsoleInfo($"[CCTG] House construction complete!");
            TSPlayer.All.SendSuccessMessage("[CCTG] Houses on both sides of spawn built!");
        }

        /// <summary>
        /// Place gem locks for each team, 150-250 tiles from their house in the direction away from spawn
        /// </summary>
        public void PlaceGemLocks()
        {
            if (leftHouseSpawn.X == -1 || rightHouseSpawn.X == -1)
            {
                TShock.Log.ConsoleError("[CCTG] Cannot place gem locks: houses not built");
                return;
            }

            TShock.Log.ConsoleInfo("[CCTG] Starting gem lock placement...");

            // Red team (left house) — gem lock goes further left (direction = -1)
            PlaceOneGemLock(leftHouseSpawn.X, leftHouseSpawn.Y, -1, 0, "Red");

            // Blue team (right house) — gem lock goes further right (direction = +1)
            PlaceOneGemLock(rightHouseSpawn.X, rightHouseSpawn.Y, 1, 1, "Blue");
        }

        private void PlaceOneGemLock(int houseX, int houseY, int direction, int style, string teamName)
        {
            var loc = FindGemLockLocation(houseX, houseY, direction);
            PlaceGemLockStructure(loc.X, loc.Y, style);

            // Verify the gem lock tiles actually exist
            var info = gemLockInfos[gemLockInfos.Count - 1];
            bool verified = false;
            for (int gx = info.WallLeft; gx <= info.WallRight; gx++)
            {
                for (int gy = info.WallTop; gy <= info.WallBottom; gy++)
                {
                    if (IsValidCoord(gx, gy) && Main.tile[gx, gy].active() && Main.tile[gx, gy].type == 440)
                    {
                        verified = true;
                        break;
                    }
                }
                if (verified) break;
            }

            if (verified)
            {
                TShock.Log.ConsoleInfo($"[CCTG] {teamName} team gem lock placed and verified at ({loc.X}, {loc.Y - 3})");
            }
            else
            {
                TShock.Log.ConsoleError($"[CCTG] {teamName} team gem lock FAILED verification, forcing repair");
                RepairGemLocks();
            }
        }

        /// <summary>
        /// Find a suitable location for a gem lock, 150-250 tiles from the house
        /// </summary>
        private Point FindGemLockLocation(int houseX, int houseY, int direction)
        {
            Random rand = new Random();
            int randomStart = rand.Next(150, 201); // 150-200 random start

            // Phase 1: Search outward from randomStart to 250
            for (int dist = randomStart; dist <= 250; dist++)
            {
                int candidateX = houseX + (direction * dist);
                if (!IsXInWorldBounds(candidateX)) continue;
                int groundY = FindSurfaceTile(candidateX, houseY);
                if (groundY != -1 && HasSpaceAbove(candidateX, groundY, 3) && HasFoundation(candidateX, groundY))
                {
                    return new Point(candidateX, groundY);
                }
            }

            // Phase 2: Search inward from randomStart-1 back to 150
            for (int dist = randomStart - 1; dist >= 150; dist--)
            {
                int candidateX = houseX + (direction * dist);
                if (!IsXInWorldBounds(candidateX)) continue;
                int groundY = FindSurfaceTile(candidateX, houseY);
                if (groundY != -1 && HasSpaceAbove(candidateX, groundY, 3) && HasFoundation(candidateX, groundY))
                {
                    return new Point(candidateX, groundY);
                }
            }

            // Phase 3: Expand range 251-400 to ensure gem lock always generates
            for (int dist = 251; dist <= 400; dist++)
            {
                int candidateX = houseX + (direction * dist);
                if (!IsXInWorldBounds(candidateX)) break;
                int groundY = FindSurfaceTile(candidateX, houseY);
                if (groundY != -1 && HasSpaceAbove(candidateX, groundY, 3) && HasFoundation(candidateX, groundY))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Gem lock location found in extended range at dist={dist}");
                    return new Point(candidateX, groundY);
                }
            }

            // Phase 4: Fallback - search 100-149 closer range
            for (int dist = 149; dist >= 100; dist--)
            {
                int candidateX = houseX + (direction * dist);
                if (!IsXInWorldBounds(candidateX)) continue;
                int groundY = FindSurfaceTile(candidateX, houseY);
                if (groundY != -1 && HasSpaceAbove(candidateX, groundY, 3) && HasFoundation(candidateX, groundY))
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Gem lock location found in fallback range at dist={dist}");
                    return new Point(candidateX, groundY);
                }
            }

            // Phase 5: Force place — only require FindSurfaceTile (1 tile ground)
            for (int dist = 100; dist <= 400; dist++)
            {
                int candidateX = houseX + (direction * dist);
                if (!IsXInWorldBounds(candidateX)) break;
                int groundY = FindSurfaceTile(candidateX, houseY);
                if (groundY != -1)
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Gem lock force-placed at dist={dist} (relaxed conditions)");
                    return new Point(candidateX, groundY);
                }
            }

            // Phase 6: Wide Y scan — FindSurfaceTile uses ±50 which may miss drastically different terrain
            TShock.Log.ConsoleWarn("[CCTG] Phase 5 failed, trying wide Y scan (±200)");
            for (int dist = 100; dist <= 400; dist++)
            {
                int candidateX = houseX + (direction * dist);
                if (!IsXInWorldBounds(candidateX)) break;
                int groundY = FindSurfaceTileWide(candidateX);
                if (groundY != -1)
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Gem lock found with wide scan at dist={dist}, groundY={groundY}");
                    return new Point(candidateX, groundY);
                }
            }

            // Phase 7: Absolute fallback — scan down from worldSurface at dist=200 to find existing solid ground
            TShock.Log.ConsoleWarn("[CCTG] All searches failed, scanning down from worldSurface for solid ground");
            int fallbackDist = 200;
            int fallbackX = houseX + (direction * fallbackDist);
            fallbackX = Math.Max(10, Math.Min(Main.maxTilesX - 10, fallbackX));
            int startY = Math.Max(50, (int)Main.worldSurface - 100);
            int endY = Math.Min(Main.maxTilesY - 10, (int)Main.worldSurface + 300);
            for (int y = startY; y <= endY; y++)
            {
                if (!IsValidCoord(fallbackX, y)) continue;
                var tile = Main.tile[fallbackX, y];
                if (tile != null && tile.active() && Main.tileSolid[tile.type])
                {
                    TShock.Log.ConsoleInfo($"[CCTG] Gem lock placed on existing ground at ({fallbackX}, {y})");
                    return new Point(fallbackX, y);
                }
            }

            TShock.Log.ConsoleError("[CCTG] No solid ground found anywhere for gem lock placement");
            return new Point(-1, -1);
        }

        private bool IsXInWorldBounds(int x)
        {
            return x >= 5 && x < Main.maxTilesX - 5;
        }

        /// <summary>
        /// Wide surface scan — searches from sky to underground to find any surface tile
        /// </summary>
        private int FindSurfaceTileWide(int x)
        {
            int scanStart = Math.Max(50, (int)Main.worldSurface - 200);
            int scanEnd = Math.Min(Main.maxTilesY - 10, (int)Main.worldSurface + 200);

            for (int y = scanStart; y <= scanEnd; y++)
            {
                if (!IsValidCoord(x, y) || !IsValidCoord(x, y - 1))
                    continue;

                var tile = Main.tile[x, y];
                var above = Main.tile[x, y - 1];

                if (tile == null || !tile.active() || !Main.tileSolid[tile.type])
                    continue;
                if (tile.type == 192)
                    continue;
                if (above != null && above.active() && Main.tileSolid[above.type])
                    continue;
                if (above != null && above.liquid > 0)
                    continue;

                return y;
            }

            return -1;
        }


        /// <summary>
        /// Find the surface tile at a given X, scanning ±50 from referenceY
        /// Surface = solid tile with air above, at least 3 solid tiles deep
        /// </summary>
        private int FindSurfaceTile(int x, int referenceY)
        {
            int scanStart = referenceY - 50;
            int scanEnd = referenceY + 50;

            for (int y = scanStart; y <= scanEnd; y++)
            {
                if (!IsValidCoord(x, y) || !IsValidCoord(x, y - 1))
                    continue;

                var tile = Main.tile[x, y];
                var above = Main.tile[x, y - 1];

                if (tile == null || !tile.active() || !Main.tileSolid[tile.type])
                    continue;
                if (tile.type == 192)
                    continue;
                if (above != null && above.active() && Main.tileSolid[above.type])
                    continue;
                if (above != null && above.liquid > 0)
                    continue;

                // Verify real ground: at least 3 solid blocks below
                bool isRealGround = true;
                for (int dy = 1; dy < 3; dy++)
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

                if (isRealGround)
                    return y;
            }

            return -1;
        }

        /// <summary>
        /// Check if the 3x3 area above the surface is clear of solid tiles (for wall + gem lock)
        /// Shifted up by 1: checks from groundY-4 to groundY-2, across x-1 to x+1
        /// </summary>
        private bool HasSpaceAbove(int x, int groundY, int requiredHeight)
        {
            for (int cx = x - 1; cx <= x + 1; cx++)
            {
                for (int dy = 2; dy <= requiredHeight + 1; dy++)
                {
                    int checkY = groundY - dy;
                    if (!IsValidCoord(cx, checkY))
                        return false;

                    var tile = Main.tile[cx, checkY];
                    if (tile != null && tile.active() && Main.tileSolid[tile.type])
                        return false;
                    if (tile != null && tile.liquid > 0)
                        return false;
                }
            }
            return true;
        }

        private bool HasFoundation(int x, int groundY)
        {
            for (int fx = x - 1; fx <= x + 1; fx++)
            {
                if (!IsValidCoord(fx, groundY))
                    return false;
                var tile = Main.tile[fx, groundY];
                if (tile == null || !tile.active() || !Main.tileSolid[tile.type])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Place a 3x3 stone wall background and gem lock at the given ground position, shifted up by 1.
        /// groundY is the surface tile Y (solid ground).
        /// Wall goes at (x-1, groundY-4) to (x+1, groundY-2).
        /// Gem lock placed at center, anchored at (x, groundY-2).
        /// Clears any existing tiles in the 3x3 area first to ensure gem lock can be placed.
        /// </summary>
        private void PlaceGemLockStructure(int x, int groundY, int style)
        {
            // Clear trees and vegetation around gem lock area (no item drops)
            int treeRadius = 5;
            for (int tx = x - treeRadius; tx <= x + treeRadius; tx++)
            {
                for (int ty = groundY - 10; ty <= groundY; ty++)
                {
                    if (!IsValidCoord(tx, ty)) continue;
                    var tile = Main.tile[tx, ty];
                    if (tile != null && tile.active())
                    {
                        ushort t = tile.type;
                        if (t == Terraria.ID.TileID.Trees || t == Terraria.ID.TileID.PalmTree ||
                            t == Terraria.ID.TileID.TreeTopaz || t == Terraria.ID.TileID.TreeAmethyst ||
                            t == Terraria.ID.TileID.TreeSapphire || t == Terraria.ID.TileID.TreeEmerald ||
                            t == Terraria.ID.TileID.TreeRuby || t == Terraria.ID.TileID.TreeDiamond ||
                            t == Terraria.ID.TileID.TreeAmber || t == Terraria.ID.TileID.VanityTreeSakura ||
                            t == Terraria.ID.TileID.VanityTreeYellowWillow ||
                            t == Terraria.ID.TileID.Plants || t == Terraria.ID.TileID.Plants2 ||
                            t == Terraria.ID.TileID.LargePiles || t == Terraria.ID.TileID.LargePiles2 ||
                            t == Terraria.ID.TileID.SmallPiles || t == Terraria.ID.TileID.Saplings ||
                            t == Terraria.ID.TileID.MushroomPlants || t == Terraria.ID.TileID.MushroomTrees)
                        {
                            WorldGen.KillTile(tx, ty, false, false, true);
                        }
                    }
                }
            }
            TSPlayer.All.SendTileRect((short)(x - treeRadius), (short)(groundY - 10),
                (byte)(treeRadius * 2 + 1), 11);

            // Clear dropped items in the area
            float leftPx = (x - treeRadius) * 16f;
            float rightPx = (x + treeRadius + 1) * 16f;
            float topPx = (groundY - 10) * 16f;
            float bottomPx = (groundY + 1) * 16f;
            for (int idx = 0; idx < Main.item.Length; idx++)
            {
                var item = Main.item[idx];
                if (item != null && item.active &&
                    item.position.X >= leftPx && item.position.X <= rightPx &&
                    item.position.Y >= topPx && item.position.Y <= bottomPx)
                {
                    Main.item[idx].TurnToAir();
                    TSPlayer.All.SendData(PacketTypes.ItemDrop, "", idx);
                }
            }

            // Wall and gem lock area: shifted up by 1 from surface
            // 3x3 area: (x-1, groundY-4) to (x+1, groundY-2)
            int wallTop = groundY - 4;
            int wallBottom = groundY - 2;

            int clearRadius = 15;
            int clearLeft = x - clearRadius;
            int clearRight = x + clearRadius;
            int clearTopLiquid = groundY - clearRadius;
            int clearBottomLiquid = groundY + clearRadius;
            for (int cx = clearLeft; cx <= clearRight; cx++)
            {
                for (int cy = clearTopLiquid; cy <= clearBottomLiquid; cy++)
                {
                    if (IsValidCoord(cx, cy))
                    {
                        Main.tile[cx, cy].liquid = 0;
                        Main.tile[cx, cy].liquidType(0);
                    }
                }
            }
            TSPlayer.All.SendTileRect((short)clearLeft, (short)clearTopLiquid,
                (byte)(clearRight - clearLeft + 1), (byte)(clearBottomLiquid - clearTopLiquid + 1));

            for (int cx = x - 1; cx <= x + 1; cx++)
            {
                for (int cy = wallTop; cy <= wallBottom; cy++)
                {
                    if (IsValidCoord(cx, cy))
                    {
                        Main.tile[cx, cy].ClearTile();
                    }
                }
            }

            // Place 3x3 stone wall background
            for (int wx = x - 1; wx <= x + 1; wx++)
            {
                for (int wy = wallTop; wy <= wallBottom; wy++)
                {
                    if (IsValidCoord(wx, wy))
                    {
                        Main.tile[wx, wy].wall = WallID.Stone;
                    }
                }
            }

            int wallCenter = groundY - 3;
            WorldGen.PlaceObject(x, wallCenter, 440, false, style);

            // Verify gem lock was placed; if not, manually set the tiles
            bool gemLockPlaced = false;
            for (int gx = x - 1; gx <= x + 1; gx++)
            {
                for (int gy = wallTop; gy <= wallBottom; gy++)
                {
                    if (IsValidCoord(gx, gy) && Main.tile[gx, gy].active() && Main.tile[gx, gy].type == 440)
                    {
                        gemLockPlaced = true;
                        break;
                    }
                }
                if (gemLockPlaced) break;
            }

            if (!gemLockPlaced)
            {
                TShock.Log.ConsoleWarn($"[CCTG] WorldGen.PlaceObject failed for gem lock at ({x},{wallCenter}), manually placing tiles");
                // Manually place 3x3 gem lock tiles (type 440)
                for (int gx = x - 1; gx <= x + 1; gx++)
                {
                    for (int gy = wallTop; gy <= wallBottom; gy++)
                    {
                        if (IsValidCoord(gx, gy))
                        {
                            Main.tile[gx, gy].type = 440;
                            Main.tile[gx, gy].active(true);
                            Main.tile[gx, gy].slope(0);
                            Main.tile[gx, gy].halfBrick(false);
                            // frameX: column 0=0, 1=18, 2=36; offset by style * 54
                            Main.tile[gx, gy].frameX = (short)((gx - (x - 1)) * 18 + style * 54);
                            // frameY: row 0=0, 1=18, 2=36
                            Main.tile[gx, gy].frameY = (short)((gy - wallTop) * 18);
                        }
                    }
                }
            }

            // Set gem lock to activated state (gem inserted): frameY += 54 on all tiles
            // Gem lock is 3x3 tiles, each tile frame is 18x18 pixels
            // Inactive frameY: row0=0, row1=18, row2=36
            // Activated frameY: row0=54, row1=72, row2=90 (offset by 54)
            for (int gx = x - 1; gx <= x + 1; gx++)
            {
                for (int gy = wallTop; gy <= wallBottom; gy++)
                {
                    if (IsValidCoord(gx, gy))
                    {
                        var tile = Main.tile[gx, gy];
                        if (tile != null && tile.active() && tile.type == 440)
                        {
                            tile.frameY = (short)(tile.frameY + 54);
                        }
                    }
                }
            }

            // Send tile update to all clients (cover the full area with margin)
            int rectX = x - 2;
            int rectY = wallTop - 1;
            int rectWidth = 5;
            int rectHeight = 6;
            TSPlayer.All.SendTileRect((short)rectX, (short)rectY, (byte)rectWidth, (byte)rectHeight);

            // Record position for cleanup on /end
            gemLockInfos.Add(new GemLockInfo { X = x, GroundY = groundY, Style = style });

            TShock.Log.ConsoleInfo($"[CCTG] Gem lock structure placed at ({x}, {wallCenter}), wall ({x - 1},{wallTop})-({x + 1},{wallBottom}), style={style}");
        }

        /// <summary>
        /// Clear all houses
        /// </summary>
        public void ClearHouses()
        {
            if (protectedHouseAreas.Count == 0)
            {
                TShock.Log.ConsoleInfo("[CCTG] No houses to clear");
                return;
            }

            TShock.Log.ConsoleInfo($"[CCTG] Starting to clear houses, total {protectedHouseAreas.Count} areas");

            foreach (var houseArea in protectedHouseAreas)
            {
                // Clear only the house area itself — no massive sky clearing
                int clearStartX = houseArea.X - 2;
                int clearEndX = houseArea.X + houseArea.Width + 2;
                int clearStartY = houseArea.Y - 1;
                int clearEndY = houseArea.Y + houseArea.Height;

                for (int x = clearStartX; x < clearEndX; x++)
                {
                    for (int y = clearStartY; y < clearEndY; y++)
                    {
                        if (IsValidCoord(x, y))
                        {
                            Main.tile[x, y].ClearEverything();
                        }
                    }
                }

                // Refresh area
                TSPlayer.All.SendTileRect((short)clearStartX, (short)clearStartY,
                    (byte)(clearEndX - clearStartX), (byte)(clearEndY - clearStartY));
            }

            // Clear gem locks
            ClearGemLocks();

            // Delete TShock regions
            DeleteHouseRegions();

            // Clear protected house areas list
            protectedHouseAreas.Clear();

            // Reset house positions
            leftHouseSpawn = new Point(-1, -1);
            rightHouseSpawn = new Point(-1, -1);

            // Reset house building status
            housesBuilt = false;

            TShock.Log.ConsoleInfo("[CCTG] Houses cleared");
        }

        /// <summary>
        /// Clear all placed gem lock structures (3x3 wall + gem lock)
        /// </summary>
        private void ClearGemLocks()
        {
            if (gemLockInfos.Count == 0)
                return;

            foreach (var info in gemLockInfos)
            {
                // Clear tiles and walls in the 3x3 area
                for (int cx = info.WallLeft; cx <= info.WallRight; cx++)
                {
                    for (int cy = info.WallTop; cy <= info.WallBottom; cy++)
                    {
                        if (IsValidCoord(cx, cy))
                        {
                            Main.tile[cx, cy].ClearEverything();
                        }
                    }
                }

                // Send tile update to clients
                int rectX = info.X - 2;
                int rectY = info.WallTop - 1;
                TSPlayer.All.SendTileRect((short)rectX, (short)rectY, 5, 6);

                TShock.Log.ConsoleInfo($"[CCTG] Gem lock cleared at ({info.X}, {info.GroundY})");
            }

            gemLockInfos.Clear();
        }

        /// <summary>
        /// Repair gem lock structures if any tiles/walls are missing (e.g. from explosions)
        /// </summary>
        public void RepairGemLocks()
        {
            foreach (var info in gemLockInfos)
            {
                bool needsRepair = false;

                // Check 3x3 wall area and gem lock tile
                for (int cx = info.WallLeft; cx <= info.WallRight; cx++)
                {
                    for (int cy = info.WallTop; cy <= info.WallBottom; cy++)
                    {
                        if (!IsValidCoord(cx, cy))
                            continue;

                        var tile = Main.tile[cx, cy];

                        // Check wall
                        if (tile.wall != WallID.Stone)
                        {
                            needsRepair = true;
                            break;
                        }

                        // Check gem lock tile (type 440)
                        if (!tile.active() || tile.type != 440)
                        {
                            needsRepair = true;
                            break;
                        }
                    }
                    if (needsRepair) break;
                }

                if (!needsRepair)
                    continue;

                // Rebuild: clear tiles, place walls, place gem lock, set frameY
                for (int cx = info.WallLeft; cx <= info.WallRight; cx++)
                {
                    for (int cy = info.WallTop; cy <= info.WallBottom; cy++)
                    {
                        if (IsValidCoord(cx, cy))
                        {
                            Main.tile[cx, cy].ClearTile();
                        }
                    }
                }

                // Place walls
                for (int wx = info.WallLeft; wx <= info.WallRight; wx++)
                {
                    for (int wy = info.WallTop; wy <= info.WallBottom; wy++)
                    {
                        if (IsValidCoord(wx, wy))
                        {
                            Main.tile[wx, wy].wall = WallID.Stone;
                        }
                    }
                }

                // Place gem lock (anchor at center)
                int wallCenter = info.GroundY - 3;
                WorldGen.PlaceObject(info.X, wallCenter, 440, false, info.Style);

                // Set activated state (frameY += 54)
                for (int gx = info.WallLeft; gx <= info.WallRight; gx++)
                {
                    for (int gy = info.WallTop; gy <= info.WallBottom; gy++)
                    {
                        if (IsValidCoord(gx, gy))
                        {
                            var tile = Main.tile[gx, gy];
                            if (tile != null && tile.active() && tile.type == 440)
                            {
                                tile.frameY = (short)(tile.frameY + 54);
                            }
                        }
                    }
                }

                // Send tile update
                int rectX = info.X - 2;
                int rectY = info.WallTop - 1;
                TSPlayer.All.SendTileRect((short)rectX, (short)rectY, 5, 6);

                TShock.Log.ConsoleInfo($"[CCTG] Repaired gem lock at ({info.X}, {info.GroundY})");
            }
        }

        /// <summary>
        /// Set gem lock visual state: activated (gem inserted) or deactivated (gem removed).
        /// activated=true: frameY has +54 offset. activated=false: base frameY without offset.
        /// </summary>
        public void SetGemLockActivated(int gemLockIndex, bool activated)
        {
            if (gemLockIndex < 0 || gemLockIndex >= gemLockInfos.Count)
                return;

            var info = gemLockInfos[gemLockIndex];

            for (int gx = info.WallLeft; gx <= info.WallRight; gx++)
            {
                for (int gy = info.WallTop; gy <= info.WallBottom; gy++)
                {
                    if (!IsValidCoord(gx, gy))
                        continue;

                    var tile = Main.tile[gx, gy];
                    if (tile == null || !tile.active() || tile.type != 440)
                        continue;

                    // Base frameY values: row0=0, row1=18, row2=36
                    // Activated: row0=54, row1=72, row2=90
                    int baseFrameY = tile.frameY >= 54 ? tile.frameY - 54 : tile.frameY;
                    tile.frameY = activated ? (short)(baseFrameY + 54) : (short)baseFrameY;
                }
            }

            // Send tile update
            int rectX = info.X - 2;
            int rectY = info.WallTop - 1;
            TSPlayer.All.SendTileRect((short)rectX, (short)rectY, 5, 6);
        }

        /// <summary>
        /// Check if a tile position is inside any gem lock's 3x3 wall area.
        /// Returns the index into gemLockInfos, or -1 if not in any area.
        /// </summary>
        public int IsInGemLockArea(int tileX, int tileY)
        {
            for (int i = 0; i < gemLockInfos.Count; i++)
            {
                var info = gemLockInfos[i];
                if (tileX >= info.WallLeft && tileX <= info.WallRight &&
                    tileY >= info.WallTop && tileY <= info.WallBottom)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Check and repair missing furniture and doors
        /// </summary>
        public void RepairFurniture()
        {
            houseStructure.RepairFurniture();
        }

        /// <summary>
        /// Clear mobs from houses
        /// </summary>
        public void ClearMobsInHouses()
        {
            if (protectedHouseAreas.Count == 0)
                return;

            int clearedCount = 0;

            // Apply to all NPCs
            for (int i = 0; i < Main.npc.Length; i++)
            {
                var npc = Main.npc[i];

                // Skip inactive NPCs
                if (npc == null || !npc.active)
                    continue;

                // Skip friendly, town NPCs, and bosses
                if (npc.friendly || npc.townNPC || npc.boss)
                    continue;

                // Get NPC tile position
                int npcTileX = (int)(npc.position.X / 16);
                int npcTileY = (int)(npc.position.Y / 16);

                // Check if NPC is within any protected house area
                foreach (var houseArea in protectedHouseAreas)
                {
                    if (houseArea.Contains(npcTileX, npcTileY))
                    {
                        // Clear Npc
                        npc.active = false;
                        npc.type = 0;

                        // Update NPC state to clients
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);

                        clearedCount++;
                        break; // No need to check other areas
                    }
                }
            }
        }

        /// <summary>
        /// Build a single house at specified position
        /// Direction: -1 for left, 1 for right
        /// Returns spawn point inside the house
        /// </summary>
        private Point BuildSingleHouse(int spawnX, int spawnY, string side, int direction)
        {
            const int foundationWidth = 16; // foundation tiles that must contact ground
            const int maxHeight = 11; // highest height

            // Find suitable location — returns LocationResult with X, GroundLevel, SurfaceHeights
            var result = locationFinder.FindLocation(spawnX, spawnY, foundationWidth, maxHeight, direction, side);

            int startX;
            int groundLevel;
            int[] surfaceHeights;

            if (!result.Success)
            {
                TShock.Log.ConsoleError($"[CCTG] {side} Failed to find suitable location, using default");
                startX = spawnX + (direction * 200);
                groundLevel = spawnY;
                surfaceHeights = new int[foundationWidth];
                for (int i = 0; i < foundationWidth; i++)
                    surfaceHeights[i] = spawnY;
            }
            else
            {
                // Use result.X — fixes the X propagation bug where centerX was never updated
                startX = result.X;
                groundLevel = result.GroundLevel;
                surfaceHeights = result.SurfaceHeights;
            }

            // Build the house structure with surface heights for grounding
            Point spawnPoint = houseStructure.BuildHouse(startX, groundLevel, direction, side, surfaceHeights);

            // Get and save protected areas
            var (leftRoom, rightRoom) = houseStructure.GetProtectedAreas(startX, groundLevel, direction);
            protectedHouseAreas.Add(leftRoom);
            protectedHouseAreas.Add(rightRoom);

            TShock.Log.ConsoleInfo($"[CCTG] {side} House protected areas recorded:");
            TShock.Log.ConsoleInfo($"[CCTG] Left room protected area: ({leftRoom.X}, {leftRoom.Y}, {leftRoom.Width}x{leftRoom.Height})");
            TShock.Log.ConsoleInfo($"[CCTG] Right room protected area: ({rightRoom.X}, {rightRoom.Y}, {rightRoom.Width}x{rightRoom.Height})");

            string teamName = direction < 0 ? "red" : "blue";
            CreateHouseRegion($"cctg_house_{teamName}_left", leftRoom);
            CreateHouseRegion($"cctg_house_{teamName}_right", rightRoom);

            return spawnPoint;
        }

        /// <summary>
        /// Check if coordinates are within world bounds
        /// </summary>
        private bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }

        private static readonly string[] HouseRegionNames = new[]
        {
            "cctg_house_red_left", "cctg_house_red_right",
            "cctg_house_blue_left", "cctg_house_blue_right"
        };

        private void CreateHouseRegion(string name, Rectangle area)
        {
            TShock.Regions.DeleteRegion(name);
            if (TShock.Regions.AddRegion(area.X, area.Y, area.Width, area.Height, name, "cctg", Main.worldID.ToString()))
            {
                TShock.Log.ConsoleInfo($"[CCTG] Created protected region '{name}' ({area.X},{area.Y} {area.Width}x{area.Height})");
            }
        }

        private void DeleteHouseRegions()
        {
            foreach (var name in HouseRegionNames)
            {
                if (TShock.Regions.DeleteRegion(name))
                    TShock.Log.ConsoleInfo($"[CCTG] Deleted protected region '{name}'");
            }
        }
    }
}
