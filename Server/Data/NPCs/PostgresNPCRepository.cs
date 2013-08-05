﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Data.NPCs
{
    public class PostgresNPCRepository : PostgresRepository, INPCRepository
    {
        private Dictionary<int, NPCModel> m_npcCache;

        private IReadOnlyCollection<NPCBehaviourModel> m_npcBehaviourCache;
        private IReadOnlyCollection<NPCBehaviourVarModel> m_npcBehaviourVarCache;
        private IReadOnlyCollection<NPCSpawnModel> m_npcSpawnCache;

        private Dictionary<int, IReadOnlyCollection<NPCBehaviourModel>> m_npcBehaviourByNPCIDCache = new Dictionary<int,IReadOnlyCollection<NPCBehaviourModel>>();
        private Dictionary<int, IReadOnlyDictionary<string, string>> m_npcBehaviourVarByNPCBehaviourIDCache = new Dictionary<int,IReadOnlyDictionary<string, string>>();

        public IEnumerable<NPCModel> GetNPCs()
        {
            if (m_npcCache == null)
            {
                m_npcCache = Function<NPCModel>("GET_NPCs").ToDictionary(n => n.NPCID);
            }

            return m_npcCache.Values;
        }

        public IEnumerable<NPCSpawnModel> GetNPCSpawns()
        {
            if (m_npcSpawnCache == null)
            {
                m_npcSpawnCache = Function<NPCSpawnModel>("GET_NPCSpawns").ToList().AsReadOnly();
            }

            return m_npcSpawnCache;
        }

        public NPCModel GetNPCByID(int npcID)
        {
            if (m_npcCache == null)
            {
                GetNPCs();
            }

            NPCModel npc = default(NPCModel);

            m_npcCache.TryGetValue(npcID, out npc);

            return npc;
        }

        public IEnumerable<NPCBehaviourModel> GetNPCBehaviours()
        {
            if (m_npcBehaviourCache == null)
            {
                m_npcBehaviourCache = Function<NPCBehaviourModel>("GET_NPCBehaviours").ToList().AsReadOnly();
            }

            return m_npcBehaviourCache;
        }

        public IEnumerable<NPCBehaviourVarModel> GetNPCBehaviourVars()
        {
            if (m_npcBehaviourVarCache == null)
            {
                m_npcBehaviourVarCache = Function<NPCBehaviourVarModel>("GET_NPCBehaviourVars").ToList().AsReadOnly();
            }

            return m_npcBehaviourVarCache;
        }

        public IEnumerable<NPCBehaviourModel> GetNPCBehavioursByNPCID(int npcID)
        {
            IReadOnlyCollection<NPCBehaviourModel> behaviours = default(IReadOnlyCollection<NPCBehaviourModel>);

            if (!m_npcBehaviourByNPCIDCache.TryGetValue(npcID, out behaviours))
            {
                behaviours = m_npcBehaviourCache.Where(nb => nb.NPCID == npcID).ToList().AsReadOnly();
                m_npcBehaviourByNPCIDCache.Add(npcID, behaviours);
            }

            return behaviours;
        }

        public IReadOnlyDictionary<string, string> GetNPCBehaviourVarsByNPCBehaviourID(int npcBehaviourID)
        {
            IReadOnlyDictionary<string, string> behaviourVars = default(IReadOnlyDictionary<string, string>);

            if (!m_npcBehaviourVarByNPCBehaviourIDCache.TryGetValue(npcBehaviourID, out behaviourVars))
            {
                behaviourVars = m_npcBehaviourVarCache.Where(nbv => nbv.NPCBehaviourID == npcBehaviourID).ToDictionary(b => b.Key, b => b.Value);
                m_npcBehaviourVarByNPCBehaviourIDCache.Add(npcBehaviourID, behaviourVars);
            }

            return behaviourVars;
        }
    }
}
