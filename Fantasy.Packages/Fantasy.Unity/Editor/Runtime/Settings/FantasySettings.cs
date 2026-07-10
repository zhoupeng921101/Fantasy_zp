using UnityEditor;

namespace Fantasy
{
    public class FantasySettings
    {
        [MenuItem("服务器/Fantasy Settings")]
        public static void OpenFantasySettings()
        {
            SettingsService.OpenProjectSettings("Project/Fantasy Settings");
        }
    }
}