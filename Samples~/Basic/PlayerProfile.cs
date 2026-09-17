namespace WakeQuery.Samples.Basic
{
    public sealed class PlayerProfile
    {
        public PlayerProfile(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }

        public string DisplayName { get; }
    }
}
