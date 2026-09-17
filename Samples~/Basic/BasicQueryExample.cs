using System;
using System.Threading;
using System.Threading.Tasks;

namespace WakeQuery.Samples.Basic
{
    public sealed class BasicQueryExample : IDisposable
    {
        private readonly IPlayerProfileApi _api;
        private readonly string _playerId;
        private readonly QueryObserver<PlayerProfile> _profile;
        private readonly Mutation<string, PlayerProfile> _rename;

        public BasicQueryExample(
            QueryClient client,
            IPlayerProfileApi api,
            string playerId,
            Action<QueryState<PlayerProfile>> onProfileChanged)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _playerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _profile = client.Watch(
                PlayerProfileQueries.Profile(api, playerId),
                onProfileChanged);
            _rename = client.CreateMutation(
                new MutationDefinition<string, PlayerProfile>(
                    ExecuteRenameAsync,
                    success =>
                        success.Set(
                            PlayerProfileQueries.Key(_playerId),
                            success.Output)));
        }

        public QueryState<PlayerProfile> ProfileState => _profile.State;

        public MutationState<PlayerProfile> RenameState => _rename.State;

        public Task<PlayerProfile> EnsureProfileAsync(
            CancellationToken cancellationToken = default)
        {
            return _profile.EnsureAsync(cancellationToken);
        }

        public Task<PlayerProfile> RefreshProfileAsync(
            CancellationToken cancellationToken = default)
        {
            return _profile.RefetchAsync(cancellationToken);
        }

        public Task<PlayerProfile> RenameAsync(
            string displayName,
            CancellationToken cancellationToken = default)
        {
            return _rename.ExecuteAsync(displayName, cancellationToken);
        }

        public void Dispose()
        {
            _rename.Dispose();
            _profile.Dispose();
        }

        private Task<PlayerProfile> ExecuteRenameAsync(
            string displayName,
            CancellationToken cancellationToken)
        {
            return _api.RenameAsync(
                _playerId,
                displayName,
                cancellationToken);
        }
    }
}
