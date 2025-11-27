using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
public class GameMenuManager : MonoBehaviour
{
    public GameObject settingsCanvas;
    private PlayerInputActions playerInputActions;
    void Awake()
    {
        playerInputActions = new PlayerInputActions(); // Инициализируем наш Input Actions Asset

        // Подписываемся на событие нажатия кнопки Escape
        // Action Map "UI", Action "Escape"
        playerInputActions.UI.Escape.performed += ctx => OnEscapePressed();
    }
    private void OnEscapePressed()
    {
        Debug.Log("esc pressed");
        // Проверяем, активен ли Canvas настроек, прежде чем закрывать его
        if (settingsCanvas != null)
        {
            OpenSettings();
        }
    }
    public void OpenSettings()
    {
        if (settingsCanvas != null)
        {
            settingsCanvas.SetActive(true);  // Активируем Canvas настроек
        }
        Debug.Log("Открыты настройки");
    }
    void OnEnable()
    {
        playerInputActions.Enable(); 
    }
    void OnDisable()
    {
        playerInputActions.Disable(); 
    }
}
