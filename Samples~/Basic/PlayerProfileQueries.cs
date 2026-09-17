using System;

namespace WakeQuery.Samples.Basic
{
    public static class PlayerProfileQueries
    {
        public static QueryKey<PlayerProfile> Key(string playerId)
        {
            return QueryKey.For<PlayerProfile>(
                "player-profile",
                QueryKeyPart.Text(playerId));
        }

        public static QueryDefinition<PlayerProfile> Profile(
            IPlayerProfileApi api,
            string playerId)
        {
            return new QueryDefinition<PlayerProfile>(
                Key(playerId),
                cancellationToken =>
                    api.GetAsync(playerId, cancellationToken),
                new QueryPolicy(
                    staleAfter: TimeSpan.FromSeconds(30),
                    unusedFor: TimeSpan.FromMinutes(5),
                    retry: RetryPolicy.Exponential(
                        maxAttempts: 3,
                        initialDelay: TimeSpan.FromSeconds(1))));
        }
    }
}
