using UnityEngine;

namespace TEngine.Localization
{
	[AddComponentMenu("I2/Localization/SetLanguage Button")]
	public class SetLanguage : MonoBehaviour 
	{
		public string _Language;

#if UNITY_EDITOR
		public LanguageSource mSource;
#endif
		
		void OnClick()
		{
			ApplyLanguage();
        }

		public void ApplyLanguage()
		{
			if( LocalizationManager.HasLanguage(_Language))
			{
				LocalizationManager.CurrentLanguage = _Language;
			}
		}
    }
}