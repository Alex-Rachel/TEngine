using UnityEngine;

namespace TEngine.Localization
{

    public class AutoChangeCultureInfo : MonoBehaviour
    {
        public void Start()
        {
            LocalizationManager.EnableChangingCultureInfo(true);
        }
    }
}