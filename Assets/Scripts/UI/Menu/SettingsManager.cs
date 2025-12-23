using System.Collections.Generic;
using FishNet.Managing.Scened;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace UI.Menu
{
    public class SettingsManager : MonoBehaviour
    {
        [Header("FPS Settings")]
        public TMP_Dropdown fpsDropdown;
        

        private readonly int[] _fpsOptions =
        {
            30,
            60,
            90,
            120,
            144,
            240,
            -1 // Unlimited
        };

        [Header("Resolution Settings")]
        public TMP_Dropdown resolutionDropdown;
        private Resolution[] _allResolutions;
        private List<string> _resolutionOptions = new List<string>();
        private int _currentResolutionIndex;

        [Header("Screen Mode Settings")]
        public TMP_Dropdown screenModeDropdown;
        private FullScreenMode _currentFullScreenMode;

        [Header("UI References")]
        public GameObject settingsCanvas;
        private PlayerInputActions _playerInputActions;

        private void Awake()
        {
            _playerInputActions = new PlayerInputActions();
            _playerInputActions.UI.Escape.performed += _ => OnEscapePressed();
        }

        private void Start()
        {
            InitializeFpsDropdown();
            InitializeScreenModeDropdown();

            LoadSettings();

            if (resolutionDropdown != null)
                resolutionDropdown.onValueChanged.AddListener(SetResolution);
        }

        // ================= FPS =================

        private void InitializeFpsDropdown()
        {
            if (fpsDropdown == null) return;

            fpsDropdown.ClearOptions();
            List<string> options = new List<string>();

            foreach (int fps in _fpsOptions)
                options.Add(fps == -1 ? "Без лимита" : $"{fps} FPS");

            fpsDropdown.AddOptions(options);
            fpsDropdown.onValueChanged.AddListener(SetFPS);
        }

        private void SetFPS(int index)
        {
            if (index < 0 || index >= _fpsOptions.Length) return;

            int fps = _fpsOptions[index];
            Application.targetFrameRate = fps;

            PlayerPrefs.SetInt("TargetFPS", fps);
            PlayerPrefs.Save();
        }

        // ================= SCREEN MODE =================

        private void InitializeScreenModeDropdown()
        {
            if (screenModeDropdown == null) return;

            screenModeDropdown.ClearOptions();
            screenModeDropdown.AddOptions(new List<string>
            {
                "Полноэкранный",
                "Полноэкранный в окне",
                "Оконный"
            });

            screenModeDropdown.onValueChanged.AddListener(SetScreenMode);
        }

        public void SetScreenMode(int index)
        {
            FullScreenMode newMode = index switch
            {
                0 => FullScreenMode.ExclusiveFullScreen,
                1 => FullScreenMode.FullScreenWindow,
                2 => FullScreenMode.Windowed,
                _ => FullScreenMode.FullScreenWindow
            };

            if (_currentFullScreenMode == newMode) return;

            _currentFullScreenMode = newMode;
            Screen.SetResolution(Screen.width, Screen.height, _currentFullScreenMode);

            PlayerPrefs.SetInt("FullScreenMode", (int)_currentFullScreenMode);
            PlayerPrefs.Save();

            InitializeResolutions();
            UpdateResolutionDropdownValue();
        }

        // ================= RESOLUTION =================

        private void InitializeResolutions()
        {
            _resolutionOptions.Clear();
            _allResolutions = Screen.resolutions;

            HashSet<string> unique = new HashSet<string>();

            foreach (var res in _allResolutions)
            {
                string option = $"{res.width} x {res.height}";
                if (unique.Add(option))
                    _resolutionOptions.Add(option);
            }

            _resolutionOptions.Sort((a, b) =>
            {
                var pa = a.Split('x');
                var pb = b.Split('x');
                int wa = int.Parse(pa[0]);
                int wb = int.Parse(pb[0]);
                return wa != wb ? wa.CompareTo(wb) : int.Parse(pa[1]).CompareTo(int.Parse(pb[1]));
            });

            if (resolutionDropdown == null) return;

            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(_resolutionOptions);
        }

        public void SetResolution(int index)
        {
            if (index < 0 || index >= _resolutionOptions.Count) return;

            string[] parts = _resolutionOptions[index].Split('x');
            int width = int.Parse(parts[0]);
            int height = int.Parse(parts[1]);

            Resolution best = default;
            int maxHz = 0;

            foreach (var res in _allResolutions)
            {
                if (res.width == width && res.height == height &&
                    res.refreshRateRatio.value > maxHz)
                {
                    best = res;
                    maxHz = (int)res.refreshRateRatio.value;
                }
            }

            Screen.SetResolution(best.width, best.height, _currentFullScreenMode, best.refreshRateRatio);

            PlayerPrefs.SetInt("ResolutionWidth", best.width);
            PlayerPrefs.SetInt("ResolutionHeight", best.height);
            PlayerPrefs.Save();
        }

        private void UpdateResolutionDropdownValue()
        {
            if (resolutionDropdown == null) return;

            string current = $"{Screen.width} x {Screen.height}";
            _currentResolutionIndex = _resolutionOptions.IndexOf(current);
            if (_currentResolutionIndex < 0) _currentResolutionIndex = 0;

            resolutionDropdown.value = _currentResolutionIndex;
            resolutionDropdown.RefreshShownValue();
        }

        // ================= LOAD =================

        private void LoadSettings()
        {
            // FPS
            int savedFPS = PlayerPrefs.GetInt("TargetFPS", -1);
            Application.targetFrameRate = savedFPS;

            if (fpsDropdown != null)
            {
                int fpsIndex = System.Array.IndexOf(_fpsOptions, savedFPS);
                fpsDropdown.value = fpsIndex >= 0 ? fpsIndex : 0;
                fpsDropdown.RefreshShownValue();
            }

            // Screen mode
            _currentFullScreenMode = (FullScreenMode)PlayerPrefs.GetInt(
                "FullScreenMode",
                (int)FullScreenMode.FullScreenWindow
            );

            if (screenModeDropdown != null)
            {
                screenModeDropdown.value = _currentFullScreenMode switch
                {
                    FullScreenMode.ExclusiveFullScreen => 0,
                    FullScreenMode.FullScreenWindow => 1,
                    FullScreenMode.Windowed => 2,
                    _ => 1
                };
                screenModeDropdown.RefreshShownValue();
            }

            Screen.SetResolution(Screen.width, Screen.height, _currentFullScreenMode);

            InitializeResolutions();

            int w = PlayerPrefs.GetInt("ResolutionWidth", Screen.width);
            int h = PlayerPrefs.GetInt("ResolutionHeight", Screen.height);
            _currentResolutionIndex = _resolutionOptions.IndexOf($"{w} x {h}");
            if (_currentResolutionIndex < 0) _currentResolutionIndex = 0;

            SetResolution(_currentResolutionIndex);

            if (resolutionDropdown != null)
            {
                resolutionDropdown.value = _currentResolutionIndex;
                resolutionDropdown.RefreshShownValue();
            }
        }

        // ================= UI =================

        public void OnEscapePressed()
        {
            if (settingsCanvas != null)
            {
                settingsCanvas.SetActive(!settingsCanvas.activeSelf);
                
                if (settingsCanvas.activeSelf)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Confined;
                    Cursor.visible = true;
                }
            }
        }

        public void GoToMainMenu()
        {
            SceneManager.LoadScene("MainMenu");
        }

        private void OnEnable() => _playerInputActions.Enable();
        private void OnDisable() => _playerInputActions.Disable();

        public void QuitToMenu() => Application.Quit();
    }
}
