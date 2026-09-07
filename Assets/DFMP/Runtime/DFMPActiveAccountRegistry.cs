using System.Collections.Generic;

namespace DFMP.Runtime
{
    public sealed class DFMPActiveAccountRegistry
    {
        readonly Dictionary<string, int> connectionByAccount = new Dictionary<string, int>();
        readonly Dictionary<int, string> accountByConnection = new Dictionary<int, string>();

        public bool TryClaim(string accountId, int connectionId)
        {
            int existingConnectionId;
            if (connectionByAccount.TryGetValue(accountId, out existingConnectionId))
                return existingConnectionId == connectionId;

            connectionByAccount[accountId] = connectionId;
            accountByConnection[connectionId] = accountId;
            return true;
        }

        public void Release(int connectionId)
        {
            string accountId;
            if (!accountByConnection.TryGetValue(connectionId, out accountId))
                return;

            accountByConnection.Remove(connectionId);
            connectionByAccount.Remove(accountId);
        }

        public bool IsClaimed(string accountId)
        {
            return connectionByAccount.ContainsKey(accountId);
        }

        public void Clear()
        {
            connectionByAccount.Clear();
            accountByConnection.Clear();
        }
    }
}
