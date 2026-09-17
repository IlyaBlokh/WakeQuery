using System.Threading;
using System.Threading.Tasks;

namespace WakeQuery.Samples.Basic
{
    public interface IPlayerProfileApi
    {
        Task<PlayerProfile> GetAsync(
            string playerId,
            CancellationToken cancellationToken);

        Task<PlayerProfile> RenameAsync(
            string playerId,
            string displayName,
            CancellationToken cancellationToken);
    }
}
