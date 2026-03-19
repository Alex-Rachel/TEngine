using System;
using System.Collections.Generic;

namespace GameProto
{
    [Serializable]
    public sealed class RegicideEnemyConfig
    {
        public int Id;
        public string Name = string.Empty;
        public int Suit;
        public int Rank;
        public int Health;
        public int Attack;
    }

    [Serializable]
    public sealed class RegicideSuitEffectConfig
    {
        public int Suit;
        public int DamageBonus;
        public int ShieldBonus;
        public bool DrawExtraCard;
    }

    [Serializable]
    public sealed class RegicideRuleConfigRow
    {
        public int Id;
        public int MaxPlayers = 4;
        public int InitialHandSize = 8;
        public int BaseShield = 10;
        public int DefeatHandPenaltyPerOverflow = 1;
        public List<RegicideEnemyConfig> EnemyQueue = new List<RegicideEnemyConfig>();
        public List<RegicideSuitEffectConfig> SuitEffects = new List<RegicideSuitEffectConfig>();
    }

    /// <summary>
    /// Minimal Luban-like table container for regicide rules.
    /// </summary>
    public sealed class TbRegicideRules
    {
        private readonly Dictionary<int, RegicideRuleConfigRow> _rows = new Dictionary<int, RegicideRuleConfigRow>();

        public void Add(RegicideRuleConfigRow row)
        {
            if (row == null)
            {
                return;
            }

            _rows[row.Id] = row;
        }

        public RegicideRuleConfigRow Get(int id)
        {
            _rows.TryGetValue(id, out RegicideRuleConfigRow row);
            return row;
        }

        public IEnumerable<RegicideRuleConfigRow> GetAll()
        {
            return _rows.Values;
        }

        public static TbRegicideRules CreateDefault()
        {
            TbRegicideRules table = new TbRegicideRules();
            RegicideRuleConfigRow row = new RegicideRuleConfigRow
            {
                Id = 1,
                MaxPlayers = 4,
                InitialHandSize = 8,
                BaseShield = 0,
                DefeatHandPenaltyPerOverflow = 1,
            };

            AddEnemyRow(row, 0, "黑桃J", 0, 11, 20, 10);
            AddEnemyRow(row, 1, "红桃J", 1, 11, 20, 10);
            AddEnemyRow(row, 2, "梅花J", 2, 11, 20, 10);
            AddEnemyRow(row, 3, "方块J", 3, 11, 20, 10);
            AddEnemyRow(row, 4, "黑桃Q", 0, 12, 30, 15);
            AddEnemyRow(row, 5, "红桃Q", 1, 12, 30, 15);
            AddEnemyRow(row, 6, "梅花Q", 2, 12, 30, 15);
            AddEnemyRow(row, 7, "方块Q", 3, 12, 30, 15);
            AddEnemyRow(row, 8, "黑桃K", 0, 13, 40, 20);
            AddEnemyRow(row, 9, "红桃K", 1, 13, 40, 20);
            AddEnemyRow(row, 10, "梅花K", 2, 13, 40, 20);
            AddEnemyRow(row, 11, "方块K", 3, 13, 40, 20);

            row.SuitEffects.Add(new RegicideSuitEffectConfig { Suit = 0, DamageBonus = 0, ShieldBonus = 0, DrawExtraCard = false }); // 黑桃：减攻
            row.SuitEffects.Add(new RegicideSuitEffectConfig { Suit = 1, DamageBonus = 0, ShieldBonus = 0, DrawExtraCard = false }); // 红桃：回牌库
            row.SuitEffects.Add(new RegicideSuitEffectConfig { Suit = 2, DamageBonus = 0, ShieldBonus = 0, DrawExtraCard = false }); // 梅花：双倍伤害
            row.SuitEffects.Add(new RegicideSuitEffectConfig { Suit = 3, DamageBonus = 0, ShieldBonus = 0, DrawExtraCard = true });  // 方块：摸牌

            table.Add(row);
            return table;
        }

        private static void AddEnemyRow(
            RegicideRuleConfigRow row,
            int id,
            string name,
            int suit,
            int rank,
            int health,
            int attack)
        {
            row.EnemyQueue.Add(new RegicideEnemyConfig
            {
                Id = id,
                Name = name,
                Suit = suit,
                Rank = rank,
                Health = health,
                Attack = attack,
            });
        }
    }
}
