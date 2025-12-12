using UnityEngine;

// a 2D grid of direction vectors that particles will follow
// can be seen (as bunch of arrows)using gizmos

public class FlowField : MonoBehaviour {
    [Header("Field Settings")]
    [Tooltip("Number of cells in each dimension (e.g., 64 means 64x64 grid)")]
    public int resolution = 64;
    
    [Tooltip("Scale of the Perlin noise. Smaller = smoother, larger = more chaotic")]
    [Range(0.01f, 0.5f)]
    public float noiseScale = 0.1f;

    [Tooltip("Use curl noise (divergence-free) instead of angle-from-Perlin")]
    public bool useCurlNoise = false;
    
    [Header("Debug Visualization")]
    [Tooltip("Show flow field arrows in Scene view")]
    public bool showGizmos = true;
    
    [Tooltip("Only draw every Nth arrow (1 = all, 2 = every other, etc.)")]
    [Range(1, 8)]
    public int gizmoSkip = 2;
    
    [Tooltip("Length of gizmo arrows")]
    [Range(0.1f, 1f)]
    public float gizmoArrowLength = 0.3f;

    // the actual field data => 2D array of normalized direction vectors
    private Vector2[,] field;
    
    // world-space bounds of the field
    private Vector2 fieldOrigin; // bottom-left corner in world space
    private Vector2 fieldSize; // width and height in world units
    private float cellSizeX; // width of each cell in world units
    private float cellSizeY; // height of each cell in world units
    
    // random offset for noise (so each run looks different)
    private float noiseOffsetX;
    private float noiseOffsetY;

    void Start() {
        InitializeField();
    }

    // sets up the field dimensions and fills it with Perlin noise
    public void InitializeField() {
        // create the array
        field = new Vector2[resolution, resolution];
        
        // random offset so each session looks different
        noiseOffsetX = Random.Range(0f, 1000f);
        noiseOffsetY = Random.Range(0f, 1000f);
        
        // calculate world-space bounds based on camera
        CalculateBounds();
        
        // fill with selected field type
        if (useCurlNoise) {
            GenerateCurlField();
        } else {
            GeneratePerlinField();
        }
        Debug.Log($"FlowField initialized: {resolution}x{resolution} grid, " +
                  $"covering {fieldSize.x:F1}x{fieldSize.y:F1} world units");
    }

    // calculates the area the field should cover based on camera's orthographic size
    private void CalculateBounds() {
        Camera cam = Camera.main;
        if (cam == null) {
            Debug.LogError("No main camera found!");
            return;
        }
        
        // orthographic size is half the vertical height
        float height = cam.orthographicSize * 2f;
        float width = height * cam.aspect;
        
        fieldSize = new Vector2(width, height);
        
        // origin is bottom-left corner, accounting for camera position
        Vector2 camPos = cam.transform.position;
        fieldOrigin = new Vector2(camPos.x - width / 2f, camPos.y - height / 2f);
        
        // ehow much world space ach cell covers (different for x and y because of aspect ratio?)
        cellSizeX = width / resolution;
        cellSizeY = height / resolution;
    }

    // fills the field with directions based on Perlin noise
    // each cell gets an angle from 0 to 2π, converted to a direction vector
    private void GeneratePerlinField() {
        for (int x = 0; x < resolution; x++) {
            for (int y = 0; y < resolution; y++) {
                // Sample Perlin noise at this cell
                float noiseValue = Mathf.PerlinNoise(
                    x * noiseScale + noiseOffsetX,
                    y * noiseScale + noiseOffsetY
                );
                
                // convert 0-1 noise to 0-2π angle
                float angle = noiseValue * Mathf.PI * 2f;
                
                // convert angle to direction vector
                field[x, y] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
        }
    }

    // fills the field with curl noise (divergence-free)
    // basically results in smooth swirling flow like pattern
    private void GenerateCurlField() {
        float eps = 0.01f;
        for (int x = 0; x < resolution; x++) {
            for (int y = 0; y < resolution; y++) {
                // sample noise around the point
                float nx = x * noiseScale + noiseOffsetX;
                float ny = y * noiseScale + noiseOffsetY;

                float n1 = Mathf.PerlinNoise(nx + eps, ny);
                float n2 = Mathf.PerlinNoise(nx - eps, ny);
                float n3 = Mathf.PerlinNoise(nx, ny + eps);
                float n4 = Mathf.PerlinNoise(nx, ny - eps);

                // gradient
                float dx = (n1 - n2) / (2f * eps);
                float dy = (n3 - n4) / (2f * eps);

                // curl in 2D => perpendicular to gradient
                Vector2 curl = new Vector2(dy, -dx).normalized;
                field[x, y] = curl;
            }
        }
    }

    // regenerates the field with new random noise
    // call this when the user hits "Reset Field" btn
    public void ResetField() {
        noiseOffsetX = Random.Range(0f, 1000f);
        noiseOffsetY = Random.Range(0f, 1000f);
        if (useCurlNoise) {
            GenerateCurlField();
        } else {
            GeneratePerlinField();
        }
        Debug.Log("FlowField reset with new noise");
    }

    // returns flow direction at that point given world position
    // uses nearest-neighbor sampling ( to keep it simple/fast)
    public Vector2 Sample(Vector2 worldPos) {
        // convert world position to normalized coordinates
        float nx = Mathf.Clamp01((worldPos.x - fieldOrigin.x) / fieldSize.x);
        float ny = Mathf.Clamp01((worldPos.y - fieldOrigin.y) / fieldSize.y);

        // convert to continuous cell coordinates
        float gx = nx * (resolution - 1);
        float gy = ny * (resolution - 1);

        int x0 = Mathf.FloorToInt(gx);
        int y0 = Mathf.FloorToInt(gy);
        int x1 = Mathf.Min(x0 + 1, resolution - 1);
        int y1 = Mathf.Min(y0 + 1, resolution - 1);

        float tx = gx - x0;
        float ty = gy - y0;

        // bilinear interpolation for smoother sampling
        Vector2 v00 = field[x0, y0];
        Vector2 v10 = field[x1, y0];
        Vector2 v01 = field[x0, y1];
        Vector2 v11 = field[x1, y1];

        Vector2 vx0 = Vector2.Lerp(v00, v10, tx);
        Vector2 vx1 = Vector2.Lerp(v01, v11, tx);
        return Vector2.Lerp(vx0, vx1, ty).normalized;
    }

    // modifies the field in a radius around a center point, used by brushes to paint on the field
    // worldCenter => center of the brush in world coordinates
    // radius => brush radius in world units
    // newDirection => direction to blend toward
    // strength => how much to blend (0-1)
    public void ApplyBrush(Vector2 worldCenter, float radius, Vector2 newDirection, float strength) {
        // convert world radius to cell radius (use average of x and y cell sizes)
        float avgCellSize = (cellSizeX + cellSizeY) / 2f;
        int cellRadius = Mathf.CeilToInt(radius / avgCellSize);
        
        // find the center cell
        float normalizedX = (worldCenter.x - fieldOrigin.x) / fieldSize.x;
        float normalizedY = (worldCenter.y - fieldOrigin.y) / fieldSize.y;
        int centerCellX = Mathf.FloorToInt(normalizedX * resolution);
        int centerCellY = Mathf.FloorToInt(normalizedY * resolution);
        
        // iterate over cells in the brush area
        for (int dx = -cellRadius; dx <= cellRadius; dx++) {
            for (int dy = -cellRadius; dy <= cellRadius; dy++) {
                int cellX = centerCellX + dx;
                int cellY = centerCellY + dy;
                
                // skip if outside grid
                if (cellX < 0 || cellX >= resolution || cellY < 0 || cellY >= resolution)
                    continue;
                
                // calculate distance from center in cells
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                
                // skip if outside radius
                if (distance > cellRadius)
                    continue;
                
                // falloff => stronger at center & weaker at edges
                float falloff = 1f - (distance / cellRadius);
                float finalStrength = strength * falloff;
                
                // blend curr direction toward new direction
                Vector2 current = field[cellX, cellY];
                Vector2 blended = Vector2.Lerp(current, newDirection.normalized, finalStrength);
                field[cellX, cellY] = blended.normalized;
            }
        }
    }

    // returns the world-space bounds of the field => useful for particle wrapping
    public (Vector2 min, Vector2 max) GetBounds() {
        Vector2 min = fieldOrigin;
        Vector2 max = fieldOrigin + fieldSize;
        return (min, max);
    }

    // swirl brush => sets vectors to rotate around the center point
    // basicallycreates vortex like effect
    public void ApplySwirlBrush(Vector2 worldCenter, float radius, float strength) {
        float avgCellSize = (cellSizeX + cellSizeY) / 2f;
        int cellRadius = Mathf.CeilToInt(radius / avgCellSize);
        
        float normalizedX = (worldCenter.x - fieldOrigin.x) / fieldSize.x;
        float normalizedY = (worldCenter.y - fieldOrigin.y) / fieldSize.y;
        int centerCellX = Mathf.FloorToInt(normalizedX * resolution);
        int centerCellY = Mathf.FloorToInt(normalizedY * resolution);
        
        for (int dx = -cellRadius; dx <= cellRadius; dx++) {
            for (int dy = -cellRadius; dy <= cellRadius; dy++) {
                int cellX = centerCellX + dx;
                int cellY = centerCellY + dy;
                
                if (cellX < 0 || cellX >= resolution || cellY < 0 || cellY >= resolution)
                    continue;
                
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance > cellRadius || distance < 0.1f)
                    continue;
                
                float falloff = 1f - (distance / cellRadius);
                float finalStrength = strength * falloff;
                
                // calculate perpendicular direction (tangent to circle around center)
                Vector2 toCenter = new Vector2(dx, dy);
                Vector2 perpendicular = new Vector2(-toCenter.y, toCenter.x).normalized;
                
                Vector2 current = field[cellX, cellY];
                Vector2 blended = Vector2.Lerp(current, perpendicular, finalStrength);
                field[cellX, cellY] = blended.normalized;
            }
        }
    }

    // attract/repel brush (same idea but positive and negative) => sets vectors to point toward or away from center
    public void ApplyAttractBrush(Vector2 worldCenter, float radius, float strength, bool repel) {
        float avgCellSize = (cellSizeX + cellSizeY) / 2f;
        int cellRadius = Mathf.CeilToInt(radius / avgCellSize);
        
        float normalizedX = (worldCenter.x - fieldOrigin.x) / fieldSize.x;
        float normalizedY = (worldCenter.y - fieldOrigin.y) / fieldSize.y;
        int centerCellX = Mathf.FloorToInt(normalizedX * resolution);
        int centerCellY = Mathf.FloorToInt(normalizedY * resolution);
        
        for (int dx = -cellRadius; dx <= cellRadius; dx++) {
            for (int dy = -cellRadius; dy <= cellRadius; dy++) {
                int cellX = centerCellX + dx;
                int cellY = centerCellY + dy;
                
                if (cellX < 0 || cellX >= resolution || cellY < 0 || cellY >= resolution)
                    continue;
                
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance > cellRadius || distance < 0.1f)
                    continue;
                
                float falloff = 1f - (distance / cellRadius);
                float finalStrength = strength * falloff;
                
                // direction toward center (or away if repel)
                Vector2 toCenter = new Vector2(-dx, -dy).normalized;
                if (repel)
                    toCenter = -toCenter;
                
                Vector2 current = field[cellX, cellY];
                Vector2 blended = Vector2.Lerp(current, toCenter, finalStrength);
                field[cellX, cellY] = blended.normalized;
            }
        }
    }

    // draw the flow field as arrows in the scene view
    // only visible when the game object is selected or gizmos are enabled
    void OnDrawGizmos() {
        // don't draw if disabled or if no field
        if (!showGizmos || field == null)
            return;
        
        Gizmos.color = Color.cyan;
        float avgCellSize = (cellSizeX + cellSizeY) / 2f;
        
        for (int x = 0; x < resolution; x += gizmoSkip) {
            for (int y = 0; y < resolution; y += gizmoSkip) {
                // calculate world position of the center of this cell
                // use the right cell sizes for x and y to handle non-square aspect ratios
                float worldX = fieldOrigin.x + (x + 0.5f) * cellSizeX;
                float worldY = fieldOrigin.y + (y + 0.5f) * cellSizeY;
                Vector3 cellCenter = new Vector3(worldX, worldY, 0);
                
                // get the direction at this cell
                Vector2 direction = field[x, y];
                Vector3 dir3D = new Vector3(direction.x, direction.y, 0);
                
                // draw line from center in the direction
                Vector3 end = cellCenter + dir3D * gizmoArrowLength * avgCellSize * gizmoSkip;
                Gizmos.DrawLine(cellCenter, end);
                
                // draw small arrowhead
                Vector3 arrowHead = end;
                Vector3 perpendicular = Vector3.Cross(dir3D, Vector3.forward).normalized;
                float arrowSize = gizmoArrowLength * avgCellSize * gizmoSkip * 0.3f;
                Gizmos.DrawLine(arrowHead, arrowHead - dir3D * arrowSize + perpendicular * arrowSize * 0.5f);
                Gizmos.DrawLine(arrowHead, arrowHead - dir3D * arrowSize - perpendicular * arrowSize * 0.5f);
            }
        }
    }
}

