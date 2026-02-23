using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace TeamNPCPlugin
{
    public class SpawnRule
    {
        public Func<TeamState, bool> Condition { get; set; }
        public int NpcType { get; set; }
        public string DisplayName { get; set; }
        public string Key { get; set; }
    }

    public static class TownNPCDefinitions
    {
        public static readonly List<SpawnRule> PriorityOrder = new()
        {
            new SpawnRule
            {
                Key = "Guide", NpcType = NPCID.Guide, DisplayName = "Guide",
                Condition = team => true
            },
            new SpawnRule
            {
                Key = "Merchant", NpcType = NPCID.Merchant, DisplayName = "Merchant",
                Condition = team => team.Money >= 10000
            },
            new SpawnRule
            {
                Key = "Nurse", NpcType = NPCID.Nurse, DisplayName = "Nurse",
                Condition = team => team.PlayerUsedLifeCrystal && team.HasNPC("Merchant")
            },
            new SpawnRule
            {
                Key = "ArmsDealer", NpcType = NPCID.ArmsDealer, DisplayName = "Arms Dealer",
                Condition = team => team.PlayerHasGun
            },
            new SpawnRule
            {
                Key = "GoblinTinkerer", NpcType = NPCID.GoblinTinkerer, DisplayName = "Goblin Tinkerer",
                Condition = team => NPC.savedGoblin
            },
            new SpawnRule
            {
                Key = "Wizard", NpcType = NPCID.Wizard, DisplayName = "Wizard",
                Condition = team => NPC.savedWizard
            },
            new SpawnRule
            {
                Key = "Dryad", NpcType = NPCID.Dryad, DisplayName = "Dryad",
                Condition = team => NPC.downedBoss1 || NPC.downedBoss2 || NPC.downedBoss3
            },
            new SpawnRule
            {
                Key = "Demolitionist", NpcType = NPCID.Demolitionist, DisplayName = "Demolitionist",
                Condition = team => team.PlayerHasExplosive && team.HasNPC("Merchant")
            },
            new SpawnRule
            {
                Key = "WitchDoctor", NpcType = NPCID.WitchDoctor, DisplayName = "Witch Doctor",
                Condition = team => NPC.downedQueenBee && !team.PlayerInSnow
            },
            new SpawnRule
            {
                Key = "Steampunker", NpcType = NPCID.Steampunker, DisplayName = "Steampunker",
                Condition = team => NPC.downedMechBossAny
            },
            new SpawnRule
            {
                Key = "Mechanic", NpcType = NPCID.Mechanic, DisplayName = "Mechanic",
                Condition = team => NPC.savedMech && !team.PlayerInJungle
            },
            new SpawnRule
            {
                Key = "Angler", NpcType = NPCID.Angler, DisplayName = "Angler",
                Condition = team => NPC.savedAngler
            },
            new SpawnRule
            {
                Key = "Cyborg", NpcType = NPCID.Cyborg, DisplayName = "Cyborg",
                Condition = team => Main.hardMode && NPC.downedPlantBoss
            },
            new SpawnRule
            {
                Key = "Pirate", NpcType = NPCID.Pirate, DisplayName = "Pirate",
                Condition = team => NPC.downedPirates
            },
            new SpawnRule
            {
                Key = "Clothier", NpcType = NPCID.Clothier, DisplayName = "Clothier",
                Condition = team => NPC.downedBoss3
            },
            new SpawnRule
            {
                Key = "Stylist", NpcType = NPCID.Stylist, DisplayName = "Stylist",
                Condition = team => NPC.savedStylist
            },
            new SpawnRule
            {
                Key = "PartyGirl", NpcType = NPCID.PartyGirl, DisplayName = "Party Girl",
                Condition = team => team.NPCCount >= 20
            },
            new SpawnRule
            {
                Key = "Santa", NpcType = NPCID.SantaClaus, DisplayName = "Santa Claus",
                Condition = team => Main.xMas && NPC.downedFrost
            },
            new SpawnRule
            {
                Key = "Tavernkeep", NpcType = NPCID.DD2Bartender, DisplayName = "Tavernkeep",
                Condition = team => NPC.savedBartender
            },
            new SpawnRule
            {
                Key = "Golfer", NpcType = NPCID.Golfer, DisplayName = "Golfer",
                Condition = team => NPC.savedGolfer
            },
            new SpawnRule
            {
                Key = "TaxCollector", NpcType = NPCID.TaxCollector, DisplayName = "Tax Collector",
                Condition = team => NPC.savedTaxCollector
            },
            new SpawnRule
            {
                Key = "Truffle", NpcType = NPCID.Truffle, DisplayName = "Truffle",
                Condition = team => false
            },
            // Zoologist disabled
            new SpawnRule
            {
                Key = "Princess", NpcType = NPCID.Princess, DisplayName = "Princess",
                Condition = team => team.NPCCount >= 25
            },
        };

        public static Dictionary<string, SpawnRule> TownNpcSpawnRules;

        static TownNPCDefinitions()
        {
            TownNpcSpawnRules = new Dictionary<string, SpawnRule>();
            foreach (var rule in PriorityOrder)
                TownNpcSpawnRules[rule.Key] = rule;
        }

        public static int GetMaxAllowed(string npcName)
        {
            return 1;
        }
    }
}
