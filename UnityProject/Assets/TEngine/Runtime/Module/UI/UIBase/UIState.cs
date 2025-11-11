namespace TEngine
{
    public enum UIState:byte
    {
        Uninitialized,
        CreatedUI,
        Loaded,
        Initialized,
        Opened,
        Closed,
        Destroying,
        Destroyed,
    }
}
