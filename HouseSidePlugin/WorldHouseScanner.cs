using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;

namespace HouseSidePlugin
{
    public class WorldHouseScanner
    {
        public List<HouseData> LeftHouses { get; private set; }
        public List<HouseData> RightHouses { get; private set; }
        public int WorldBoundaryX { get; private set; }

        private const int ScanInterval = 20; // Increased to reduce duplicates
        private HashSet<Point> scannedLocations;

        // Lock object for WorldGen.StartRoomCheck() to prevent race conditions
        private static readonly object roomCheckLock = new object();

        // Minimum overlap percentage to consider two houses as duplicates
        private const float OverlapThreshold = 0.5f;

        public WorldHouseScanner()
        {
            LeftHouses = new List<HouseData>();
            RightHouses = new List<HouseData>();
            scannedLocations = new HashSet<Point>();
        }

        public void ScanWorld()
        {
            Console.WriteLine("[WorldHouseScanner] Starting world scan...");
            TShock.Log.Info("[WorldHouseScanner] Starting world scan...");

            LeftHouses.Clear();
            RightHouses.Clear();
            scannedLocations.Clear();

            WorldBoundaryX = Main.maxTilesX / 2;

            Console.WriteLine($"[WorldHouseScanner] World size: {Main.maxTilesX} x {Main.maxTilesY}");
            Console.WriteLine($"[WorldHouseScanner] Boundary X: {WorldBoundaryX}");
            Console.WriteLine($"[WorldHouseScanner] Scan interval: {ScanInterval} tiles");

            int totalChecks = 0;
            int validHouses = 0;

            for (int x = 10; x < Main.maxTilesX - 10; x += ScanInterval)
            {
                for (int y = 10; y < Main.maxTilesY - 10; y += ScanInterval)
                {
                    totalChecks++;
                    if (CheckLocation(x, y))
                    {
                        validHouses++;
                    }
                }
            }

            Console.WriteLine($"[WorldHouseScanner] Scan complete:");
            Console.WriteLine($"[WorldHouseScanner]   Total checks: {totalChecks}");
            Console.WriteLine($"[WorldHouseScanner]   Valid houses found: {validHouses}");
            Console.WriteLine($"[WorldHouseScanner]   LEFT houses: {LeftHouses.Count}");
            Console.WriteLine($"[WorldHouseScanner]   RIGHT houses: {RightHouses.Count}");

            TShock.Log.Info($"[WorldHouseScanner] Scan complete: {validHouses} valid houses ({LeftHouses.Count} LEFT, {RightHouses.Count} RIGHT)");
        }

        private bool CheckLocation(int x, int y)
        {
            Rectangle bounds;

            // Lock to prevent race conditions on WorldGen.roomX1/X2/Y1/Y2
            lock (roomCheckLock)
            {
                if (!WorldGen.StartRoomCheck(x, y))
                {
                    return false;
                }

                // Get room boundaries from WorldGen static fields IMMEDIATELY
                // Must read within lock before other code can overwrite
                bounds = new Rectangle(
                    WorldGen.roomX1,
                    WorldGen.roomY1,
                    WorldGen.roomX2 - WorldGen.roomX1 + 1,
                    WorldGen.roomY2 - WorldGen.roomY1 + 1
                );
            }

            Point center = new Point(
                (bounds.Left + bounds.Right) / 2,
                (bounds.Top + bounds.Bottom) / 2
            );

            // Check if this house overlaps significantly with any existing house
            if (IsDuplicateHouse(bounds))
            {
                return false;
            }

            scannedLocations.Add(center);

            HouseData house = new HouseData
            {
                CenterX = center.X,
                CenterY = center.Y,
                Bounds = bounds,
                Side = (center.X < WorldBoundaryX) ? "LEFT" : "RIGHT",
                IsOccupied = false
            };

            if (house.Side == "LEFT")
            {
                LeftHouses.Add(house);
                Console.WriteLine($"[WorldHouseScanner] LEFT house #{LeftHouses.Count}: Center=({center.X},{center.Y}), Bounds={bounds}");
            }
            else
            {
                RightHouses.Add(house);
                Console.WriteLine($"[WorldHouseScanner] RIGHT house #{RightHouses.Count}: Center=({center.X},{center.Y}), Bounds={bounds}");
            }

            return true;
        }

        /// <summary>
        /// Check if a house with the given bounds already exists (based on overlap)
        /// </summary>
        private bool IsDuplicateHouse(Rectangle newBounds)
        {
            // Check against all existing houses
            foreach (var house in LeftHouses)
            {
                // Check if bounds are nearly identical (within 2 tiles)
                if (AreBoundsNearlyIdentical(newBounds, house.Bounds))
                {
                    Console.WriteLine($"[WorldHouseScanner] Duplicate found: new={newBounds}, existing={house.Bounds}");
                    return true;
                }

                // Also check overlap percentage
                float overlap = CalculateOverlapPercentage(newBounds, house.Bounds);
                if (overlap >= OverlapThreshold)
                {
                    Console.WriteLine($"[WorldHouseScanner] High overlap ({overlap:P0}): new={newBounds}, existing={house.Bounds}");
                    return true;
                }
            }

            foreach (var house in RightHouses)
            {
                // Check if bounds are nearly identical (within 2 tiles)
                if (AreBoundsNearlyIdentical(newBounds, house.Bounds))
                {
                    Console.WriteLine($"[WorldHouseScanner] Duplicate found: new={newBounds}, existing={house.Bounds}");
                    return true;
                }

                // Also check overlap percentage
                float overlap = CalculateOverlapPercentage(newBounds, house.Bounds);
                if (overlap >= OverlapThreshold)
                {
                    Console.WriteLine($"[WorldHouseScanner] High overlap ({overlap:P0}): new={newBounds}, existing={house.Bounds}");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if two room bounds are nearly identical (within tolerance)
        /// </summary>
        private bool AreBoundsNearlyIdentical(Rectangle rect1, Rectangle rect2)
        {
            const int tolerance = 2; // Allow 2 tiles difference

            return Math.Abs(rect1.Left - rect2.Left) <= tolerance &&
                   Math.Abs(rect1.Top - rect2.Top) <= tolerance &&
                   Math.Abs(rect1.Right - rect2.Right) <= tolerance &&
                   Math.Abs(rect1.Bottom - rect2.Bottom) <= tolerance;
        }

        /// <summary>
        /// Calculate the percentage of overlap between two rectangles
        /// </summary>
        private float CalculateOverlapPercentage(Rectangle rect1, Rectangle rect2)
        {
            // Find intersection
            int x1 = Math.Max(rect1.Left, rect2.Left);
            int y1 = Math.Max(rect1.Top, rect2.Top);
            int x2 = Math.Min(rect1.Right, rect2.Right);
            int y2 = Math.Min(rect1.Bottom, rect2.Bottom);

            // No overlap
            if (x2 <= x1 || y2 <= y1)
            {
                return 0f;
            }

            // Calculate overlap area
            int overlapArea = (x2 - x1) * (y2 - y1);
            int rect1Area = rect1.Width * rect1.Height;
            int rect2Area = rect2.Width * rect2.Height;

            // Use the smaller area as the base for percentage calculation
            int smallerArea = Math.Min(rect1Area, rect2Area);

            if (smallerArea == 0)
            {
                return 0f;
            }

            return (float)overlapArea / smallerArea;
        }
    }
}
