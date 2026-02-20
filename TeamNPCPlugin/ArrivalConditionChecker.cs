using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;

namespace TeamNPCPlugin
{
    public class ArrivalConditionChecker
    {
        private int redTickCounter = 0;
        private int blueTickCounter = 0;
        private int redCheckInterval;
        private int blueCheckInterval;
        private Random random = new Random();

        private NPCTeamManager npcManager;
        private NPCSpawnController spawnController;
        private Dictionary<string, TeamState> teamStates;
        private Configuration config;

        private Dictionary<int, Point> playerLockedHomes = new Dictionary<int, Point>();

        public ArrivalConditionChecker(NPCTeamManager manager, NPCSpawnController controller, Dictionary<string, TeamState> states, int intervalSeconds, Configuration configuration)
        {
            npcManager = manager;
            spawnController = controller;
            teamStates = states;
            redCheckInterval = random.Next(25, 61) * 60;
            blueCheckInterval = random.Next(25, 61) * 60;
            config = configuration;
        }

        public void OnGameUpdate(EventArgs args)
        {
            bool redReady = false;
            bool blueReady = false;

            redTickCounter++;
            if (redTickCounter >= redCheckInterval)
            {
                redTickCounter = 0;
                redCheckInterval = random.Next(25, 61) * 60;
                redReady = true;
            }

            blueTickCounter++;
            if (blueTickCounter >= blueCheckInterval)
            {
                blueTickCounter = 0;
                blueCheckInterval = random.Next(25, 61) * 60;
                blueReady = true;
            }

            if (!redReady && !blueReady)
                return;

            UpdateTeamStates();

            if (redReady)
                CheckTeamSpawnConditions("Red");
            if (blueReady)
                CheckTeamSpawnConditions("Blue");

            CleanupInactiveNPCs();
            EnforceNPCHomeBounds();
        }

        public void SetPlayerLockedHome(int npcIndex, Point home)
        {
            playerLockedHomes[npcIndex] = home;
            TShock.Log.ConsoleInfo($"[TeamNPC] Locked NPC {npcIndex} ({Main.npc[npcIndex].TypeName}) home at ({home.X}, {home.Y})");
        }

        public void RemovePlayerLockedHome(int npcIndex)
        {
            if (playerLockedHomes.Remove(npcIndex))
                TShock.Log.ConsoleInfo($"[TeamNPC] Unlocked NPC {npcIndex} home");
        }

        private void UpdateTeamStates()
        {
            teamStates["Red"].Players = TShock.Players
                .Where(p => p != null && p.Active && p.TPlayer.team == 1)
                .ToList();

            teamStates["Blue"].Players = TShock.Players
                .Where(p => p != null && p.Active && p.TPlayer.team == 3)
                .ToList();

            teamStates["Red"].NPCCount = GetTeamNPCCount("Red");
            teamStates["Blue"].NPCCount = GetTeamNPCCount("Blue");
        }

        private bool HasWebbedPlayer()
        {
            foreach (var p in TShock.Players)
            {
                if (p != null && p.Active && (p.TPlayer.team == 1 || p.TPlayer.team == 3))
                {
                    for (int b = 0; b < Terraria.Player.maxBuffs; b++)
                    {
                        if (p.TPlayer.buffType[b] == 149 && p.TPlayer.buffTime[b] > 0)
                            return true;
                    }
                }
            }
            return false;
        }

        private void CheckTeamSpawnConditions(string teamName)
        {
            if (!Main.dayTime)
                return;

            if (HasWebbedPlayer())
                return;

            TeamState team = teamStates[teamName];

            if (team.Players.Count == 0)
                return;

            foreach (var rule in TownNPCDefinitions.PriorityOrder)
            {
                int currentCount = npcManager.GetNPCCount(rule.Key, teamName);
                if (currentCount > 0)
                    continue;

                if (!rule.Condition(team))
                    continue;

                Point spawnLocation = team.SpawnCenter;
                int npcIndex = spawnController.SpawnTeamNPC(rule.NpcType, teamName, spawnLocation);

                if (npcIndex >= 0)
                {
                    npcManager.RegisterNPC(npcIndex, rule.Key, teamName, rule.NpcType);
                    SendArrivalMessage(rule.DisplayName, teamName, team.TeamId);
                    TShock.Log.ConsoleInfo($"[TeamNPC] {teamName}: {rule.DisplayName} arrived at ({spawnLocation.X}, {spawnLocation.Y})");
                    break;
                }
            }
        }

        private int GetTeamNPCCount(string teamName)
        {
            int count = 0;
            foreach (var rule in TownNPCDefinitions.PriorityOrder)
            {
                count += npcManager.GetNPCCount(rule.Key, teamName);
            }
            return count;
        }

        private void SendArrivalMessage(string displayName, string teamName, int teamId)
        {
            string message = $"[{teamName}] {displayName} has arrived!";
            Color teamColor = Main.teamColor[teamId];
            TSPlayer.All.SendMessage(message, teamColor.R, teamColor.G, teamColor.B);
        }

        private void CleanupInactiveNPCs()
        {
            foreach (var npcIndex in npcManager.GetAllRegisteredNPCs().ToList())
            {
                if (npcIndex < 0 || npcIndex >= Main.maxNPCs || !Main.npc[npcIndex].active)
                {
                    npcManager.UnregisterNPC(npcIndex);
                    playerLockedHomes.Remove(npcIndex);
                    continue;
                }

                int expectedType = npcManager.GetNPCType(npcIndex);
                if (expectedType >= 0 && Main.npc[npcIndex].type != expectedType)
                {
                    npcManager.UnregisterNPC(npcIndex);
                    playerLockedHomes.Remove(npcIndex);
                }
            }

            foreach (var npcIndex in playerLockedHomes.Keys.ToList())
            {
                if (npcIndex < 0 || npcIndex >= Main.maxNPCs || !Main.npc[npcIndex].active)
                    playerLockedHomes.Remove(npcIndex);
            }
        }

        private void EnforceNPCHomeBounds()
        {
            int boundary = Main.spawnTileX;

            // Registered NPCs: kick if home crossed boundary
            foreach (var npcIndex in npcManager.GetAllRegisteredNPCs().ToList())
            {
                if (npcIndex < 0 || npcIndex >= Main.maxNPCs || !Main.npc[npcIndex].active)
                    continue;

                NPC npc = Main.npc[npcIndex];
                if (npc.homeless)
                    continue;

                string team = npcManager.GetNPCTeam(npcIndex);
                if (team == null)
                    continue;

                bool outOfBounds = (team == "Red" && npc.homeTileX > boundary) ||
                                   (team == "Blue" && npc.homeTileX < boundary);

                if (outOfBounds)
                {
                    npc.homeless = true;
                    TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", npcIndex);
                    TShock.Log.ConsoleInfo($"[TeamNPC] {team} {npc.TypeName} home out of bounds, kicked homeless");
                }
            }

            // All town NPCs: if a non-registered NPC self-assigned to wrong side, kick it
            Point redSpawn = teamStates["Red"].SpawnCenter;
            Point blueSpawn = teamStates["Blue"].SpawnCenter;
            if (redSpawn.X > 0 && blueSpawn.X > 0)
            {
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC npc = Main.npc[i];
                    if (npc == null || !npc.active || !npc.townNPC || npc.homeless)
                        continue;

                    // Red side: homeTileX should be < boundary
                    // Blue side: homeTileX should be > boundary
                    // If home is on red side (< boundary), it belongs to red; if on blue side (> boundary), to blue
                    bool homeOnRedSide = npc.homeTileX < boundary;
                    bool homeOnBlueSide = npc.homeTileX > boundary;

                    string npcTeam = npcManager.GetNPCTeam(i);

                    if (npcTeam == "Red" && !homeOnRedSide)
                    {
                        npc.homeless = true;
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                        TShock.Log.ConsoleInfo($"[TeamNPC] Red {npc.TypeName} self-assigned to blue side, kicked");
                    }
                    else if (npcTeam == "Blue" && !homeOnBlueSide)
                    {
                        npc.homeless = true;
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                        TShock.Log.ConsoleInfo($"[TeamNPC] Blue {npc.TypeName} self-assigned to red side, kicked");
                    }
                    else if (npcTeam == null)
                    {
                        // Unregistered NPC: kick if it squatted on either team's side
                        if (homeOnRedSide || homeOnBlueSide)
                        {
                            npc.homeless = true;
                            TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                            TShock.Log.ConsoleInfo($"[TeamNPC] Unregistered {npc.TypeName} self-assigned to team side, kicked");
                        }
                    }
                }
            }

            EnforcePlayerLockedHomes();
        }

        private void EnforcePlayerLockedHomes()
        {
            if (playerLockedHomes.Count == 0)
                return;

            foreach (var kvp in playerLockedHomes.ToList())
            {
                int lockedIndex = kvp.Key;
                Point lockedHome = kvp.Value;

                if (lockedIndex < 0 || lockedIndex >= Main.maxNPCs || !Main.npc[lockedIndex].active)
                {
                    playerLockedHomes.Remove(lockedIndex);
                    continue;
                }

                NPC lockedNpc = Main.npc[lockedIndex];

                if (lockedNpc.homeTileX != lockedHome.X || lockedNpc.homeTileY != lockedHome.Y)
                {
                    lockedNpc.homeTileX = lockedHome.X;
                    lockedNpc.homeTileY = lockedHome.Y;
                    lockedNpc.homeless = false;
                    TSPlayer.All.SendData(PacketTypes.UpdateNPCHome, "", lockedIndex,
                        lockedHome.X, lockedHome.Y, 0);
                    TShock.Log.ConsoleInfo($"[TeamNPC] Restored locked home for {lockedNpc.TypeName} at ({lockedHome.X},{lockedHome.Y})");
                }

                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (i == lockedIndex) continue;
                    NPC other = Main.npc[i];
                    if (other == null || !other.active || !other.townNPC || other.homeless)
                        continue;

                    if (other.homeTileX == lockedHome.X && other.homeTileY == lockedHome.Y)
                    {
                        other.homeless = true;
                        TSPlayer.All.SendData(PacketTypes.NpcUpdate, "", i);
                        TShock.Log.ConsoleInfo($"[TeamNPC] Evicted {other.TypeName} from locked home ({lockedHome.X},{lockedHome.Y})");
                    }
                }
            }
        }
    }
}
