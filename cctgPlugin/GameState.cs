using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace cctgPlugin
{
    /// <summary>
    /// Tracks the player's team state
    /// </summary>
    public class PlayerTeamState
    {
        public int LastTeam = 0;
        public DateTime LastTeamChangeTime = DateTime.MinValue;
    }

    /// <summary>
    /// Tracks the player's recall teleport state
    /// </summary>
    public class RecallTeleportState
    {
        public bool WaitingForTeleport = false;
        public bool WaitingToTeleportToTeamHouse = false;
        public DateTime TeleportDetectedTime;
        public Vector2 LastKnownPosition;
        public DateTime LastItemUseTime = DateTime.MinValue;
    }

    /// <summary>
    /// Tracks the player's boundary violation state
    /// </summary>
    public class BoundaryViolationState
    {
        public bool IsOutOfBounds = false;              // Whether the player is currently out of bounds
        public DateTime FirstViolationTime = DateTime.MinValue;  // Time of first boundary violation
        public DateTime ViolationStartTime = DateTime.MinValue;  // Time when the player went out of bounds
        public DateTime LastReturnTime = DateTime.MinValue;      // Last time the player returned within bounds
        public double AccumulatedTime = 0;              // Accumulated out-of-bounds time
        public bool WarningShown = false;               // Whether a warning has been shown
        public DateTime WarningShownTime = DateTime.MinValue;    // Time when the warning was shown
        public bool FirstDamageApplied = false;         // Whether the first damage has been applied
        public DateTime LastDamageTime = DateTime.MinValue;  // Last time damage was applied
        public int DamageCount = 0;                     // Number of times damage has been applied
        public DateTime RecallGraceUntil = DateTime.MinValue;  // Grace period after recall teleport
    }

    /// <summary>
    /// Tracks the player's shop modification state
    /// </summary>
    public class ShopModificationState
    {
        public bool IsModifying = false;                // Whether shop modification is currently active
        public DateTime StartTime = DateTime.MinValue;  // When the modification started
        public int FrameCounter = 0;                   // Frame counter for timing
        public int LastModifiedFrame = 0;              // Last frame when modification was sent
        public int TargetNPCIndex = -1;                 // Target NPC index
    }

    /// <summary>
    /// Tracks gem pickup countdown state for a gem lock
    /// </summary>
    public class GemPickupState
    {
        public int PlayerIndex = -1;
        public int Countdown = 0;
        public DateTime LastTickTime = DateTime.MinValue;
        public bool Completed = false;          // Gem already picked up, stop detecting proximity
        public int CarrierPlayerIndex = -1;     // Player currently carrying the gem (-1 = none)
        public DateTime DroppedTime = DateTime.MinValue; // When gem was dropped on ground
        public bool IsOnGround = false;         // Gem item is on the ground
        public bool Warned450 = false;          // Distance milestone: 450 feet
        public bool Warned300 = false;          // Distance milestone: 300 feet
        public bool Warned150 = false;          // Distance milestone: 150 feet
    }

    /// <summary>
    /// Tracks the player's timer state for /t command
    /// </summary>
    public class HookDropItem
    {
        public int Type;
        public int Stack;
    }

    public class HookDropState
    {
        private static int _nextId = 0;
        public int Id = System.Threading.Interlocked.Increment(ref _nextId);
        public float DeathX;
        public float DeathY;
        public List<HookDropItem> DroppedHooks = new List<HookDropItem>();
        public DateTime LastMessageTime = DateTime.MinValue;
        public DateTime DropTime = DateTime.MinValue;
    }

    public class PlayerTimerState
    {
        public bool IsTimerActive = false;            // Whether the timer is currently active
        public DateTime StartTime = DateTime.MinValue;  // When the timer started
        public double TotalSeconds = 0;               // Total seconds elapsed
        public int PlayerIndex = -1;                   // Player index for reference
    }
}
