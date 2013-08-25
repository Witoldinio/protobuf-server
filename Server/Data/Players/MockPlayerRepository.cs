﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Data.Players
{
    public class MockPlayerRepository : IPlayerRepository
    {
        private Random m_rand = new Random(Environment.TickCount);
        private static int s_nextPlayerID;

        public PlayerModel GetPlayerByID(int playerID)
        {
            return GetPlayerModel("Player" + playerID);
        }

        public IEnumerable<PlayerModel> GetPlayersByAccountID(int accountID)
        {
            return Enumerable.Range(0, 1).Select(i => GetPlayerModel("Player" + accountID));
        }

        public IEnumerable<PlayerStatModel> GetPlayerStatsByPlayerID(int playerID)
        {
            return GetPlayerStats();
        }

        public void UpdatePlayer(int playerID, int accountID, string name, float health, float power, long money, int map, float x, float y, float rotation)
        {
        }

        public void UpdatePlayerStat(int playerStatID, int playerID, int statID, float statValue)
        {
        }

        private PlayerModel GetPlayerModel(string name)
        {
            int playerID = Interlocked.Increment(ref s_nextPlayerID);

            PlayerModel player = new PlayerModel()
            {
                AccountID = m_rand.Next(),
                Health = (float)m_rand.NextDouble(),
                Map = 0,
                Money = 0,
                Name = name,
                PlayerID = m_rand.Next(),
                Power = (float)m_rand.NextDouble(),
                Rotation = 0,
                X = m_rand.Next(0, 6000),
                Y = m_rand.Next(0, 6000)
            };
            player.X = 2500;
            player.Y = 2500;

            return player;
        }

        private IEnumerable<PlayerStatModel> GetPlayerStats()
        {
            return new List<PlayerStatModel>()
            {
                new PlayerStatModel() { StatID = 1, StatValue = 25 },
                new PlayerStatModel() { StatID = 2, StatValue = 500 }
            };
        }
    }
}