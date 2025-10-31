
public static class TestLogger
{
    public static void Log(System.Object instance, string message)
    {
        UnityEngine.Debug.Log($"[{instance.GetType().Name}] {message}");
    }
}