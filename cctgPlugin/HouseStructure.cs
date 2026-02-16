using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// House structure builder - responsible for constructing house shapes and furniture
    /// </summary>
    public class HouseStructure
    {
        private const int TotalWidth = 14; // 5 + 10 - 1 = 14
        private const int MaxHeight = 11; // highest height

        /// <summary>
        /// Build a complete house structure
        /// </summary>
        /// <param name="startX">Starting X coordinate</param>
        /// <param name="groundLevel">Ground level Y coordinate</param>
        /// <param name="direction">Direction: -1 for red team (left), 1 for blue team (right)</param>
        /// <param name="side">Side name for logging</param>
        /// <param name="surfaceHeights">Per-column surface heights from FindFlatGround (length = foundation width)</param>
        /// <returns>Spawn point inside the house</returns>
        public Point BuildHouse(int startX, int groundLevel, int direction, string side, int[] surfaceHeights)
        {
            int leftRoomWidth, leftRoomHeight, rightRoomWidth, rightRoomHeight;

            if (direction < 0) // Red team house - original layout
            {
                leftRoomWidth = 5;
                leftRoomHeight = 11;
                rightRoomWidth = 10;
                rightRoomHeight = 7;
            }
            else // Blue team house - mirror layout
            {
                leftRoomWidth = 10;  // Large room on left
                leftRoomHeight = 7;  // Shorter height
                rightRoomWidth = 5;  // Small room on right
                rightRoomHeight = 11; // Taller height
            }

            TShock.Log.Info($"[CCTG] {side} House build position: X={startX}, ground level={groundLevel}");

            ClearTreesAboveHouse(startX, groundLevel);

            // Clear the house interior area first, then ground the foundation
            ClearHouseInterior(startX, groundLevel);

            // Ground the foundation — fill gaps between each column's surface and groundLevel
            GroundFoundation(startX, groundLevel, surfaceHeights);

            TShock.Log.Info($"[CCTG] {side} House interior cleared (height={MaxHeight + 1} rows only)");

            // Build rooms based on team
            int leftStartX, leftTopY, rightStartX, rightTopY;

            if (direction < 0) // Red team
            {
                BuildRedTeamRooms(startX, groundLevel, leftRoomWidth, leftRoomHeight, rightRoomWidth, rightRoomHeight,
                    out leftStartX, out leftTopY, out rightStartX, out rightTopY);
            }
            else // Blue team
            {
                BuildBlueTeamRooms(startX, groundLevel, leftRoomWidth, leftRoomHeight, rightRoomWidth, rightRoomHeight,
                    out leftStartX, out leftTopY, out rightStartX, out rightTopY);
            }

            // Build middle wall with passage
            BuildMiddleWall(leftTopY, rightTopY, rightStartX, groundLevel, direction);

            // Place doors and record positions
            int leftDoorX = leftStartX;
            int rightDoorX = rightStartX + rightRoomWidth - 1;
            int doorY = groundLevel - 3;
            PlaceDoor(leftDoorX, doorY, TileID.ClosedDoor);
            PlaceDoor(rightDoorX, doorY, TileID.ClosedDoor);
            doorPositions.Add(new Point(leftDoorX, doorY));
            doorPositions.Add(new Point(rightDoorX, doorY));

            // Place platforms and torches
            PlacePlatformsAndTorches(leftStartX, groundLevel, direction);

            // Fill walls
            FillWalls(leftStartX, leftTopY, leftRoomWidth, groundLevel);
            FillWalls(rightStartX, rightTopY, rightRoomWidth, groundLevel);

            // Fill middle wall background (including passage area)
            FillMiddleWall(leftTopY, rightTopY, rightStartX, groundLevel);

            // Place furniture
            PlaceFurniture(leftStartX, rightStartX, groundLevel, leftRoomWidth, rightRoomWidth, direction);

            // Refresh area — only cover the house region, not 40 blocks above
            int refreshStartY = groundLevel - MaxHeight - 1;
            int refreshHeight = MaxHeight + 3; // house height + foundation + small margin
            TSPlayer.All.SendTileRect((short)(startX - 2), (short)refreshStartY,
                (byte)(TotalWidth + 4), (byte)refreshHeight);

            // Return house spawn point (right room center, above floor)
            int spawnX = rightStartX + rightRoomWidth / 2;
            int spawnY = groundLevel - 3; // 2 blocks above floor

            TShock.Log.Info($"[CCTG] {side} House built, spawn set to: ({spawnX}, {spawnY})");
            return new Point(spawnX, spawnY);
        }

        /// <summary>
        /// Get protected areas for this house
        /// </summary>
        public (Rectangle leftRoom, Rectangle rightRoom) GetProtectedAreas(int startX, int groundLevel, int direction)
        {
            int leftRoomWidth, leftRoomHeight, rightRoomWidth, rightRoomHeight;

            if (direction < 0) // Red team
            {
                leftRoomWidth = 5;
                leftRoomHeight = 11;
                rightRoomWidth = 10;
                rightRoomHeight = 7;
            }
            else // Blue team
            {
                leftRoomWidth = 10;
                leftRoomHeight = 7;
                rightRoomWidth = 5;
                rightRoomHeight = 11;
            }

            int leftStartX = startX;
            int leftTopY = groundLevel - leftRoomHeight;
            int rightStartX = leftStartX + leftRoomWidth - 1;
            int rightTopY = groundLevel - rightRoomHeight;

            // Protect entire room including walls, floor, ceiling, and foundation
            // Left room: from leftStartX-1 (foundation extra block) to rightStartX (middle wall)
            // Y: from ceiling to ground level (foundation)
            int leftProtectX = leftStartX - 1;
            int leftProtectY = leftTopY;
            int leftProtectWidth = leftRoomWidth + 1; // +1 for foundation extra block on left
            int leftProtectHeight = leftRoomHeight + 1; // +1 for foundation row

            // Right room: from rightStartX (middle wall) to rightStartX+rightRoomWidth-1 (right wall)
            int rightProtectX = rightStartX;
            int rightProtectY = rightTopY;
            int rightProtectWidth = rightRoomWidth; // no extra block on right
            int rightProtectHeight = rightRoomHeight + 1; // +1 for foundation row

            return (
                new Rectangle(leftProtectX, leftProtectY, leftProtectWidth, leftProtectHeight),
                new Rectangle(rightProtectX, rightProtectY, rightProtectWidth, rightProtectHeight)
            );
        }

        /// <summary>
        /// Clear only the house interior area (from groundLevel - MaxHeight to groundLevel)
        /// Does NOT clear the sky above — preserves natural terrain
        /// </summary>
        private void ClearTreesAboveHouse(int startX, int groundLevel)
        {
            int clearStartX = startX - 3;
            int clearEndX = startX + TotalWidth + 3;

            for (int x = clearStartX; x < clearEndX; x++)
            {
                for (int y = groundLevel - MaxHeight - 5; y <= groundLevel; y++)
                {
                    if (!IsValidCoord(x, y))
                        continue;

                    var tile = Main.tile[x, y];
                    if (tile != null && tile.active())
                    {
                        ushort t = tile.type;
                        if (t == TileID.Trees || t == TileID.PalmTree || t == TileID.TreeTopaz ||
                            t == TileID.TreeAmethyst || t == TileID.TreeSapphire || t == TileID.TreeEmerald ||
                            t == TileID.TreeRuby || t == TileID.TreeDiamond || t == TileID.TreeAmber ||
                            t == TileID.VanityTreeSakura || t == TileID.VanityTreeYellowWillow ||
                            t == TileID.Plants || t == TileID.Plants2 ||
                            t == TileID.LargePiles || t == TileID.LargePiles2 ||
                            t == TileID.SmallPiles || t == TileID.Saplings ||
                            t == TileID.Sunflower || t == TileID.CorruptPlants ||
                            t == TileID.CrimsonPlants || t == TileID.JunglePlants ||
                            t == TileID.JunglePlants2 || t == TileID.HallowedPlants ||
                            t == TileID.HallowedPlants2 || t == TileID.Vines ||
                            t == TileID.MushroomPlants || t == TileID.MushroomTrees)
                        {
                            WorldGen.KillTile(x, y, false, false, true);
                        }
                    }
                }
            }

            int refreshY = groundLevel - MaxHeight - 5;
            TSPlayer.All.SendTileRect((short)(startX - 3), (short)refreshY,
                (byte)(TotalWidth + 6), (byte)(MaxHeight + 7));

        }

        private void ClearHouseInterior(int startX, int groundLevel)
        {
            int clearStartX = startX - 2;
            int clearEndX = startX + TotalWidth + 2;
            int clearTopY = groundLevel - MaxHeight - 3;
            int clearBottomY = groundLevel;

            for (int x = clearStartX; x < clearEndX; x++)
            {
                for (int y = clearTopY; y <= clearBottomY; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].ClearEverything();
                    }
                }
            }
        }

        /// <summary>
        /// Ground the foundation by filling non-solid tiles from each column's surface down to groundLevel
        /// Ensures all foundation tiles at groundLevel are solid
        /// </summary>
        private void GroundFoundation(int startX, int groundLevel, int[] surfaceHeights)
        {
            // surfaceHeights has one entry per foundation column (TotalWidth entries, but we use what we have)
            int width = surfaceHeights.Length;

            const int maxFillDepth = 2; // Only fill at most 2 layers below surface

            for (int i = 0; i < width; i++)
            {
                int x = startX + i;
                int surfaceY = surfaceHeights[i];

                // Fill from surfaceY down to groundLevel, but at most 2 layers
                int fillEnd = Math.Min(groundLevel, surfaceY + maxFillDepth - 1);
                for (int y = surfaceY; y <= fillEnd; y++)
                {
                    if (!IsValidCoord(x, y))
                        continue;

                    var tile = Main.tile[x, y];
                    if (tile == null || !tile.active() || !Main.tileSolid[tile.type])
                    {
                        PlaceTile(x, y, TileID.StoneSlab);
                    }
                }
            }

            TShock.Log.Info($"[CCTG] Foundation grounded: {width} columns from X={startX}, groundLevel={groundLevel}");
        }

        /// <summary>
        /// Build red team rooms (small room on left, large room on right)
        /// </summary>
        private void BuildRedTeamRooms(int startX, int groundLevel,
            int leftRoomWidth, int leftRoomHeight, int rightRoomWidth, int rightRoomHeight,
            out int leftStartX, out int leftTopY, out int rightStartX, out int rightTopY)
        {
            // === Build left room (5x11) ===
            leftStartX = startX;
            leftTopY = groundLevel - leftRoomHeight;

            // Left room foundation layer (at ground level, 1 extra block on left side)
            for (int x = leftStartX - 1; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab);
            }

            // left room floor (interior floor)
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab);
            }

            // left room ceiling - include full width to cover middle wall position
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, leftTopY, TileID.StoneSlab);
            }

            // left room left wall
            for (int y = leftTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(leftStartX, y, TileID.StoneSlab);
            }

            // === Build right room (10x7) ===
            rightStartX = leftStartX + leftRoomWidth - 1;
            rightTopY = groundLevel - rightRoomHeight;

            // right room foundation layer (at ground level)
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab);
            }

            // right room floor
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab);
            }

            // right room ceiling - exclude middle wall position to avoid overlap with left room
            for (int x = rightStartX + 1; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, rightTopY, TileID.StoneSlab);
            }

            // right room right wall
            for (int y = rightTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(rightStartX + rightRoomWidth - 1, y, TileID.StoneSlab);
            }
        }

        /// <summary>
        /// Build blue team rooms (large room on left, small room on right)
        /// </summary>
        private void BuildBlueTeamRooms(int startX, int groundLevel,
            int leftRoomWidth, int leftRoomHeight, int rightRoomWidth, int rightRoomHeight,
            out int leftStartX, out int leftTopY, out int rightStartX, out int rightTopY)
        {
            TShock.Log.Info($"[CCTG] Blue team: Building left room (10x7) first");

            // === Build left room (10x7) first ===
            leftStartX = startX;
            leftTopY = groundLevel - leftRoomHeight;

            // Left room foundation layer (at ground level, 1 extra block on left side)
            for (int x = leftStartX - 1; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab);
            }

            // left room floor (interior floor)
            for (int x = leftStartX; x < leftStartX + leftRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab);
            }

            // left room ceiling - exclude middle wall position to avoid overlap with right room
            for (int x = leftStartX; x < leftStartX + leftRoomWidth - 1; x++)
            {
                PlaceTile(x, leftTopY, TileID.StoneSlab);
            }

            // left room left wall
            for (int y = leftTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(leftStartX, y, TileID.StoneSlab);
            }

            TShock.Log.Info($"[CCTG] Blue team: Building right room (5x11) second");

            // === Build right room (5x11) second ===
            rightStartX = leftStartX + leftRoomWidth - 1;
            rightTopY = groundLevel - rightRoomHeight;

            // right room foundation layer (at ground level)
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel, TileID.StoneSlab);
            }

            // right room floor
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, groundLevel - 1, TileID.StoneSlab);
            }

            // right room ceiling - include full width to cover middle wall position
            for (int x = rightStartX; x < rightStartX + rightRoomWidth; x++)
            {
                PlaceTile(x, rightTopY, TileID.StoneSlab);
            }

            // right room right wall
            for (int y = rightTopY + 1; y < groundLevel - 1; y++)
            {
                PlaceTile(rightStartX + rightRoomWidth - 1, y, TileID.StoneSlab);
            }
        }

        /// <summary>
        /// Build middle wall with passage
        /// </summary>
        private void BuildMiddleWall(int leftTopY, int rightTopY, int middleX, int groundLevel, int direction)
        {
            int passageHeight = 4; // passage height (bottom 4 blocks)

            // Get the higher ceiling (smaller Y value, higher position)
            int higherCeilingY = Math.Min(leftTopY, rightTopY);

            // Get the lower ceiling (larger Y value, lower position)
            int lowerCeilingY = Math.Max(leftTopY, rightTopY);

            // Build middle wall from the higher ceiling to ground
            for (int y = higherCeilingY + 1; y < groundLevel - 1; y++)
            {
                // Check if we're in the passage area (bottom 4 blocks from lower ceiling)
                if (y >= lowerCeilingY + 1 && y >= groundLevel - passageHeight)
                {
                    // This is the passage area - leave it empty
                    continue;
                }
                else
                {
                    // Build wall
                    PlaceTile(middleX, y, TileID.StoneSlab);
                }
            }

            TShock.Log.Info($"[CCTG] Middle wall built from Y={higherCeilingY + 1} to Y={groundLevel - 1}, passage area: Y>={groundLevel - passageHeight}");
        }

        /// <summary>
        /// Place platforms and torches
        /// </summary>
        private void PlacePlatformsAndTorches(int leftStartX, int groundLevel, int direction)
        {
            int leftDoorTopX = leftStartX;
            int leftDoorTopY = groundLevel - 4;

            if (direction < 0) // Red team house - platforms above left door
            {
                // Place platforms above left door
                PlacePlatform(leftDoorTopX + 1, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
                PlacePlatform(leftDoorTopX + 3, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
                TShock.Log.Info($"[CCTG] Platforms above red team left door placed");

                // Place torches above left door
                PlaceTorch(leftDoorTopX + 1, leftDoorTopY + 3 - 6 - 1);
                PlaceTorch(leftDoorTopX + 3, leftDoorTopY + 3 - 6 - 1);
                TShock.Log.Info($"[CCTG] Torches above red team left door placed");
            }
            else // Blue team house - platforms above left room door, moved 9 blocks right
            {
                // Move 9 blocks to the right
                int shiftedX = leftDoorTopX + 9;

                // Place platforms at shifted position (9 blocks right of original)
                PlacePlatform(shiftedX + 1, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
                PlacePlatform(shiftedX + 3, leftDoorTopY + 3 - 6, TileID.Platforms, 0);
                TShock.Log.Info($"[CCTG] Platforms for blue team placed 9 blocks right at ({shiftedX + 1}, {leftDoorTopY + 3 - 6})");

                // Place torches at shifted position (9 blocks right of original)
                PlaceTorch(shiftedX + 1, leftDoorTopY + 3 - 6 - 1);
                PlaceTorch(shiftedX + 3, leftDoorTopY + 3 - 6 - 1);
                TShock.Log.Info($"[CCTG] Torches for blue team placed 9 blocks right at ({shiftedX + 1}, {leftDoorTopY + 3 - 6 - 1})");
            }
        }

        /// <summary>
        /// Fill wood walls
        /// </summary>
        private void FillWalls(int startX, int topY, int roomWidth, int groundLevel)
        {
            for (int x = startX + 1; x < startX + roomWidth - 1; x++)
            {
                for (int y = topY + 1; y < groundLevel - 1; y++)
                {
                    if (IsValidCoord(x, y))
                    {
                        Main.tile[x, y].wall = WallID.Wood;
                    }
                }
            }
        }

        /// <summary>
        /// Fill middle wall background (only in passage area where there are no tiles)
        /// </summary>
        private void FillMiddleWall(int leftTopY, int rightTopY, int middleX, int groundLevel)
        {
            // Get the higher ceiling (smaller Y value, higher position)
            int higherCeilingY = Math.Min(leftTopY, rightTopY);

            // Fill background wall from higher ceiling to ground, but only where there's no tile
            for (int y = higherCeilingY + 1; y < groundLevel - 1; y++)
            {
                if (IsValidCoord(middleX, y))
                {
                    var tile = Main.tile[middleX, y];
                    // Only place wall if there's no active tile (i.e., in the passage area)
                    if (tile != null && !tile.active())
                    {
                        tile.wall = WallID.Wood;
                    }
                }
            }

            TShock.Log.Info($"[CCTG] Middle wall background filled (only in passage area without tiles)");
        }

        // Store furniture and door placement parameters for repair
        private List<FurnitureSetInfo> furnitureSets = new List<FurnitureSetInfo>();
        private List<Point> doorPositions = new List<Point>();

        public class FurnitureSetInfo
        {
            public int StartX;
            public int FloorY;
        }

        public List<FurnitureSetInfo> FurnitureSets => furnitureSets;
        public List<Point> DoorPositions => doorPositions;

        /// <summary>
        /// Place furniture
        /// </summary>
        private void PlaceFurniture(int leftStartX, int rightStartX, int groundLevel, int leftWidth, int rightWidth, int direction)
        {
            int floorY = groundLevel - 2; // Above floor

            TShock.Log.Info($"[CCTG] Placing furniture for {(direction < 0 ? "red team" : "blue team")} house...");

            // Red team (left house): furniture in right room (large room)
            // Blue team (right house): furniture in left room (large room)

            if (direction < 0) // Red team house - furniture in right room
            {
                PlaceFurnitureSet(rightStartX, floorY, "Red team - Right room");
                furnitureSets.Add(new FurnitureSetInfo { StartX = rightStartX, FloorY = floorY });
            }
            else // Blue team house - furniture in left room
            {
                PlaceFurnitureSet(leftStartX, floorY, "Blue team - Left room");
                furnitureSets.Add(new FurnitureSetInfo { StartX = leftStartX, FloorY = floorY });
            }

            // Refresh furniture area
            NetMessage.SendTileSquare(-1, leftStartX, floorY - 2, 15);
        }

        /// <summary>
        /// Place a set of furniture (chair, anvil, platform, workbench, furnace)
        /// </summary>
        private void PlaceFurnitureSet(int startX, int floorY, string roomName)
        {
            // Wood chair
            if (WorldGen.PlaceObject(startX + 2, floorY, TileID.Chairs, false, 0))
            {
                TShock.Log.Info($"[CCTG] {roomName}: Wood chair placed at ({startX + 2}, {floorY})");
            }

            // Anvil
            if (WorldGen.PlaceObject(startX + 3, floorY, TileID.Anvils, false, 0))
            {
                TShock.Log.Info($"[CCTG] {roomName}: Anvil placed at ({startX + 3}, {floorY})");
            }

            // Wood platform (2 blocks)
            int platformY = floorY - 1;
            PlacePlatform(startX + 3, platformY, TileID.Platforms, 0);
            PlacePlatform(startX + 4, platformY, TileID.Platforms, 0);
            TShock.Log.Info($"[CCTG] {roomName}: Wood platform placed at ({startX + 3}, {platformY})");

            // Work bench
            if (WorldGen.PlaceObject(startX + 3, platformY - 1, TileID.WorkBenches, false, 0))
            {
                TShock.Log.Info($"[CCTG] {roomName}: Work bench placed at ({startX + 3}, {platformY - 1})");
            }

            // Furnace
            if (WorldGen.PlaceObject(startX + 6, floorY, TileID.Furnaces, false, 0))
            {
                TShock.Log.Info($"[CCTG] {roomName}: Furnace placed at ({startX + 6}, {floorY})");
            }
        }

        /// <summary>
        /// Check and repair missing furniture and doors in all houses
        /// </summary>
        public void RepairFurniture()
        {
            bool repaired = false;

            foreach (var info in furnitureSets)
            {
                int startX = info.StartX;
                int floorY = info.FloorY;
                int platformY = floorY - 1;

                // Check and repair chair at startX+2, floorY
                if (RepairSingleFurniture(startX + 2, floorY, TileID.Chairs, "Chair"))
                    repaired = true;

                // Check and repair anvil at startX+3, floorY
                if (RepairSingleFurniture(startX + 3, floorY, TileID.Anvils, "Anvil"))
                    repaired = true;

                // Check and repair workbench at startX+3, platformY-1
                if (RepairSingleFurniture(startX + 3, platformY - 1, TileID.WorkBenches, "Workbench"))
                    repaired = true;

                // Check and repair furnace at startX+6, floorY
                if (RepairSingleFurniture(startX + 6, floorY, TileID.Furnaces, "Furnace"))
                    repaired = true;
            }

            // Check and repair doors
            foreach (var doorPos in doorPositions)
            {
                if (RepairDoor(doorPos.X, doorPos.Y))
                    repaired = true;
            }

            if (repaired)
            {
                // Refresh areas for all players
                foreach (var info in furnitureSets)
                {
                    NetMessage.SendTileSquare(-1, info.StartX, info.FloorY - 3, 15);
                }
                foreach (var doorPos in doorPositions)
                {
                    NetMessage.SendTileSquare(-1, doorPos.X, doorPos.Y, 5);
                }
            }
        }

        /// <summary>
        /// Check if a door is missing and re-place it
        /// </summary>
        private bool RepairDoor(int x, int y)
        {
            if (!IsValidCoord(x, y))
                return false;

            var tile = Main.tile[x, y];

            // Door occupies 3 tiles vertically, check the anchor point
            // Doors can be open or closed (ClosedDoor or OpenDoor)
            if (!tile.active() || (tile.type != TileID.ClosedDoor && tile.type != TileID.OpenDoor))
            {
                WorldGen.PlaceDoor(x, y, TileID.ClosedDoor);
                WorldGen.SquareTileFrame(x, y, true);
                TShock.Log.Info($"[CCTG] Repaired Door at ({x}, {y})");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a furniture piece is missing and re-place it
        /// Returns true if repair was needed
        /// </summary>
        private bool RepairSingleFurniture(int x, int y, ushort tileType, string name)
        {
            if (!IsValidCoord(x, y) || !IsValidCoord(x, y + 1))
                return false;

            var tile = Main.tile[x, y];

            if (!tile.active() || tile.type != tileType)
            {
                var ground = Main.tile[x, y + 1];
                if (ground == null || !ground.active() || !Main.tileSolid[ground.type])
                    return false;

                WorldGen.PlaceObject(x, y, tileType, false, 0);

                if (Main.tile[x, y].active() && Main.tile[x, y].type == tileType)
                {
                    TShock.Log.Info($"[CCTG] Repaired {name} at ({x}, {y})");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Place platforms
        /// </summary>
        private void PlacePlatform(int x, int y, ushort type, int style)
        {
            if (IsValidCoord(x, y))
            {
                var tile = Main.tile[x, y];
                tile.type = type;
                tile.active(true);
                tile.slope(0);
                tile.halfBrick(false);

                if (style > 0)
                {
                    tile.frameX = (short)(style * 18);
                }

                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        /// <summary>
        /// Place tile
        /// </summary>
        private void PlaceTile(int x, int y, ushort type, int style = 0)
        {
            if (IsValidCoord(x, y))
            {
                var tile = Main.tile[x, y];
                tile.type = type;
                tile.active(true);
                tile.slope(0);
                tile.halfBrick(false);

                if (style > 0)
                {
                    tile.frameX = (short)(style * 18);
                }

                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        /// <summary>
        /// Place door
        /// </summary>
        private void PlaceDoor(int x, int y, ushort type)
        {
            if (IsValidCoord(x, y))
            {
                WorldGen.PlaceDoor(x, y, type);
                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        /// <summary>
        /// Place torches
        /// </summary>
        private void PlaceTorch(int x, int y)
        {
            if (IsValidCoord(x, y))
            {
                WorldGen.PlaceTile(x, y, TileID.Torches, false, false, -1, 0);
                WorldGen.SquareTileFrame(x, y, true);
            }
        }

        /// <summary>
        /// Check if coordinates are within world bounds
        /// </summary>
        private bool IsValidCoord(int x, int y)
        {
            return x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY;
        }
    }
}
