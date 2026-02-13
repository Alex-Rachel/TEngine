#if FANTASY_CONSOLE
#pragma warning disable CS8601 // Possible null reference assignment.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
namespace Fantasy
{
    public sealed class ThreadSynchronizationContext : SynchronizationContext
    {
        private Action _actionHandler;
        private readonly Queue<Action> _queue = new();
        
        public void Update()
        {
            while (_queue.TryDequeue(out _actionHandler))
            {
                try
                {
                    _actionHandler();
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }
        }
        
        public override void Post(SendOrPostCallback callback, object state)
        {
            Post(() => callback(state));
        }
        
        public void Post(Action action)
        {
            _queue.Enqueue(action);
        }
    }
}
#endif