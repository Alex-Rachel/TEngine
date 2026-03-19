using System.Security.Cryptography;
using System.Text;

namespace GameLogic.Regicide
{
    public static class RegicideStateHasher
    {
        public static string ComputeHash(RegicideBattleState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(1024);
            builder.Append(state.SessionId).Append('|');
            builder.Append(state.RuleId).Append('|');
            builder.Append(state.Round).Append('|');
            builder.Append(state.CurrentPlayerIndex).Append('|');
            builder.Append(state.AppliedSequence).Append('|');
            builder.Append(state.IsGameOver ? 1 : 0).Append('|');
            builder.Append(state.IsVictory ? 1 : 0).Append('|');

            if (state.CurrentEnemy != null)
            {
                builder.Append(state.CurrentEnemy.EnemyId).Append(':')
                    .Append(state.CurrentEnemy.Health).Append(':')
                    .Append(state.CurrentEnemy.Attack).Append('|');
            }

            for (int i = 0; i < state.Players.Count; i++)
            {
                RegicidePlayerState player = state.Players[i];
                builder.Append(player.PlayerId).Append(':')
                    .Append(player.SeatIndex).Append(':')
                    .Append(player.Shield).Append(':');
                for (int j = 0; j < player.Hand.Count; j++)
                {
                    RegicideCard card = player.Hand[j];
                    builder.Append((int)card.Suit).Append('-').Append(card.Rank).Append(',');
                }

                builder.Append('|');
            }

            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
            StringBuilder hashBuilder = new StringBuilder(hashBytes.Length * 2);
            for (int i = 0; i < hashBytes.Length; i++)
            {
                hashBuilder.Append(hashBytes[i].ToString("x2"));
            }

            return hashBuilder.ToString();
        }
    }
}
