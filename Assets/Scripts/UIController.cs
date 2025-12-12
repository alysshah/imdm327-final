using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.IO;
using TMPro;

// connects UI elements to the game systems
// also handles all button clicks and slider changes

public class UIController : MonoBehaviour {
    [Header("System References")]
    public FlowField flowField;
    public ParticleManager particleManager;
    public BrushController brushController;
    public SoundReactor soundReactor;

    [Header("UI Elements - Panel")]
    public GameObject panel; // the entire panel (hide this when collapsed)
    public Button btnTogglePanel; // button to collapse/expand (should be OUTSIDE panel)
    public TMP_Text toggleButtonText; // text on the toggle button

    [Header("UI Elements - Buttons")]
    public Button btnFlow;
    public Button btnSwirl;
    public Button btnAttract;
    public Button btnRepel;
    public Button btnReset;

    [Header("UI Elements - Field Type")]
    public TMP_Dropdown dropdownFieldType; // 0 = Perlin, 1 = Curl

    [Header("Capture / Frame")]
    [Tooltip("Optional overlay shown during capture to add a black frame")]
    public GameObject frameOverlay;
    [Tooltip("Screenshot scale multiplier (1 = native res)")]
    public int screenshotSuperSize = 1;
    [Tooltip("Key to trigger PNG capture")]
    public Key captureKey = Key.P;
    [Tooltip("Key to toggle frame overlay on/off")]
    public Key overlayToggleKey = Key.O;
    [Tooltip("If set, saves screenshots to this absolute folder (e.g., /Users/user/Downloads)")]
    public string screenshotFolder = "";
    [Tooltip("Key to toggle flow gizmos on/off")]
    public Key gizmoToggleKey = Key.G;

    [Header("UI Elements - Sliders")]
    public Slider sliderBrushSize;
    public Slider sliderSpeed;
    public Slider sliderTurbulence;

    [Header("UI Elements - Toggle")]
    public Toggle toggleSound;

    [Header("UI Elements - Labels")]
    public TMP_Text titleText;
    public TMP_Text labelBrushSize; 
    public TMP_Text labelSpeed;  
    public TMP_Text labelTurbulence;
    public TMP_Text labelVolume; // shows as percentage like "Vol: XX%"

    // track which brush is selected for visual feedback
    private Button currentBrushButton;
    private bool isPanelOpen = true;
    private bool soundDebugWasOn = false;

    void Start() {
        // make sure SoundReactor debug UI is enabled
        if (soundReactor != null) {
            soundReactor.showDebugUI = true;
        }

        // set up panel toggle button
        if (btnTogglePanel != null) {
            btnTogglePanel.onClick.AddListener(TogglePanel);
        }

        // set up button listeners
        if (btnFlow != null) btnFlow.onClick.AddListener(() => SetBrushMode(BrushController.BrushMode.Flow, btnFlow));
        if (btnSwirl != null) btnSwirl.onClick.AddListener(() => SetBrushMode(BrushController.BrushMode.Swirl, btnSwirl));
        if (btnAttract != null) btnAttract.onClick.AddListener(() => SetBrushMode(BrushController.BrushMode.Attract, btnAttract));
        if (btnRepel != null) btnRepel.onClick.AddListener(() => SetBrushMode(BrushController.BrushMode.Repel, btnRepel));
        
        if (btnReset != null) btnReset.onClick.AddListener(OnResetField);

        // set up dropdown for field type
        if (dropdownFieldType != null && flowField != null) {
            dropdownFieldType.ClearOptions();
            dropdownFieldType.AddOptions(new System.Collections.Generic.List<string> { "Perlin", "Curl" });
            dropdownFieldType.value = flowField.useCurlNoise ? 1 : 0;
            dropdownFieldType.onValueChanged.AddListener(OnFieldTypeChanged);
        }

        // Set up slider listeners
        if (sliderBrushSize != null) {
            sliderBrushSize.minValue = 0.2f;
            sliderBrushSize.maxValue = 5f;
            sliderBrushSize.value = brushController != null ? brushController.brushSize : 0.8f;
            sliderBrushSize.onValueChanged.AddListener(OnBrushSizeChanged);
        }

        if (sliderSpeed != null) {
            sliderSpeed.minValue = 0.5f;
            sliderSpeed.maxValue = 10f;
            sliderSpeed.value = particleManager != null ? particleManager.flowStrength : 2f;
            sliderSpeed.onValueChanged.AddListener(OnSpeedChanged);
        }

        if (sliderTurbulence != null) {
            sliderTurbulence.minValue = 0f;
            sliderTurbulence.maxValue = 2f;
            sliderTurbulence.value = particleManager != null ? particleManager.turbulence : 0.1f;
            sliderTurbulence.onValueChanged.AddListener(OnTurbulenceChanged);
        }

        // set up toggle
        if (toggleSound != null) {
            toggleSound.isOn = soundReactor != null ? soundReactor.enableMic : false;
            toggleSound.onValueChanged.AddListener(OnSoundToggled);
        }

        // set initial title
        if (titleText != null) {
            titleText.text = "Flow Field Painter";
        }

        // highlight initial brush mode
        if (btnFlow != null) {
            currentBrushButton = btnFlow;
            HighlightButton(btnFlow);
        }

        // update labels
        UpdateLabels();
        UpdateToggleButtonText();
    }

    void Update() {
        // keep toggle in sync if sound is toggled elsewhere
        if (toggleSound != null && soundReactor != null) {
            if (toggleSound.isOn != soundReactor.enableMic) {
                toggleSound.isOn = soundReactor.enableMic;
            }
        }
        
        // update volume display
        if (soundReactor != null && labelVolume != null) {
            float vol = soundReactor.GetVolume();
            labelVolume.text = $"Vol: {(vol * 100):F0}%";
            labelVolume.color = Color.Lerp(Color.white, Color.green, vol);
        }

        // capture screenshot on keypress!!
        var keyboard = Keyboard.current;
        if (keyboard != null) {
            if (keyboard[captureKey].wasPressedThisFrame) {
                StartCoroutine(CaptureScreenshotRoutine());
            }

            if (keyboard[overlayToggleKey].wasPressedThisFrame && frameOverlay != null) {
                frameOverlay.SetActive(!frameOverlay.activeSelf);
            }

            if (keyboard[gizmoToggleKey].wasPressedThisFrame && flowField != null) {
                flowField.showGizmos = !flowField.showGizmos;
                if (brushController != null) {
                    brushController.showBrushGizmo = flowField.showGizmos;
                }
            }
        }

        // if overlay is active, make sure debug volume bar is hidden
        // also restore it when inactive
        if (soundReactor != null && frameOverlay != null) {
            bool overlayActive = frameOverlay.activeSelf;
            if (overlayActive && soundReactor.showDebugUI) {
                soundDebugWasOn = true;
                soundReactor.showDebugUI = false;
            }
            else if (!overlayActive && soundDebugWasOn) {
                soundReactor.showDebugUI = true;
                soundDebugWasOn = false;
            }
        }
    }

    void OnFieldTypeChanged(int value) {
        if (flowField != null) {
            flowField.useCurlNoise = (value == 1);
            flowField.ResetField();
        }
    }

    void TogglePanel() {
        isPanelOpen = !isPanelOpen;
        
        // hide/show the entire panel (including background)
        if (panel != null) {
            panel.SetActive(isPanelOpen);
        }
        
        // toggle the SoundReactor debug UI with the panel
        if (soundReactor != null) {
            soundReactor.showDebugUI = isPanelOpen;
        }
        
        UpdateToggleButtonText();
    }

    void UpdateToggleButtonText() {
        if (toggleButtonText != null) {
            // just use simple ASCII characters for the toggle button
            toggleButtonText.text = isPanelOpen ? "X" : "=";
        }
    }

    void SetBrushMode(BrushController.BrushMode mode, Button button) {
        if (brushController != null) {
            brushController.brushMode = mode;
        }

        // update visual feedback
        if (currentBrushButton != null) {
            ResetButtonColor(currentBrushButton);
        }
        currentBrushButton = button;
        HighlightButton(button);
    }

    void HighlightButton(Button btn) {
        if (btn == null) return;
        ColorBlock colors = btn.colors;
        colors.normalColor = new Color(0.3f, 0.6f, 0.9f, 1f); // blue highlight
        btn.colors = colors;
    }

    void ResetButtonColor(Button btn) {
        if (btn == null) return;
        ColorBlock colors = btn.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 1f); // default white
        btn.colors = colors;
    }

    void OnBrushSizeChanged(float value) {
        if (brushController != null) {
            brushController.brushSize = value;
        }
        UpdateLabels();
    }

    void OnSpeedChanged(float value) {
        if (particleManager != null) {
            particleManager.flowStrength = value;
        }
        UpdateLabels();
    }

    void OnTurbulenceChanged(float value) {
        if (particleManager != null) {
            particleManager.turbulence = value;
        }
        UpdateLabels();
    }

    void OnSoundToggled(bool isOn) {
        if (soundReactor != null) {
            soundReactor.SetEnabled(isOn);
        }
    }

    void OnResetField() {
        if (flowField != null) {
            flowField.ResetField();
        }
    }

    void UpdateLabels()
    {
        // update slider labels (but make sure they exist first)
        if (labelBrushSize != null && sliderBrushSize != null) {
            labelBrushSize.text = $"Brush Size: {sliderBrushSize.value:F1}";
        }
        if (labelSpeed != null && sliderSpeed != null) {
            labelSpeed.text = $"Flow Speed: {sliderSpeed.value:F1}";
        }
        if (labelTurbulence != null && sliderTurbulence != null) {
            labelTurbulence.text = $"Turbulence: {sliderTurbulence.value:F2}";
        }
    }

    private System.Collections.IEnumerator CaptureScreenshotRoutine() {
        // show frame overlay if provided
        bool frameWasActive = frameOverlay != null && frameOverlay.activeSelf;
        if (frameOverlay != null) frameOverlay.SetActive(true);

        // hide debug UI while capturing
        bool soundDebugWasActive = soundReactor != null && soundReactor.showDebugUI;
        if (soundReactor != null) soundReactor.showDebugUI = false;

        // wait a frame so visuals update
        yield return null;

        // build filename and path
        string filename = $"FlowField_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        string baseFolder = string.IsNullOrEmpty(screenshotFolder)
            ? Application.persistentDataPath
            : screenshotFolder;
        if (!Directory.Exists(baseFolder))
            Directory.CreateDirectory(baseFolder);
        string path = Path.Combine(baseFolder, filename);

        ScreenCapture.CaptureScreenshot(path, Mathf.Max(1, screenshotSuperSize));
        Debug.Log($"Saved screenshot: {path}");

        // wait another frame to ensure capture request queued (optional)
        yield return null;

        // restore overlay
        if (frameOverlay != null) frameOverlay.SetActive(frameWasActive);
        // restore debug UI
        if (soundReactor != null) soundReactor.showDebugUI = soundDebugWasActive;
    }
}

