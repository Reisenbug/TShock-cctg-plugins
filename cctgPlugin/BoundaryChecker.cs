using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;

namespace cctgPlugin
{
    /// <summary>
    /// Boundary check manager
    /// </summary>
    public class BoundaryChecker
    {
        // Game start time
        private DateTime gameStartTime = DateTime.MinValue;

        // Boundary check duration (18 minutes)
        private const double BOUNDARY_CHECK_DURATION = 18 * 60;

        // Player boundary violation states
        private Dictionary<int, BoundaryViolationState> playerBoundaryStates = new Dictionary<int, BoundaryViolationState>();

        // Game started flag
        private bool gameStarted = false;

        /// <summary>
        /// Start boundary check
        /// </summary>
        public void StartBoundaryCheck()
        {
            gameStarted = true;
            gameStartTime = DateTime.Now;
            playerBoundaryStates.Clear();
            TShock.Log.ConsoleInfo("[CCTG] Game started! Boundary check active (18 minutes)");
        }

        /// <summary>
        /// Stop boundary check
        /// </summary>
        public void StopBoundaryCheck()
        {
            gameStarted = false;
            playerBoundaryStates.Clear();
        }

        /// <summary>
        /// Clear all player boundary states
        /// </summary>
        public void ClearBoundaryStates()
        {
            playerBoundaryStates.Clear();
        }

        /// <summary>
        /// Set recall grace period for a player (reset boundary state and pause checking for 0.6s)
        /// </summary>
        public void SetRecallGrace(int playerIndex)
        {
            if (!playerBoundaryStates.ContainsKey(playerIndex))
            {
                playerBoundaryStates[playerIndex] = new BoundaryViolationState();
            }
            var state = playerBoundaryStates[playerIndex];
            state.RecallGraceUntil = DateTime.Now.AddSeconds(0.6);

            // Reset boundary violation state
            state.IsOutOfBounds = false;
            state.AccumulatedTime = 0;
            state.FirstViolationTime = DateTime.MinValue;
            state.ViolationStartTime = DateTime.MinValue;
            state.LastReturnTime = DateTime.MinValue;
            state.WarningShown = false;
            state.WarningShownTime = DateTime.MinValue;
            state.FirstDamageApplied = false;
            state.DamageCount = 0;
        }

        /// <summary>
        /// Boundary check and punishment
        /// </summary>
        public void CheckBoundaryViolation(TSPlayer player)
        {
            if (!gameStarted || gameStartTime == DateTime.MinValue)
            {
                return;
            }

            // Check if within 18 minutes
            double timeSinceStart = (DateTime.Now - gameStartTime).TotalSeconds;
            if (timeSinceStart > BOUNDARY_CHECK_DURATION)
                return;

            // Get player team
            int playerTeam = player.TPlayer.team;
            if (playerTeam != 1 && playerTeam != 3)
                return;

            // Initialize player boundary state
            if (!playerBoundaryStates.ContainsKey(player.Index))
            {
                playerBoundaryStates[player.Index] = new BoundaryViolationState();
            }

            var state = playerBoundaryStates[player.Index];

            // Skip if in recall grace period
            if (state.RecallGraceUntil != DateTime.MinValue && DateTime.Now < state.RecallGraceUntil)
                return;

            // Get spawn X coordinate
            int spawnX = Main.spawnTileX;
            int playerTileX = (int)(player.TPlayer.position.X / 16);

            // Check if out of bounds
            bool isOutOfBounds = false;
            if (playerTeam == 1) // Red team: from left, cannot cross spawn
            {
                isOutOfBounds = playerTileX >= spawnX;
            }
            else if (playerTeam == 3) // Blue team: from right, cannot cross spawn
            {
                isOutOfBounds = playerTileX <= spawnX;
            }

            // Handle boundary state changes
            if (isOutOfBounds)
            {
                // Just went out of bounds
                if (!state.IsOutOfBounds)
                {
                    // Check if within 5-second return window
                    if (state.LastReturnTime != DateTime.MinValue)
                    {
                        double timeSinceReturn = (DateTime.Now - state.LastReturnTime).TotalSeconds;
                        if (timeSinceReturn <= 3.0)
                        {
                            // Out of bounds again within 5 seconds, timer continues
                            state.IsOutOfBounds = true;
                            state.ViolationStartTime = DateTime.Now;
                            TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} out of bounds again, timer continues (accumulated {state.AccumulatedTime:F1}s)");
                        }
                        else
                        {
                            // Out of bounds again after 5 seconds, reset state
                            state.IsOutOfBounds = true;
                            state.ViolationStartTime = DateTime.Now;
                            state.FirstViolationTime = DateTime.Now;
                            state.AccumulatedTime = 0;
                            state.WarningShown = false;
                            state.WarningShownTime = DateTime.MinValue;
                            state.FirstDamageApplied = false;
                            state.DamageCount = 0;
                            TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} out of bounds (team={playerTeam}, pos={playerTileX}, spawn={spawnX})");
                        }
                    }
                    else
                    {
                        // First violation
                        state.IsOutOfBounds = true;
                        state.ViolationStartTime = DateTime.Now;
                        state.FirstViolationTime = DateTime.Now;
                        state.AccumulatedTime = 0;
                        state.WarningShown = false;
                        state.WarningShownTime = DateTime.MinValue;
                        state.FirstDamageApplied = false;
                        state.DamageCount = 0;
                        TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} out of bounds (team={playerTeam}, pos={playerTileX}, spawn={spawnX})");
                    }
                }

                // Calculate accumulated time
                double currentViolationTime = (DateTime.Now - state.ViolationStartTime).TotalSeconds;
                double totalTime = state.AccumulatedTime + currentViolationTime;

                // No warning within 0.6 seconds
                if (totalTime <= 0.6)
                {
                    return;
                }

                // After 0.6s: show warning
                if (!state.WarningShown)
                {
                    player.SendErrorMessage("You are out of bounds!");
                    state.WarningShown = true;
                    state.WarningShownTime = DateTime.Now;
                    TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} boundary warning ({totalTime:F1}s)");
                }

                // Apply damage: 5 * 1.5^(damageCount), every 1 second after warning
                if (state.WarningShown && state.WarningShownTime != DateTime.MinValue)
                {
                    double timeSinceWarning = (DateTime.Now - state.WarningShownTime).TotalSeconds;

                    // First damage 1s after warning, then every 1s
                    if (timeSinceWarning >= 1.5)
                    {
                        bool shouldDamage = false;

                        if (!state.FirstDamageApplied)
                        {
                            shouldDamage = true;
                        }
                        else
                        {
                            double timeSinceLastDamage = (DateTime.Now - state.LastDamageTime).TotalSeconds;
                            if (timeSinceLastDamage >= 1.0)
                            {
                                shouldDamage = true;
                            }
                        }

                        if (shouldDamage)
                        {
                            // Damage formula: 5 * 1.5^(damageCount)
                            int damage = (int)(5 * Math.Pow(1.5, state.DamageCount));
                            if (damage > 200)
                                damage = 200;

                            player.DamagePlayer(damage);

                            state.FirstDamageApplied = true;
                            state.LastDamageTime = DateTime.Now;
                            state.DamageCount++;
                            TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} boundary damage {damage}hp (count={state.DamageCount})");
                        }
                    }
                }
            }
            else
            {
                // Player returned to bounds
                if (state.IsOutOfBounds)
                {
                    double thisViolationTime = (DateTime.Now - state.ViolationStartTime).TotalSeconds;
                    state.AccumulatedTime += thisViolationTime;
                    state.IsOutOfBounds = false;
                    state.LastReturnTime = DateTime.Now;

                    TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} returned to bounds (accumulated {state.AccumulatedTime:F1}s)");
                }
                else
                {
                    // Check if over 5 seconds, reset
                    if (state.LastReturnTime != DateTime.MinValue)
                    {
                        double timeSinceReturn = (DateTime.Now - state.LastReturnTime).TotalSeconds;
                        if (timeSinceReturn > 3.0)
                        {
                            state.AccumulatedTime = 0;
                            state.FirstViolationTime = DateTime.MinValue;
                            state.LastReturnTime = DateTime.MinValue;
                            state.WarningShown = false;
                            state.WarningShownTime = DateTime.MinValue;
                            state.FirstDamageApplied = false;
                            state.DamageCount = 0;
                            TShock.Log.ConsoleInfo($"[CCTG] Player {player.Name} boundary timer reset");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get debug info
        /// </summary>
        public string GetDebugInfo(TSPlayer player)
        {
            if (!gameStarted || gameStartTime == DateTime.MinValue)
            {
                return "Game not started";
            }

            double timeSinceStart = (DateTime.Now - gameStartTime).TotalSeconds;
            int playerTeam = player.TPlayer.team;
            int spawnX = Main.spawnTileX;
            int playerTileX = (int)(player.TPlayer.position.X / 16);

            bool isOut = false;
            if (playerTeam == 1)
            {
                isOut = playerTileX >= spawnX;
            }
            else if (playerTeam == 3)
            {
                isOut = playerTileX <= spawnX;
            }

            string info = $"Game time: {timeSinceStart:F1} seconds\n";
            info += $"Boundary check duration: {BOUNDARY_CHECK_DURATION} seconds ({BOUNDARY_CHECK_DURATION / 60} minutes)\n";
            info += $"Boundary check active: {timeSinceStart <= BOUNDARY_CHECK_DURATION}\n";
            info += $"Player team: {playerTeam}\n";
            info += $"Player position: {playerTileX}\n";
            info += $"Spawn point: {spawnX}\n";
            info += $"Boundary check: {isOut}\n";

            if (playerBoundaryStates.ContainsKey(player.Index))
            {
                var state = playerBoundaryStates[player.Index];
                info += $"\nCurrent out of bounds: {state.IsOutOfBounds}\n";
                info += $"Accumulated violation time: {state.AccumulatedTime:F2} seconds\n";
                info += $"Warning shown: {state.WarningShown}\n";
                info += $"First damage applied: {state.FirstDamageApplied}\n";
            }

            return info;
        }
    }
}
