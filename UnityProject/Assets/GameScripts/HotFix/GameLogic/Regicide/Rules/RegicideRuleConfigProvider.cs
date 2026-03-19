using System.Collections.Generic;
using GameProto;

namespace GameLogic.Regicide
{
    public interface IRegicideRuleConfigProvider
    {
        RegicideRuleConfig GetByRuleId(int ruleId);
        bool Validate(out string error);
    }

    public sealed class RegicideRuleConfigProvider : Singleton<RegicideRuleConfigProvider>, IRegicideRuleConfigProvider
    {
        private TbRegicideRules _rulesTable;
        private readonly Dictionary<int, RegicideRuleConfig> _cache = new Dictionary<int, RegicideRuleConfig>();

        protected override void OnInit()
        {
            _rulesTable = TbRegicideRules.CreateDefault();
            WarmupCache();
        }

        private void WarmupCache()
        {
            _cache.Clear();
            foreach (RegicideRuleConfigRow row in _rulesTable.GetAll())
            {
                _cache[row.Id] = Convert(row);
            }
        }

        public RegicideRuleConfig GetByRuleId(int ruleId)
        {
            if (_cache.TryGetValue(ruleId, out RegicideRuleConfig config))
            {
                return config;
            }

            RegicideRuleConfigRow row = _rulesTable.Get(ruleId);
            if (row == null)
            {
                return null;
            }

            config = Convert(row);
            _cache[ruleId] = config;
            return config;
        }

        public bool Validate(out string error)
        {
            foreach (RegicideRuleConfig config in _cache.Values)
            {
                if (config.MaxPlayers < 1 || config.MaxPlayers > 4)
                {
                    error = $"Invalid max players: {config.MaxPlayers}";
                    return false;
                }

                if (config.InitialHandSize <= 0)
                {
                    error = "Initial hand size must be positive.";
                    return false;
                }

                if (config.EnemyQueue.Count == 0)
                {
                    error = "Enemy queue is empty.";
                    return false;
                }

                for (int i = 0; i < config.EnemyQueue.Count; i++)
                {
                    RegicideEnemyState enemy = config.EnemyQueue[i];
                    if (enemy.Health <= 0 || enemy.Attack <= 0)
                    {
                        error = $"Invalid enemy row at index {i}.";
                        return false;
                    }

                    if (enemy.Suit < RegicideSuit.Spade || enemy.Suit > RegicideSuit.Diamond)
                    {
                        error = $"Invalid enemy suit at index {i}.";
                        return false;
                    }

                    if (enemy.Rank < 11 || enemy.Rank > 13)
                    {
                        error = $"Invalid enemy rank at index {i}.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private static RegicideRuleConfig Convert(RegicideRuleConfigRow row)
        {
            RegicideRuleConfig result = new RegicideRuleConfig
            {
                RuleId = row.Id,
                MaxPlayers = row.MaxPlayers,
                InitialHandSize = row.InitialHandSize,
                BaseShield = row.BaseShield,
                DefeatHandPenaltyPerOverflow = row.DefeatHandPenaltyPerOverflow,
                EnemyQueue = new List<RegicideEnemyState>(),
                SuitEffects = new List<RegicideSuitEffect>(),
            };

            for (int i = 0; i < row.EnemyQueue.Count; i++)
            {
                RegicideEnemyConfig enemy = row.EnemyQueue[i];
                result.EnemyQueue.Add(new RegicideEnemyState
                {
                    EnemyId = enemy.Id,
                    Name = enemy.Name,
                    Suit = (RegicideSuit)enemy.Suit,
                    Rank = enemy.Rank,
                    Health = enemy.Health,
                    Attack = enemy.Attack,
                    Defeated = false,
                });
            }

            for (int i = 0; i < row.SuitEffects.Count; i++)
            {
                RegicideSuitEffectConfig effect = row.SuitEffects[i];
                result.SuitEffects.Add(new RegicideSuitEffect
                {
                    Suit = (RegicideSuit)effect.Suit,
                    DamageBonus = effect.DamageBonus,
                    ShieldBonus = effect.ShieldBonus,
                    DrawExtraCard = effect.DrawExtraCard,
                });
            }

            return result;
        }
    }
}
