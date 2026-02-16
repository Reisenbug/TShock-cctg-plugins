using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace TeamNPCPlugin
{
    public class NPCSpawnController
    {
        private NPCTeamManager teamManager;
        private HashSet<int> managedNPCTypes = new HashSet<int>();
        private Configuration config;

        // Track NPCs being spawned by the plugin to avoid blocking them
        private HashSet<int> pluginSpawningNPCs = new HashSet<int>();

        // Track when commands are used to temporarily allow NPC spawning
        private DateTime lastCommandTime = DateTime.MinValue;
        private const int CommandWindowMilliseconds = 2000; // 2 second window after command

        public NPCSpawnController(NPCTeamManager manager, Configuration configuration = null)
        {
            teamManager = manager;
            config = configuration;
            InitializeManagedNPCTypes();
        }

        private void InitializeManagedNPCTypes()
        {
            // Add all defined town NPC types to managed list
            foreach (var rule in TownNPCDefinitions.TownNpcSpawnRules.Values)
            {
                managedNPCTypes.Add(rule.NpcType);
            }
        }

        public bool IsManagedTownNPC(int npcType)
        {
            return managedNPCTypes.Contains(npcType);
        }

        /// <summary>
        /// Mark that a command was just used to spawn an NPC, allowing next spawn
        /// </summary>
        public void MarkCommandUsed()
        {
            lastCommandTime = DateTime.Now;
            Console.WriteLine($"[NPCSpawnController] Command window opened (next {CommandWindowMilliseconds}ms will allow NPC spawns)");
        }

        // Event hook: Prevent vanilla NPC spawning
        public void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            if (args.Handled)
                return;

            NPC npc = Main.npc[args.NpcId];

            // Don't block NPCs that the plugin is currently spawning
            if (pluginSpawningNPCs.Contains(args.NpcId))
            {
                Console.WriteLine($"[NPCSpawnController] ✅ Allowing plugin-spawned {npc.TypeName} (ID: {args.NpcId})");
                if (config?.EnableDebugLog ?? false)
                {
                    TShock.Log.Info($"[TeamNPC][Debug] Allowing plugin-spawned {npc.TypeName} (ID: {args.NpcId})");
                }
                pluginSpawningNPCs.Remove(args.NpcId);
                return;
            }

            // Allow NPCs spawned by commands (within 2 second window)
            TimeSpan timeSinceCommand = DateTime.Now - lastCommandTime;
            if (timeSinceCommand.TotalMilliseconds < CommandWindowMilliseconds)
            {
                Console.WriteLine($"[NPCSpawnController] ✅ Allowing command-spawned {npc.TypeName} (ID: {args.NpcId}) - within {timeSinceCommand.TotalMilliseconds:F0}ms of command");
                if (config?.EnableDebugLog ?? false)
                {
                    TShock.Log.Info($"[TeamNPC][Debug] Allowing command-spawned {npc.TypeName} (ID: {args.NpcId})");
                }
                return;
            }

            // Check if this is a managed town NPC
            if (IsManagedTownNPC(npc.netID))
            {
                Console.WriteLine($"[NPCSpawnController] ❌ Blocking vanilla {npc.TypeName} spawn (ID: {args.NpcId})");

                // Cancel vanilla spawn
                args.Handled = true;
                npc.active = false;
                npc.type = 0; // Clear NPC type to prevent message

                // Send kill packet to all clients to ensure NPC is removed
                TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", args.NpcId);

                if (config?.EnableDebugLog ?? false)
                {
                    TShock.Log.Info($"[TeamNPC][Debug] Prevented vanilla {npc.TypeName} spawn (ID: {args.NpcId})");
                }
            }
        }

        // Spawn team NPC at specified location
        public int SpawnTeamNPC(int npcType, string teamName, Point location)
        {
            try
            {
                Console.WriteLine($"[NPCSpawnController] Attempting to spawn NPC type {npcType} for {teamName} team at ({location.X}, {location.Y})");

                // Find the next available NPC slot
                int index = -1;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (!Main.npc[i].active)
                    {
                        index = i;
                        break;
                    }
                }

                if (index == -1)
                {
                    Console.WriteLine($"[NPCSpawnController] ❌ No available NPC slots!");
                    return -1;
                }

                // Mark this NPC as being spawned by the plugin
                pluginSpawningNPCs.Add(index);

                // Find a clear Y position above any solid tiles at the spawn X
                int spawnY = location.Y;
                while (spawnY > 0 && Main.tile[location.X, spawnY] != null
                    && Main.tile[location.X, spawnY].active()
                    && Main.tileSolid[Main.tile[location.X, spawnY].type])
                {
                    spawnY--;
                }

                int spawnedIndex = NPC.NewNPC(
                    new EntitySource_DebugCommand(),
                    location.X * 16,
                    spawnY * 16,
                    npcType
                );

                Console.WriteLine($"[NPCSpawnController] NPC.NewNPC() returned index: {spawnedIndex}");

                // Add actual spawned index to whitelist if different
                if (spawnedIndex != index && spawnedIndex >= 0)
                {
                    pluginSpawningNPCs.Add(spawnedIndex);
                }

                if (spawnedIndex >= 0 && spawnedIndex < Main.maxNPCs)
                {
                    NPC npc = Main.npc[spawnedIndex];

                    if (npc != null && npc.active)
                    {
                        Console.WriteLine($"[NPCSpawnController] ✅ NPC spawn succeeded!");

                        npc.homeless = true;
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", spawnedIndex);

                        return spawnedIndex;
                    }
                    else
                    {
                        Console.WriteLine($"[NPCSpawnController] ❌ NPC spawn failed - NPC not active");
                        pluginSpawningNPCs.Clear();
                    }
                }
                else
                {
                    Console.WriteLine($"[NPCSpawnController] ❌ NPC spawn failed - invalid index: {spawnedIndex}");
                    pluginSpawningNPCs.Clear();
                }

                return -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NPCSpawnController] ❌ Exception during spawn: {ex.Message}");
                TShock.Log.Error($"[TeamNPC] Failed to spawn NPC: {ex.Message}");
                pluginSpawningNPCs.Clear();
                return -1;
            }
        }

        // Set NPC home
        public void SetNPCHome(int npcIndex, Point homeLocation)
        {
            if (npcIndex < 0 || npcIndex >= Main.maxNPCs)
                return;

            NPC npc = Main.npc[npcIndex];
            if (npc == null || !npc.active)
                return;

            npc.homeTileX = homeLocation.X;
            npc.homeTileY = homeLocation.Y;
            npc.homeless = false;

            // Sync home to all clients
            TSPlayer.All.SendData(PacketTypes.UpdateNPCHome, "", npcIndex,
                homeLocation.X, homeLocation.Y, 0);
        }

        // Clear all town NPCs
        public void ClearAllTownNPCs()
        {
            int cleared = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.active && IsManagedTownNPC(npc.netID))
                {
                    npc.active = false;
                    npc.type = 0;
                    TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                    cleared++;
                }
            }

            if (cleared > 0)
            {
                TShock.Log.Info($"[TeamNPC] Cleared {cleared} town NPCs");
            }
        }
    }
}
