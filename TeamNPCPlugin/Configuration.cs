using System;
using System.IO;
using System.Text.Json;

namespace TeamNPCPlugin
{
    public class Configuration
    {
        public bool EnablePlugin { get; set; } = true;
        public int CheckIntervalSeconds { get; set; } = 30;
        public int RedTeamSpawnX { get; set; } = -200;
        public int RedTeamSpawnY { get; set; } = 150;
        public int BlueTeamSpawnX { get; set; } = 200;
        public int BlueTeamSpawnY { get; set; } = 150;
        public int HouseSearchRadius { get; set; } = 100;
        public bool ClearNPCsOnLoad { get; set; } = true;
        public bool EnableDebugLog { get; set; } = false;

        // Partition settings
        public int PartitionBoundary { get; set; } = -1; // -1 = use Main.spawnTileX

        // Housing validation settings
        public bool EnableStrictHousingValidation { get; set; } = true;
        public int MinHouseDistance { get; set; } = 10; // Minimum distance between NPC houses

        // Persistence settings
        public bool EnableHousingPersistence { get; set; } = true;
        public int AutoSaveIntervalSeconds { get; set; } = 300; // 5 minutes

        // Search settings
        public int SpiralSearchRadius { get; set; } = 150; // Increased search range

        private static readonly string ConfigPath = Path.Combine("tshock", "teamnpc.json");

        public static Configuration Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<Configuration>(json);
                }
                else
                {
                    // Create default configuration
                    var config = new Configuration();
                    config.Save();
                    return config;
                }
            }
            catch (Exception ex)
            {
                TShockAPI.TShock.Log.Error($"[TeamNPC] Failed to load configuration: {ex.Message}");
                return new Configuration();
            }
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, json);
                TShockAPI.TShock.Log.Info($"[TeamNPC] Configuration saved to {ConfigPath}");
            }
            catch (Exception ex)
            {
                TShockAPI.TShock.Log.Error($"[TeamNPC] Failed to save configuration: {ex.Message}");
            }
        }
    }
}
