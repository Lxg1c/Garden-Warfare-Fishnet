using UnityEngine;

namespace UI.Menu
{
    public class GameMenuManager : MonoBehaviour
    {
        public GameObject settingsCanvas;
        private PlayerInputActions _playerInputActions;
        void Awake()
        {
            _playerInputActions = new PlayerInputActions();
            _playerInputActions.UI.Escape.performed += _ => OnEscapePressed();
        }
        private void OnEscapePressed()
        {
            Debug.Log("esc pressed");
            if (settingsCanvas != null)
            {
                OpenSettings();
            }
        }
        public void OpenSettings()
        {
            if (settingsCanvas != null)
            {
                settingsCanvas.SetActive(true);
            }
            Debug.Log("Открыты настройки");
        }
        void OnEnable()
        {
            _playerInputActions.Enable(); 
        }
        void OnDisable()
        {
            _playerInputActions.Disable(); 
        }
    }

}
