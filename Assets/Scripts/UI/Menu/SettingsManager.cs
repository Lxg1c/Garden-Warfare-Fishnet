using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
// using UnityEditor.Rendering; // Обычно не требуется в сборках, если не используется что-то специфичное для редактора
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class SettingsManager : MonoBehaviour
{
    [Header("FPS Settings")]
    public Slider fpsSlider;
    public TMP_Text fpsText;
    public TMP_InputField fpsInputField;
    public int minFPS = 60; // Минимальное значение FPS
    public int maxFPS = 240; // Максимальное значение FPS

    [Header("Resolution Settings")]
    public TMP_Dropdown resolutionDropdown;
    private Resolution[] allResolutions; // Все доступные разрешения (включая частоту обновления)
    private List<string> resolutionOptions = new List<string>(); // Список строк для Dropdown
    private int currentResolutionIndex = 0; // Индекс текущего разрешения

    [Header("Screen Mode Settings")]
    public TMP_Dropdown screenModeDropdown; // Dropdown для выбора режима экрана
    private FullScreenMode currentFullScreenMode; // Текущий выбранный режим экрана

    [Header("UI References")]
    public GameObject settingsCanvas;
    private PlayerInputActions playerInputActions;

    void Awake()
    {
        playerInputActions = new PlayerInputActions(); // Инициализируем наш Input Actions Asset
        playerInputActions.UI.Escape.performed += ctx => OnEscapePressed();
    }

    private void Start()
    {
        // --- Настройка FPS ---
        if (fpsSlider != null)
        {
            fpsSlider.minValue = minFPS;
            fpsSlider.maxValue = maxFPS;
            fpsSlider.onValueChanged.AddListener(SetFPSFromSlider);
            // Устанавливаем текущее значение FPS или максимум, если не ограничено
            fpsSlider.value = Application.targetFrameRate == -1 ? maxFPS : Application.targetFrameRate;
        }
        if (fpsInputField != null)
        {
            fpsInputField.onEndEdit.AddListener(SetFPSFromInputField);
        }
        UpdateFPSUI(Application.targetFrameRate == -1 ? maxFPS : Application.targetFrameRate);

        // --- Настройка Dropdown для режима экрана ---
        if (screenModeDropdown != null)
        {
            screenModeDropdown.ClearOptions();
            List<string> modeOptions = new List<string>
            {
                "Полноэкранный",        // FullScreenMode.ExclusiveFullScreen
                "Полноэкранный в окне", // FullScreenMode.FullScreenWindow
                "Оконный"               // FullScreenMode.Windowed
            };
            screenModeDropdown.AddOptions(modeOptions);
            screenModeDropdown.onValueChanged.AddListener(SetScreenMode);
        }

        // Загружаем все сохраненные настройки (FPS, режим экрана, разрешение)
        LoadSettings();

        // --- Настройка Dropdown для разрешения ---
        if (resolutionDropdown != null)
        {
            // Здесь мы не очищаем и не добавляем опции, так как это уже сделано в InitializeResolutions
            // во время вызова LoadSettings -> SetScreenMode -> InitializeResolutions.
            // Просто убедимся, что слушатель добавлен.
            resolutionDropdown.onValueChanged.AddListener(SetResolution);
        }

        Debug.Log("Initial Screen FullScreenMode: " + Screen.fullScreenMode + " Width: " + Screen.width + " Height: " + Screen.height);
    }

    private void InitializeResolutions()
    {
        resolutionOptions.Clear(); // Очищаем предыдущие опции
        allResolutions = Screen.resolutions; // Получаем все доступные разрешения монитора

        HashSet<string> uniqueResolutionStrings = new HashSet<string>();

        foreach (var res in allResolutions)
        {
            // Для оконных режимов, можем показывать только разрешения, которые "подходят"
            // к текущему монитору, но для простоты пока показываем все уникальные по размеру.
            // Unity сама будет применять их с учетом ограничений режима.
            string option = res.width + " x " + res.height;

            if (!uniqueResolutionStrings.Contains(option))
            {
                resolutionOptions.Add(option);
                uniqueResolutionStrings.Add(option);
            }
        }

        // Сортируем разрешения (от меньшего к большему)
        resolutionOptions.Sort((res1, res2) =>
        {
            string[] parts1 = res1.Split('x');
            string[] parts2 = res2.Split('x');
            int w1 = int.Parse(parts1[0].Trim());
            int h1 = int.Parse(parts1[1].Trim());
            int w2 = int.Parse(parts2[0].Trim());
            int h2 = int.Parse(parts2[1].Trim());

            if (w1 != w2) return w1.CompareTo(w2);
            return h1.CompareTo(h2);
        });

        // Обновляем Dropdown разрешений в UI
        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(resolutionOptions);
            // Устанавливаем значение Dropdown на текущее разрешение или ближайшее к нему
            UpdateResolutionDropdownValue();
        }
        Debug.Log("Resolutions Initialized for mode: " + currentFullScreenMode + ". Count: " + resolutionOptions.Count);
    }

    public void SetFPSFromSlider(float fpsValue)
    {
        int fps = Mathf.RoundToInt(fpsValue); // Округляем до целого числа
        SetTargetFPS(fps);
        UpdateFPSUI(fps);
    }

    public void SetFPSFromInputField(string fpsString)
    {
        int fps;
        if (int.TryParse(fpsString, out fps)) // Пробуем преобразовать строку в число
        {
            // Ограничиваем FPS в пределах min/max
            fps = Mathf.Clamp(fps, minFPS, maxFPS);
            SetTargetFPS(fps);
            UpdateFPSUI(fps);
        }
        else
        {
            // Если ввод некорректен, возвращаем UI к текущему значению
            UpdateFPSUI(Application.targetFrameRate == -1 ? maxFPS : Application.targetFrameRate);
        }
    }

    private void UpdateFPSUI(int fps)
    {
        if (fpsSlider != null) fpsSlider.value = fps;
        if (fpsText != null) fpsText.text = "FPS: " + fps;
        if (fpsInputField != null) fpsInputField.text = fps.ToString();
        // Debug.Log("Current FPS UI updated to: " + fps);
    }

    private void SetTargetFPS(int fps)
    {
        Application.targetFrameRate = fps;
        PlayerPrefs.SetInt("TargetFPS", fps); // Сохраняем настройку
        PlayerPrefs.Save();
        // Debug.Log("Target FPS set to: " + fps);
    }

    public void SetResolution(int resolutionIndex)
    {
        if (resolutionIndex >= 0 && resolutionIndex < resolutionOptions.Count)
        {
            string selectedOption = resolutionOptions[resolutionIndex];
            string[] parts = selectedOption.Split('x');
            int width = int.Parse(parts[0].Trim());
            int height = int.Parse(parts[1].Trim());

            Resolution bestResolution = new Resolution { width = width, height = height, refreshRateRatio = new RefreshRate { numerator = 0, denominator = 1 } }; // Fallback

            // Ищем разрешение с нужной шириной и высотой, выбирая максимальную доступную частоту обновления
            int maxRefreshRate = 0;
            foreach (var res in allResolutions)
            {
                if (res.width == width && res.height == height)
                {
                    // Unity 2021+ использует refreshRateRatio.value
                    if (res.refreshRateRatio.value > maxRefreshRate)
                    {
                        maxRefreshRate = (int)res.refreshRateRatio.value;
                        bestResolution = res;
                    }
                }
            }

            Screen.SetResolution(bestResolution.width, bestResolution.height, currentFullScreenMode, bestResolution.refreshRateRatio);
            PlayerPrefs.SetInt("ResolutionWidth", bestResolution.width);
            PlayerPrefs.SetInt("ResolutionHeight", bestResolution.height);
            PlayerPrefs.Save();

            currentResolutionIndex = resolutionIndex; // Обновляем текущий индекс
            Debug.Log($"Resolution set to: {bestResolution.width}x{bestResolution.height} @ {bestResolution.refreshRateRatio.value}Hz, Mode: {currentFullScreenMode}");
        }
    }

    public void SetScreenMode(int modeIndex)
    {
        FullScreenMode newMode;
        switch (modeIndex)
        {
            case 0: // Полноэкранный (ExclusiveFullScreen)
                newMode = FullScreenMode.ExclusiveFullScreen;
                break;
            case 1: // Полноэкранный в окне (FullScreenWindow)
                newMode = FullScreenMode.FullScreenWindow;
                break;
            case 2: // Оконный (Windowed)
                newMode = FullScreenMode.Windowed;
                break;
            default:
                newMode = FullScreenMode.FullScreenWindow; // По умолчанию
                break;
        }

        if (currentFullScreenMode != newMode)
        {
            currentFullScreenMode = newMode;
            Screen.SetResolution(Screen.width, Screen.height, currentFullScreenMode); // Применяем режим с текущим разрешением
            PlayerPrefs.SetInt("FullScreenMode", (int)currentFullScreenMode); // Сохраняем
            PlayerPrefs.Save();

            // Переинициализируем разрешения, так как доступные разрешения могут измениться
            // в зависимости от нового режима экрана (хотя Unity может сама адаптировать)
            InitializeResolutions();
            UpdateResolutionDropdownValue(); // Обновляем dropdown разрешений, чтобы он соответствовал новому режиму
            Debug.Log("Screen Mode set to: " + currentFullScreenMode);
        }
    }

    // Обновляет значение Dropdown разрешений, чтобы оно соответствовало текущему разрешению экрана
    private void UpdateResolutionDropdownValue()
    {
        if (resolutionDropdown != null)
        {
            currentResolutionIndex = 0;
            string currentResString = Screen.width + " x " + Screen.height;
            for (int i = 0; i < resolutionOptions.Count; i++)
            {
                if (resolutionOptions[i] == currentResString)
                {
                    currentResolutionIndex = i;
                    break;
                }
            }
            resolutionDropdown.value = currentResolutionIndex;
            resolutionDropdown.RefreshShownValue();
        }
    }

    public void LoadSettings()
    {
        // --- Загрузка FPS ---
        int savedFPS = PlayerPrefs.GetInt("TargetFPS", -1);
        if (savedFPS == -1) savedFPS = maxFPS;
        SetTargetFPS(savedFPS);
        UpdateFPSUI(savedFPS);

        // --- Загрузка режима экрана ---
        // По умолчанию FullScreenWindow (FullScreen Optimized)
        int savedScreenModeInt = PlayerPrefs.GetInt("FullScreenMode", (int)FullScreenMode.FullScreenWindow);
        currentFullScreenMode = (FullScreenMode)savedScreenModeInt;

        if (screenModeDropdown != null)
        {
            // Устанавливаем значение в Dropdown UI
            switch (currentFullScreenMode)
            {
                case FullScreenMode.ExclusiveFullScreen:
                    screenModeDropdown.value = 0;
                    break;
                case FullScreenMode.FullScreenWindow:
                    screenModeDropdown.value = 1;
                    break;
                case FullScreenMode.Windowed:
                    screenModeDropdown.value = 2;
                    break;
                default:
                    screenModeDropdown.value = 1; // По умолчанию FullScreenWindow
                    break;
            }
            screenModeDropdown.RefreshShownValue();
        }

        // Применяем режим экрана перед загрузкой разрешения,
        // чтобы InitializeResolutions работал с правильным контекстом.
        Screen.SetResolution(Screen.width, Screen.height, currentFullScreenMode);
        Debug.Log("Loaded Screen Mode: " + currentFullScreenMode);

        // --- Инициализация и загрузка разрешения ---
        InitializeResolutions(); // Это обновит список resolutionOptions на основе текущих возможностей
                                 // и уже установленного currentFullScreenMode.

        int savedWidth = PlayerPrefs.GetInt("ResolutionWidth", Screen.currentResolution.width);
        int savedHeight = PlayerPrefs.GetInt("ResolutionHeight", Screen.currentResolution.height);

        // Находим индекс сохраненного разрешения в обновленном списке resolutionOptions
        currentResolutionIndex = 0;
        string savedResString = savedWidth + " x " + savedHeight;
        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            if (resolutionOptions[i] == savedResString)
            {
                currentResolutionIndex = i;
                break;
            }
        }

        // Применяем разрешение (SetResolution использует currentFullScreenMode)
        SetResolution(currentResolutionIndex);

        if (resolutionDropdown != null)
        {
            resolutionDropdown.value = currentResolutionIndex;
            resolutionDropdown.RefreshShownValue();
        }
    }

    private void OnEscapePressed()
    {
        Debug.Log("Escape pressed");
        if (settingsCanvas != null && settingsCanvas.activeSelf)
        {
            CloseSettings();
        }
    }

    public void CloseSettings()
    {
        if (settingsCanvas != null)
        {
            settingsCanvas.SetActive(false);
        }
        Debug.Log("Settings closed.");
    }

    void OnEnable()
    {
        playerInputActions.Enable();
    }

    void OnDisable()
    {
        playerInputActions.Disable();
    }
    public void QuitToMenu()
    {
        Application.Quit();
    }
}