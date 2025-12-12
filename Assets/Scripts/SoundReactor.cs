using UnityEngine;

// captures microphone input and provides volume level for other systems to use
// sound volume drives turbulence, speed, or other particle parameters

public class SoundReactor : MonoBehaviour {
    [Header("Microphone Settings")]
    [Tooltip("Enable microphone input")]
    public bool enableMic = true;
    
    [Tooltip("How sensitive the volume detection is")]
    [Range(0.5f, 10f)]
    public float sensitivity = 2f;
    
    [Tooltip("How quickly volume changes (lower = smoother)")]
    [Range(0.01f, 0.5f)]
    public float smoothing = 0.1f;

    [Header("Debug")]
    [Tooltip("Show volume level in Inspector")]
    [Range(0f, 1f)]
    public float currentVolume = 0f;
    
    [Tooltip("Show volume bar in Game view")]
    public bool showDebugUI = true;
    
    [Header("Debug UI Settings")]
    [Tooltip("Position of the debug volume bar (pixels from bottom-left)")]
    public Vector2 debugUIPosition = new Vector2(10f, 10f);
    
    [Tooltip("Size of the debug volume bar")]
    public Vector2 debugUISize = new Vector2(30f, 120f);

    // microphone data
    private AudioClip micClip;
    private string micDevice;
    private bool micInitialized = false;
    
    // volume calculation
    private float[] sampleBuffer = new float[256];
    private float targetVolume = 0f;
    private bool hasLoggedSound = false;
    
    // other constants
    private const int SAMPLE_RATE = 44100;
    private const int MIC_LENGTH_SECONDS = 1;

    void Start() {
        if (enableMic) {
            InitializeMicrophone();
        }
    }

    void InitializeMicrophone() {
        // check if any microphone is available
        if (Microphone.devices.Length == 0) {
            Debug.LogWarning("SoundReactor: No microphone detected!");
            enableMic = false;
            return;
        }

        // use the default microphone (ig the first one)
        micDevice = Microphone.devices[0];
        Debug.Log($"SoundReactor: Using microphone '{micDevice}'");

        // start recording, don't wait, let Update handle checking
        micClip = Microphone.Start(micDevice, true, MIC_LENGTH_SECONDS, SAMPLE_RATE);
        
        if (micClip == null) {
            Debug.LogWarning("SoundReactor: Failed to create microphone clip! Check microphone permissions.");
            Debug.LogWarning("On macOS: System Preferences > Security & Privacy > Privacy > Microphone > Enable for Unity");
            enableMic = false;
            return;
        }

        // mark as initialized
        micInitialized = true;
        Debug.Log("SoundReactor: Microphone started, waiting for data...");
    }
    
    // retry microphone initialization
    public void RetryMicrophone() {
        if (micInitialized && Microphone.IsRecording(micDevice)) {
            Microphone.End(micDevice);
        }
        micInitialized = false;
        enableMic = true;
        InitializeMicrophone();
    }

    void Update() {
        if (!enableMic || !micInitialized || micClip == null) {
            currentVolume = Mathf.Lerp(currentVolume, 0f, smoothing);
            return;
        }

        // check if microphone is actually recording
        if (!Microphone.IsRecording(micDevice)) {
            currentVolume = Mathf.Lerp(currentVolume, 0f, smoothing);
            return;
        }

        // get current microphone position
        int micPosition = Microphone.GetPosition(micDevice);
        
        // skip if no data yet
        if (micPosition <= 0) {
            return;
        }
        
        // calculate where to read samples from
        int readPosition = micPosition - sampleBuffer.Length;
        if (readPosition < 0)
            readPosition += micClip.samples;

        // read samples from the microphone
        try {
            micClip.GetData(sampleBuffer, readPosition);
            
            // calculate RMS (root mean square) volume
            float sum = 0f;
            for (int i = 0; i < sampleBuffer.Length; i++) {
                sum += sampleBuffer[i] * sampleBuffer[i];
            }
            float rms = Mathf.Sqrt(sum / sampleBuffer.Length);
            
            // apply sensitivity and clamp
            targetVolume = Mathf.Clamp01(rms * sensitivity);
            
            // log once when sound is first detected
            if (!hasLoggedSound && targetVolume > 0.1f) {
                Debug.Log($"SoundReactor: Sound detected! Volume: {targetVolume:F2}");
                hasLoggedSound = true;
            }
        }
        catch (System.Exception e) {
            Debug.LogWarning($"SoundReactor: Error reading mic data: {e.Message}");
        }

        // smooth the volume changes
        currentVolume = Mathf.Lerp(currentVolume, targetVolume, smoothing);
    }

    void OnDestroy() {
        // stop microphone when destroyed
        if (micInitialized && Microphone.IsRecording(micDevice)) {
            Microphone.End(micDevice);
        }
    }

    // returns the current smoothed volume level (0 to 1)
    public float GetVolume() {
        return enableMic ? currentVolume : 0f;
    }

    // toggle microphone on/off at runtime
    public void SetEnabled(bool enabled) {
        if (enabled && !micInitialized) {
            enableMic = true;
            InitializeMicrophone();
        }
        else if (!enabled && micInitialized) {
            enableMic = false;
            if (Microphone.IsRecording(micDevice))
            {
                Microphone.End(micDevice);
            }
            micInitialized = false;
        }
        else {
            enableMic = enabled;
        }
    }

    // draw a simple volume meter in the Game view for debugging.
    void OnGUI() {
        if (!showDebugUI)
            return;

        // position and size
        float x = debugUIPosition.x;
        float y = debugUIPosition.y;
        float barWidth = debugUISize.x;
        float barHeight = debugUISize.y;

        // draw background panel
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.DrawTexture(new Rect(x - 5, y - 5, barWidth + 10, barHeight + 40), Texture2D.whiteTexture);
        
        // draw volume bar background
        GUI.color = new Color(0.3f, 0.3f, 0.3f, 1f);
        GUI.DrawTexture(new Rect(x, y, barWidth, barHeight), Texture2D.whiteTexture);
        
        // draw volume bar (fills from bottom)
        float fillHeight = Mathf.Max(currentVolume * barHeight, 2);
        GUI.color = Color.Lerp(Color.green, Color.red, currentVolume);
        GUI.DrawTexture(new Rect(x, y + barHeight - fillHeight, barWidth, fillHeight), Texture2D.whiteTexture);
        
        // draw status text
        GUI.color = Color.white;
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontStyle = FontStyle.Bold;
        
        string status = "OFF";
        if (enableMic && micInitialized) {
            if (Microphone.IsRecording(micDevice))
                status = "MIC ON";
            else
                status = "ERROR";
        }
        else if (enableMic) {
            status = "INIT...";
        }
        
        GUI.Label(new Rect(x - 5, y + barHeight + 5, barWidth + 10, 25), status, style);
        
        // show volume percentage
        GUI.Label(new Rect(x - 5, y + barHeight + 22, barWidth + 10, 20), $"{(currentVolume * 100):F0}%", style);
    }
}

