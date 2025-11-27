using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace UI.Menu
{
    public class SettingsManager : MonoBehaviour
{
    [Header("FPS Settings")]
    public Slider fpsSlider;
    public TMP_Text fpsText;
    public TMP_InputField fpsInputField;
    public int minFPS = 60;
    public int maxFPS = 240; 

    [Header("Resolution Settings")]
    public TMP_Dropdown resolutionDropdown;
    private Resolution[] _allResolutions;
    private List<string> _resolutionOptions;
    private int _currentResolutionIndex;

    [Header("Screen Mode Settings")]
    public TMP_Dropdown screenModeDropdown;
    private FullScreenMode _currentFullScreenMode;

    [Header("UI References")]
    public GameObject settingsCanvas;
    private PlayerInputActions _playerInputActions;

    void Awake()
    {
        _playerInputActions = new PlayerInputActions();
        _playerInputActions.UI.Escape.performed += _ => OnEscapePressed();
    }

    private void Start()
    {
        if (fpsSlider != null)
        {
            fpsSlider.minValue = minFPS;
            fpsSlider.maxValue = maxFPS;
            fpsSlider.onValueChanged.AddListener(SetFPSFromSlider);
            fpsSlider.value = Application.targetFrameRate == -1 ? maxFPS : Application.targetFrameRate;
        }
        if (fpsInputField != null)
        {
            fpsInputField.onEndEdit.AddListener(SetFPSFromInputField);
        }
        UpdateFpsUi(Application.targetFrameRate == -1 ? maxFPS : Application.targetFrameRate);
        
        if (screenModeDropdown != null)
        {
            screenModeDropdown.ClearOptions();
            List<string> modeOptions = new List<string>
            {
                "Полноэкранный",      
                "Полноэкранный в окне",
                "Оконный" 
            };
            screenModeDropdown.AddOptions(modeOptions);
            screenModeDropdown.onValueChanged.AddListener(SetScreenMode);
        }
        LoadSettings();
        
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
        _resolutionOptions.Clear();
        _allResolutions = Screen.resolutions;

        HashSet<string> uniqueResolutionStrings = new HashSet<string>();

        foreach (var res in _allResolutions)
        {
            // Для оконных режимов, можем показывать только разрешения, которые "подходят"
            // к текущему монитору, но для простоты пока показываем все уникальные по размеру.
            // Unity сама будет применять их с учетом ограничений режима.
            string option = res.width + " x " + res.height;

            if (!uniqueResolutionStrings.Contains(option))
            {
                _resolutionOptions.Add(option);
                uniqueResolutionStrings.Add(option);
            }
        }
        
        _resolutionOptions.Sort((res1, res2) =>
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
        
        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(_resolutionOptions);
            // Устанавливаем значение Dropdown на текущее разрешение или ближайшее к нему
            UpdateResolutionDropdownValue();
        }
        Debug.Log("Resolutions Initialized for mode: " + _currentFullScreenMode + ". Count: " + _resolutionOptions.Count);
    }

    public void SetFPSFromSlider(float fpsValue)
    {
        int fps = Mathf.RoundToInt(fpsValue);
        SetTargetFPS(fps);
        UpdateFpsUi(fps);
    }

    public void SetFPSFromInputField(string fpsString)
    {
        int fps;
        if (int.TryParse(fpsString, out fps)) 
        {
            fps = Mathf.Clamp(fps, minFPS, maxFPS);
            SetTargetFPS(fps);
            UpdateFpsUi(fps);
        }
        else
        {
            UpdateFpsUi(Application.targetFrameRate == -1 ? maxFPS : Application.targetFrameRate);
        }
    }

    private void UpdateFpsUi(int fps)
    {
        if (fpsSlider != null) fpsSlider.value = fps;
        if (fpsText != null) fpsText.text = "FPS: " + fps;
        if (fpsInputField != null) fpsInputField.text = fps.ToString();
    }

    private void SetTargetFPS(int fps)
    {
        Application.targetFrameRate = fps;
        PlayerPrefs.SetInt("TargetFPS", fps); 
        PlayerPrefs.Save();
    }

    public void SetResolution(int resolutionIndex)
    {
        if (resolutionIndex >= 0 && resolutionIndex < _resolutionOptions.Count)
        {
            string selectedOption = _resolutionOptions[resolutionIndex];
            string[] parts = selectedOption.Split('x');
            int width = int.Parse(parts[0].Trim());
            int height = int.Parse(parts[1].Trim());

            Resolution bestResolution = new Resolution
            {
                width = width, height = height, refreshRateRatio = new RefreshRate { numerator = 0, denominator = 1 }
            };
            
            int maxRefreshRate = 0;
            foreach (var res in _allResolutions)
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

            Screen.SetResolution(bestResolution.width, bestResolution.height, _currentFullScreenMode, bestResolution.refreshRateRatio);
            PlayerPrefs.SetInt("ResolutionWidth", bestResolution.width);
            PlayerPrefs.SetInt("ResolutionHeight", bestResolution.height);
            PlayerPrefs.Save();

            _currentResolutionIndex = resolutionIndex;
            Debug.Log($"Resolution set to: {bestResolution.width}x{bestResolution.height} @ {bestResolution.refreshRateRatio.value}Hz, Mode: {_currentFullScreenMode}");
        }
    }

    public void SetScreenMode(int modeIndex)
    {
        FullScreenMode newMode;
        switch (modeIndex)
        {
            case 0: 
                newMode = FullScreenMode.ExclusiveFullScreen;
                break;
            case 1: 
                newMode = FullScreenMode.FullScreenWindow;
                break;
            case 2:
                newMode = FullScreenMode.Windowed;
                break;
            default:
                newMode = FullScreenMode.FullScreenWindow;
                break;
        }

        if (_currentFullScreenMode != newMode)
        {
            _currentFullScreenMode = newMode;
            Screen.SetResolution(Screen.width, Screen.height, _currentFullScreenMode);
            PlayerPrefs.SetInt("FullScreenMode", (int)_currentFullScreenMode);
            PlayerPrefs.Save();

            // Переинициализируем разрешения, так как доступные разрешения могут измениться
            // в зависимости от нового режима экрана (хотя Unity может сама адаптировать)
            InitializeResolutions();
            UpdateResolutionDropdownValue();
            Debug.Log("Screen Mode set to: " + _currentFullScreenMode);
        }
    }
    
    private void UpdateResolutionDropdownValue()
    {
        if (resolutionDropdown != null)
        {
            _currentResolutionIndex = 0;
            string currentResString = Screen.width + " x " + Screen.height;
            for (int i = 0; i < _resolutionOptions.Count; i++)
            {
                if (_resolutionOptions[i] == currentResString)
                {
                    _currentResolutionIndex = i;
                    break;
                }
            }
            resolutionDropdown.value = _currentResolutionIndex;
            resolutionDropdown.RefreshShownValue();
        }
    }

    public void LoadSettings()
    {
        // --- Загрузка FPS ---
        int savedFPS = PlayerPrefs.GetInt("TargetFPS", -1);
        if (savedFPS == -1) savedFPS = maxFPS;
        SetTargetFPS(savedFPS);
        UpdateFpsUi(savedFPS);

        // --- Загрузка режима экрана ---
        // По умолчанию FullScreenWindow (FullScreen Optimized)
        int savedScreenModeInt = PlayerPrefs.GetInt("FullScreenMode", (int)FullScreenMode.FullScreenWindow);
        _currentFullScreenMode = (FullScreenMode)savedScreenModeInt;

        if (screenModeDropdown != null)
        {
            switch (_currentFullScreenMode)
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
                    screenModeDropdown.value = 1;
                    break;
            }
            screenModeDropdown.RefreshShownValue();
        }

        // Применяем режим экрана перед загрузкой разрешения,
        // чтобы InitializeResolutions работал с правильным контекстом.
        Screen.SetResolution(Screen.width, Screen.height, _currentFullScreenMode);
        Debug.Log("Loaded Screen Mode: " + _currentFullScreenMode);

        // --- Инициализация и загрузка разрешения ---
        InitializeResolutions();
        
        int savedWidth = PlayerPrefs.GetInt("ResolutionWidth", Screen.currentResolution.width);
        int savedHeight = PlayerPrefs.GetInt("ResolutionHeight", Screen.currentResolution.height);

      
        _currentResolutionIndex = 0;
        string savedResString = savedWidth + " x " + savedHeight;
        for (int i = 0; i < _resolutionOptions.Count; i++)
        {
            if (_resolutionOptions[i] == savedResString)
            {
                _currentResolutionIndex = i;
                break;
            }
        }
        
        SetResolution(_currentResolutionIndex);

        if (resolutionDropdown != null)
        {
            resolutionDropdown.value = _currentResolutionIndex;
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
        _playerInputActions.Enable();
    }

    void OnDisable()
    {
        _playerInputActions.Disable();
    }
    public void QuitToMenu()
    {
        Application.Quit();
    }
}
}
