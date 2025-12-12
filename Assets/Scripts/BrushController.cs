using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// handles mouse input for "painting" on the flow field

public class BrushController : MonoBehaviour {
    [Header("References")]
    [Tooltip("The flow field to paint on")]
    public FlowField flowField;

    [Header("Brush Settings")]
    [Tooltip("Current brush mode")]
    public BrushMode brushMode = BrushMode.Flow;
    
    [Tooltip("Radius of brush effect in world units")]
    [Range(0.2f, 5f)]
    public float brushSize = 0.8f;
    
    [Tooltip("How strongly the brush affects the field (0-1)")]
    [Range(0.01f, 1f)]
    public float brushStrength = 0.3f;

    [Header("Debug")]
    [Tooltip("Show brush radius in Scene view")]
    public bool showBrushGizmo = true;

    public enum BrushMode {
        Flow, // vectors point in drag direction
        Swirl, // vectors rotate around cursor
        Attract, // vectors point toward cursor
        Repel // vectors point away from cursor
    }

    // mouse tracking
    private Vector2 lastMouseWorldPos;
    private Vector2 currentMouseWorldPos;
    private bool isDragging = false;
    private Camera mainCamera;

    void Start() {
        mainCamera = Camera.main;
        
        if (flowField == null) {
            Debug.LogError("BrushController: No FlowField assigned!");
        }
    }

    void Update() {
        if (flowField == null || mainCamera == null)
            return;

        // get mouse
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        // get curr mouse position in world space
        // since it's orthographic 2D, just need x and y
        Vector3 mouseScreenPos = mouse.position.ReadValue();
        mouseScreenPos.z = Mathf.Abs(mainCamera.transform.position.z);
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        currentMouseWorldPos = new Vector2(worldPos.x, worldPos.y);

        // check if mouse is over UI, if yes don't paint
        bool isOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // handle mouse input
        bool leftButtonPressed = mouse.leftButton.isPressed;
        bool leftButtonDown = mouse.leftButton.wasPressedThisFrame;
        bool leftButtonUp = mouse.leftButton.wasReleasedThisFrame;

        if (leftButtonDown && !isOverUI) {
            // started dragging (only if not over UI)
            isDragging = true;
            lastMouseWorldPos = currentMouseWorldPos;
        }
        else if (leftButtonPressed && isDragging && !isOverUI) {
            // continue dragging, apply brush
            ApplyBrush();
            lastMouseWorldPos = currentMouseWorldPos;
        }
        else if (leftButtonUp || isOverUI) {
            // stopped dragging (or moved over UI)
            isDragging = false;
        }
    }

    // applies the current brush to the flow field at the mouse position
    void ApplyBrush() {
        // calculate drag direction (for flow brush)
        Vector2 dragDelta = currentMouseWorldPos - lastMouseWorldPos;
        
        // skip if not moving (prevents issues with zero-length vectors)
        if (dragDelta.magnitude < 0.001f && brushMode == BrushMode.Flow)
            return;

        // apply based on brush mode
        switch (brushMode) {
            case BrushMode.Flow:
                ApplyFlowBrush(dragDelta.normalized);
                break;
            case BrushMode.Swirl:
                ApplySwirlBrush();
                break;
            case BrushMode.Attract:
                ApplyAttractBrush();
                break;
            case BrushMode.Repel:
                ApplyRepelBrush();
                break;
        }
    }

    // flow brush => sets vectors to point in the drag direction
    void ApplyFlowBrush(Vector2 direction) {
        flowField.ApplyBrush(currentMouseWorldPos, brushSize, direction, brushStrength);
    }

    /// swirl brush => sets vectors to rotate around the cursor, basically vortex effect
    void ApplySwirlBrush() {
        flowField.ApplySwirlBrush(currentMouseWorldPos, brushSize, brushStrength);
    }

    // attract brush => sets vectors to point toward the cursor
    void ApplyAttractBrush() {
        flowField.ApplyAttractBrush(currentMouseWorldPos, brushSize, brushStrength, false);
    }

    // repel brush => sets vectors to point away from the cursor
    void ApplyRepelBrush() {
        flowField.ApplyAttractBrush(currentMouseWorldPos, brushSize, brushStrength, true);
    }

    // draw brush preview in scene view
    void OnDrawGizmos() {
        if (!showBrushGizmo)
            return;

        // draw brush radius at mouse position
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f); // orange, sorta transparent
        
        if (Application.isPlaying && mainCamera != null) {
            Gizmos.DrawWireSphere(new Vector3(currentMouseWorldPos.x, currentMouseWorldPos.y, 0), brushSize);
        }
    }

    // public methods for UI

    public void SetBrushMode(int mode) {
        brushMode = (BrushMode)mode;
        Debug.Log($"Brush mode: {brushMode}");
    }

    public void SetBrushMode(BrushMode mode) {
        brushMode = mode;
        Debug.Log($"Brush mode: {brushMode}");
    }
}

