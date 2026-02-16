using System.Collections.Generic;
using System.Linq;

namespace TeamNPCPlugin
{
    public class NPCTeamManager
    {
        // NPC index -> Team affiliation (Red/Blue)
        private Dictionary<int, string> npcTeamMap = new Dictionary<int, string>();

        // NPC index -> NPC name
        private Dictionary<int, string> npcNameMap = new Dictionary<int, string>();

        // NPC index -> NPC type ID
        private Dictionary<int, int> npcTypeMap = new Dictionary<int, int>();

        // (NPC name, Team) -> NPC index list
        private Dictionary<(string npcName, string team), List<int>> teamNPCRegistry =
            new Dictionary<(string, string), List<int>>();

        public void RegisterNPC(int npcIndex, string npcName, string team, int npcType = -1)
        {
            npcTeamMap[npcIndex] = team;
            npcNameMap[npcIndex] = npcName;
            if (npcType >= 0)
                npcTypeMap[npcIndex] = npcType;

            var key = (npcName, team);
            if (!teamNPCRegistry.ContainsKey(key))
                teamNPCRegistry[key] = new List<int>();

            teamNPCRegistry[key].Add(npcIndex);
        }

        public void UnregisterNPC(int npcIndex)
        {
            if (!npcTeamMap.ContainsKey(npcIndex))
                return;

            string team = npcTeamMap[npcIndex];
            npcTeamMap.Remove(npcIndex);
            npcNameMap.Remove(npcIndex);
            npcTypeMap.Remove(npcIndex);

            // Remove from registry
            foreach (var kvp in teamNPCRegistry.ToList())
            {
                kvp.Value.Remove(npcIndex);
            }
        }

        public string GetNPCTeam(int npcIndex)
        {
            return npcTeamMap.ContainsKey(npcIndex) ? npcTeamMap[npcIndex] : null;
        }

        public string GetNPCName(int npcIndex)
        {
            return npcNameMap.ContainsKey(npcIndex) ? npcNameMap[npcIndex] : null;
        }

        public int GetNPCCount(string npcName, string team)
        {
            var key = (npcName, team);
            return teamNPCRegistry.ContainsKey(key) ? teamNPCRegistry[key].Count : 0;
        }

        public bool IsNPCRegistered(int npcIndex)
        {
            return npcTeamMap.ContainsKey(npcIndex);
        }

        public int GetNPCType(int npcIndex)
        {
            return npcTypeMap.ContainsKey(npcIndex) ? npcTypeMap[npcIndex] : -1;
        }

        public IEnumerable<int> GetAllRegisteredNPCs()
        {
            return npcTeamMap.Keys.ToList();
        }
    }
}
