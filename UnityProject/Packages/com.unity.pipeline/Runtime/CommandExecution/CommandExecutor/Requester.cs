namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Who sent a request, as the client says, for the analytics report once it ends: who drove it
    /// (human, agent, ci), which AI client family, the client's own id for the invocation that
    /// sent it, which several requests can share, and what invoked the client and for which skill.
    /// Its default says nothing: every field null.
    /// </summary>
    readonly struct Requester
    {
        public Requester(string source, string name, long? invocationId, string caller = null, string skill = null)
        {
            Source = source;
            Name = name;
            InvocationId = invocationId;
            Caller = caller;
            Skill = skill;
        }

        /// <summary>Who drove the client: <c>human</c>, <c>agent</c>, <c>ci</c> or <c>unknown</c>; null when it didn't say.</summary>
        public string Source { get; }

        /// <summary>Which AI client family drove the client, such as <c>claude</c> or <c>none</c>; null when it didn't say.</summary>
        public string Name { get; }

        /// <summary>
        /// The client's own id for the invocation that sent the request, as the HTTP API's
        /// <c>commandId</c>; null when it sent none. An invocation's retries share it.
        /// </summary>
        public long? InvocationId { get; }

        /// <summary>What invoked the client, such as <c>plugin</c> or <c>other</c>; null when it didn't say.</summary>
        public string Caller { get; }

        /// <summary>The agent skill driving the client, such as <c>unity-editor</c>; null when it didn't say.</summary>
        public string Skill { get; }
    }
}
