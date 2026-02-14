using UnityEditor;

namespace Fantasy
{
    public class FantasySettings
    {
        [MenuItem("Fantasy/Fantasy Settings")]
        public static void OpenFantasySettings()
        {
            SettingsService.OpenProjectSettings("Project/Fantasy Settings");
        }
    }
}