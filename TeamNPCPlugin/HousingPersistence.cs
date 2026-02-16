using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using TShockAPI;

namespace TeamNPCPlugin
{
    /// <summary>
    /// House assignment for a specific NPC
    /// </summary>
    public class HouseAssignment
    {
        public string NPCName { get; set; }
        public int LocationX { get; set; }
        public int LocationY { get; set; }
        public int BoundsX { get; set; }
        public int BoundsY { get; set; }
        public int BoundsWidth { get; set; }
        public int BoundsHeight { get; set; }
        public DateTime AssignedAt { get; set; }

        public HouseAssignment()
        {
            NPCName = "";
            AssignedAt = DateTime.Now;
        }

        public Point GetLocation() => new Point(LocationX, LocationY);
        public Rectangle GetBounds() => new Rectangle(BoundsX, BoundsY, BoundsWidth, BoundsHeight);
    }

    /// <summary>
    /// Housing data for a team
    /// </summary>
    public class TeamHousingData
    {
        public string TeamName { get; set; }
        public Dictionary<string, HouseAssignment> NPCAssignments { get; set; }

        public TeamHousingData()
        {
            TeamName = "";
            NPCAssignments = new Dictionary<string, HouseAssignment>();
        }
    }

    /// <summary>
    /// Root persistence data structure
    /// </summary>
    public class HousingPersistenceData
    {
        public DateTime LastSaved { get; set; }
        public Dictionary<string, TeamHousingData> Teams { get; set; }

        public HousingPersistenceData()
        {
            LastSaved = DateTime.Now;
            Teams = new Dictionary<string, TeamHousingData>();
        }
    }

    /// <summary>
    /// Manages persistence of housing assignments to JSON file
    /// </summary>
    public class HousingPersistence
    {
        private static readonly string FilePath = Path.Combine("tshock", "teamnpc_housing.json");

        /// <summary>
        /// Save all team housing assignments to JSON
        /// </summary>
        public static void Save(Dictionary<string, TeamState> teamStates)
        {
            try
            {
                var data = new HousingPersistenceData
                {
                    LastSaved = DateTime.Now,
                    Teams = new Dictionary<string, TeamHousingData>()
                };

                // Extract housing data from each team state
                foreach (var kvp in teamStates)
                {
                    string teamName = kvp.Key;
                    TeamState team = kvp.Value;

                    var teamData = new TeamHousingData
                    {
                        TeamName = teamName,
                        NPCAssignments = new Dictionary<string, HouseAssignment>()
                    };

                    // Get all house assignments from team
                    var assignments = team.GetHouseAssignments();
                    foreach (var assignment in assignments)
                    {
                        teamData.NPCAssignments[assignment.Key] = assignment.Value;
                    }

                    data.Teams[teamName] = teamData;
                }

                // Serialize to JSON
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));

                // Write to file
                File.WriteAllText(FilePath, json);

                TShock.Log.ConsoleInfo($"[TeamNPC] Saved housing assignments to {FilePath}");
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"[TeamNPC] Failed to save housing persistence: {ex.Message}");
            }
        }

        /// <summary>
        /// Load housing assignments from JSON
        /// </summary>
        public static HousingPersistenceData Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    TShock.Log.ConsoleInfo("[TeamNPC] No housing persistence file found, starting fresh");
                    return new HousingPersistenceData();
                }

                string json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<HousingPersistenceData>(json);

                if (data == null)
                {
                    TShock.Log.Warn("[TeamNPC] Housing persistence file is empty or invalid");
                    return new HousingPersistenceData();
                }

                TShock.Log.ConsoleInfo($"[TeamNPC] Loaded housing assignments from {FilePath} (saved: {data.LastSaved})");
                return data;
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"[TeamNPC] Failed to load housing persistence: {ex.Message}");
                return new HousingPersistenceData();
            }
        }

        /// <summary>
        /// Apply loaded persistence data to team states (with revalidation)
        /// </summary>
        public static void ApplyToTeamStates(
            HousingPersistenceData data,
            Dictionary<string, TeamState> teamStates,
            HousingValidator validator)
        {
            if (data == null || data.Teams == null)
                return;

            int totalRestored = 0;
            int totalInvalid = 0;

            foreach (var kvp in data.Teams)
            {
                string teamName = kvp.Key;
                TeamHousingData teamData = kvp.Value;

                if (!teamStates.ContainsKey(teamName))
                {
                    TShock.Log.Warn($"[TeamNPC] Skipping unknown team '{teamName}' from persistence");
                    continue;
                }

                TeamState team = teamStates[teamName];

                // Restore each NPC assignment after revalidating
                foreach (var assignment in teamData.NPCAssignments)
                {
                    string npcName = assignment.Key;
                    HouseAssignment house = assignment.Value;

                    // Revalidate the house is still valid
                    var result = validator.ValidateHousing(house.LocationX, house.LocationY);

                    if (!result.IsValid)
                    {
                        TShock.Log.Warn($"[TeamNPC] House for {npcName} ({teamName}) no longer valid: {result.FailureReason}");
                        totalInvalid++;
                        continue;
                    }

                    // Restore the assignment
                    team.RestoreHouseAssignment(npcName, house.GetLocation());
                    totalRestored++;
                }
            }

            if (totalRestored > 0 || totalInvalid > 0)
            {
                TShock.Log.ConsoleInfo($"[TeamNPC] Restored {totalRestored} housing assignments, {totalInvalid} were invalid");
            }
        }
    }
}
