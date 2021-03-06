﻿using Data.Abilities;
using Data.NPCs;
using NLog;
using Protocol;
using Server.Abilities;
using Server.Gameplay;
using Server.Map;
using Server.Utility;
using Server.Zones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Server.NPC
{
    public class NPCInstance : IEntity
    {
        private static Logger s_log = LogManager.GetCurrentClassLogger();

        public int ID { get; private set; }

        public NPCModel NPCModel { get; private set; }
        public NPCSpawnModel NPCSpawnModel { get; private set; }

        private List<INPCBehaviour> m_behaviours;
        private Dictionary<Type, INPCBehaviour> m_behavioursByType;

        private IReadOnlyDictionary<StatType, float> m_stats;

        private Fiber m_fiber;

        private EntityStateUpdate m_stateUpdate;
        private EntityIntroduction m_introduction;

        private MapData m_mapData;

        public Vector2 Position { get; set; }

        public string Name
        {
            get { return string.Format("[NPC {0} {1}]", NPCModel.Name, ID); }
        }

        public Vector2 Velocity { get; set; }

        public int Health { get; private set; }
        public int MaxHealth { get; private set; }

        public int Power { get; private set; }
        public int MaxPower { get; private set; }

        public byte Rotation { get; set; }

        public bool IsDead { get; private set; }

        public byte Level { get { return 1; } }

        public NPCInstance(Fiber fiber, NPCModel npc, NPCSpawnModel npcSpawn, List<INPCBehaviour> behaviours, IReadOnlyDictionary<StatType, float> stats, MapData mapData)
        {
            NPCModel = npc;
            NPCSpawnModel = npcSpawn;
            m_stats = stats;
            m_fiber = fiber;
            m_mapData = mapData;

            m_behavioursByType = m_behaviours.ToDictionary(b => b.GetType());

            Position = new Vector2((float)npcSpawn.X, (float)npcSpawn.Y);
            ID = IDGenerator.GetNextID();

            MaxHealth = Formulas.StaminaToHealth(GetStatValue(StatType.Stamina));
            Health = MaxHealth;

            MaxPower = Formulas.LevelToPower(Level);
            Power = MaxPower;

            m_introduction = new EntityIntroduction()
            {
                ID = ID,
                Level = (byte)NPCModel.Level,
                MaxHealth = MaxHealth,
                MaxPower = MaxPower,
                Name = Name,
                ModelID = NPCModel.ModelID
            };

            m_stateUpdate = new EntityStateUpdate()
            {
                Rotation = Compression.RotationToByte(npcSpawn.Rotation),
                ID = ID,
                Health = Health,
                Power = 100,
            };

            m_behaviours = behaviours;
        }

        public void Update(TimeSpan dt)
        {
            if (IsDead)
            {
                return;
            }

            foreach (INPCBehaviour behaviour in m_behaviours)
            {
                behaviour.Update(dt, this);
            }

            m_stateUpdate.X = Compression.PositionToUShort(Position.X);
            m_stateUpdate.Y = Compression.PositionToUShort(Position.Y);
            m_stateUpdate.Rotation = Compression.RotationToByte(Rotation);
            m_stateUpdate.VelX = Compression.VelocityToShort(Velocity.X);
            m_stateUpdate.VelY = Compression.VelocityToShort(Velocity.Y);
            m_stateUpdate.Health = (ushort)Health;
            m_stateUpdate.Timestamp = Environment.TickCount;
        }

        public void ApplyHealthDelta(int delta, IEntity source)
        {
            int newHealth = Health + delta;

            Health = MathHelper.Clamp(newHealth, 0, MaxHealth);

            if (Health == 0)
            {
                Die(source);
            }
        }

        public void ApplyPowerDelta(int delta, IEntity source)
        {
            int newPower = Power + delta;

            Power = MathHelper.Clamp(newPower, 0, MaxPower);
        }

        public void ApplyXPDelta(int delta, IEntity source)
        {
        }

        private void Die(IEntity killer)
        {
            IsDead = true;
            m_fiber.Schedule(Respawn, NPCSpawnModel.Frequency);

            killer.ApplyXPDelta(NPCModel.XP, this);

            Info("Killed by {0}", killer == null ? "[Unknown]" : killer.Name);
        }

        private void Respawn()
        {
            Position = new Vector2((float)NPCSpawnModel.X, (float)NPCSpawnModel.Y);
            Rotation = Compression.RotationToByte(NPCSpawnModel.Rotation);

            Health = MaxHealth;
            Power = MaxPower;

            IsDead = false;

            Info("Respawned");
        }

        public UseAbilityResult AcceptAbilityAsSource(AbilityInstance ability)
        {
            ApplyHealthDelta(ability.Ability.SourceHealthDelta, this);
            return UseAbilityResult.OK;
        }

        public Task<UseAbilityResult> AcceptAbilityAsTarget(AbilityInstance ability)
        {
            return m_fiber.Enqueue(() =>
            {
                if (IsDead)
                {
                    return UseAbilityResult.InvalidTarget;
                }
                else if (Vector2.DistanceSquared(ability.Source.Position, Position) > Math.Pow(ability.Ability.Range, 2))
                {
                    return UseAbilityResult.OutOfRange;
                }
                else
                {
                    int levelBonus = ability.Source.Level * 5;
                    if (ability.Ability.AbilityType == AbilityModel.EAbilityType.HARM)
                    {
                        levelBonus *= -1;
                    }
                    ApplyHealthDelta(ability.Ability.TargetHealthDelta + levelBonus, ability.Source);
                    return UseAbilityResult.OK;
                }
            });
        }

        public void AwardXP(float xp)
        {
        }

        private float GetStatValue(StatType statType)
        {
            float stat;

            m_stats.TryGetValue(statType, out stat);

            return stat;
        }

        public EntityStateUpdate GetStateUpdate()
        {
            return m_stateUpdate;
        }

        public EntityIntroduction GetIntroduction()
        {
            return m_introduction;
        }

        protected List<Vector2> FindPath(Vector2 from, Vector2 to)
        {
            return m_mapData.FindPath(from, to);
        }

        public T GetBehaviour<T>() where T : class, INPCBehaviour
        {
            INPCBehaviour behaviour = null;

            m_behavioursByType.TryGetValue(typeof(T), out behaviour);

            return behaviour as T;
        }

        #region Logging
        private const string LOG_FORMAT = "[{0}] {1}: {2}";
        private void Trace(string message, params object[] args)
        {
            s_log.Trace(string.Format(LOG_FORMAT, ID, Name, string.Format(message, args)));
        }
        private void Info(string message, params object[] args)
        {
            s_log.Info(string.Format(LOG_FORMAT, ID, Name, string.Format(message, args)));
        }
        private void Warn(string message, params object[] args)
        {
            s_log.Warn(string.Format(LOG_FORMAT, ID, Name, string.Format(message, args)));
        }
        private void Error(string message, params object[] args)
        {
            s_log.Error(string.Format(LOG_FORMAT, ID, Name, string.Format(message, args)));
        }
        #endregion
    }
}
