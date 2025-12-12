using UnityEngine;
using System.Collections.Generic;

// manages all particles: spawning, updating movement, and wrapping at screen edges
// makes particles follow the flow field directions

public class ParticleManager : MonoBehaviour {
    [Header("References")]
    [Tooltip("The flow field that particles will follow")]
    public FlowField flowField;
    
    [Tooltip("The particle prefab (must have a Trail Renderer)")]
    public GameObject particlePrefab;

    [Header("Particle Settings")]
    [Tooltip("Number of particles to spawn")]
    public int particleCount = 1200;
    
    [Tooltip("How strongly particles follow the flow field")]
    [Range(0.1f, 10f)]
    public float flowStrength = 2f;
    
    [Tooltip("How quickly particles slow down (1 = no damping, 0 = instant stop)")]
    [Range(0.9f, 1f)]
    public float damping = 0.98f;
    
    [Tooltip("Maximum particle speed")]
    [Range(1f, 20f)]
    public float maxSpeed = 8f;

    [Header("Turbulence & Spread")]
    [Tooltip("Base amount of random motion added to particles")]
    [Range(0f, 2f)]
    public float turbulence = 0.1f;
    
    [Tooltip("How much particles drift perpendicular to flow (spreads them out)")]
    [Range(0f, 1f)]
    public float spread = 0.3f;
    
    [Tooltip("Percentage of particles to respawn per second (keeps distribution fresh)")]
    [Range(0f, 50f)]
    public float respawnRate = 10f;
    
    [Header("Trail")]
    [Tooltip("How long trails last in seconds")]
    [Range(0.1f, 10f)]
    public float trailLength = 6.0f;
    
    private float lastTrailLength;

    [Header("Flow Variation")]
    [Tooltip("Small noise added to flow sampling to reduce lane convergence")]
    [Range(0f, 1f)]
    public float flowNoise = 0.15f;

    [Tooltip("Small positional jitter each frame to decorrelate paths")]
    [Range(0f, 0.2f)]
    public float positionJitter = 0.02f;

    [Header("Trail Fade Control")]
    [Tooltip("Maximum seconds to wait while trail fades before respawn/wrap completes")]
    [Range(0f, 10f)]
    public float fadeWaitCap = 6.0f;

    [Header("Sound Reactivity")]
    [Tooltip("Sound reactor component (optional)")]
    public SoundReactor soundReactor;
    
    [Tooltip("How much sound adds to turbulence (gets multiplied, so small values = big effect)")]
    [Range(0f, 10f)]
    public float soundToTurbulence = 3f;
    
    [Tooltip("How much sound adds to particle speed")]
    [Range(0f, 10f)]
    public float soundToSpeed = 2f;
    
    [Tooltip("How much sound increases spread")]
    [Range(0f, 5f)]
    public float soundToSpread = 1f;

    [Header("Debug")]
    [Tooltip("Show particle positions as dots in Scene view")]
    public bool showDebugDots = false;

    // data for each particle
    private class ParticleData {
        public Transform transform;
        public TrailRenderer trail;
        public Vector2 position;
        public Vector2 velocity;
        
        // per particle variation
        public float speedMultiplier; // varies speed per particle
        public float noiseOffsetX; // unique noise sampling offset
        public float noiseOffsetY; // same but for y
        public bool isPaused; // skip updates while trail is fading
    }

    private List<ParticleData> particles = new List<ParticleData>();
    private Vector2 boundsMin;
    private Vector2 boundsMax;

    // container for organization
    private Transform particleContainer;

    void Start() {
        // wait a frame for FlowField to initialize, then spawn particles
        Invoke(nameof(Initialize), 0.1f);
    }

    void Initialize() {
        if (flowField == null) {
            Debug.LogError("ParticleManager: No FlowField assigned!");
            return;
        }

        if (particlePrefab == null) {
            Debug.LogError("ParticleManager: No particle prefab assigned!");
            return;
        }

        // get bounds from flow field
        (boundsMin, boundsMax) = flowField.GetBounds();

        // create container to keep hierarchy clean
        particleContainer = new GameObject("Particles").transform;

        // spawn all particles
        SpawnParticles();
        
        // initialize trail length tracking
        lastTrailLength = trailLength;
        UpdateTrailLength();

        Debug.Log($"ParticleManager: Spawned {particleCount} particles");
    }

    void SpawnParticles() {
        for (int i = 0; i < particleCount; i++) {
            // random starting position within bounds
            Vector2 startPos = new Vector2(
                Random.Range(boundsMin.x, boundsMax.x),
                Random.Range(boundsMin.y, boundsMax.y)
            );

            // instantiate the prefab
            GameObject obj = Instantiate(particlePrefab, startPos, Quaternion.identity, particleContainer);
            obj.name = $"Particle_{i}";

            // get trail renderer
            TrailRenderer trail = obj.GetComponent<TrailRenderer>();

            // create particle data with per particle variation
            ParticleData data = new ParticleData {
                transform = obj.transform,
                trail = trail,
                position = startPos,
                velocity = Vector2.zero,
                
                // each particle gets slightly different behavior
                speedMultiplier = Random.Range(0.7f, 1.3f),
                noiseOffsetX = Random.Range(0f, 1000f),
                noiseOffsetY = Random.Range(0f, 1000f),
                isPaused = false
            };

            particles.Add(data);
        }
    }

    void Update() {
        float dt = Time.deltaTime;
        float time = Time.time;
        
        // update trail length if changed
        if (Mathf.Abs(trailLength - lastTrailLength) > 0.01f) {
            UpdateTrailLength();
            lastTrailLength = trailLength;
        }
        
        // calculate how many particles to respawn this frame
        float respawnsThisFrame = (respawnRate / 100f) * particles.Count * dt;
        int respawnCount = Mathf.FloorToInt(respawnsThisFrame);
        if (Random.value < (respawnsThisFrame - respawnCount))
            respawnCount++;

        foreach (ParticleData p in particles) {
            if (p.isPaused)
                continue; // skip updates while trail is fading
            
            // random respawn check
            if (respawnCount > 0 && Random.value < (respawnCount / (float)particles.Count)) {
                ScheduleRespawn(p);
                respawnCount--;
                continue;
            }
            
            // sample flow field at current position
            Vector2 flowDirection = flowField.Sample(p.position);
            // add small flow noise to avoid lane convergence
            if (flowNoise > 0f) {
                float fnx = Mathf.PerlinNoise(p.noiseOffsetX + time * 0.5f, p.noiseOffsetY) - 0.5f;
                float fny = Mathf.PerlinNoise(p.noiseOffsetX, p.noiseOffsetY + time * 0.5f) - 0.5f;
                Vector2 flowJitter = new Vector2(fnx, fny) * flowNoise;
                flowDirection = (flowDirection + flowJitter).normalized;
            }

            // get sound level and calculate boosts
            float soundLevel = (soundReactor != null) ? soundReactor.GetVolume() : 0f;
            
            // sound dramatically affects turbulence, speed, and spread
            float effectiveTurbulence = turbulence + (soundLevel * soundToTurbulence);
            float effectiveSpread = spread + (soundLevel * soundToSpread);
            float speedBoost = 1f + (soundLevel * soundToSpeed);
            
            // add per-particle turbulence using Perlin noise
            float noiseX = Mathf.PerlinNoise(p.noiseOffsetX + time * 2f, p.noiseOffsetY) - 0.5f;
            float noiseY = Mathf.PerlinNoise(p.noiseOffsetX, p.noiseOffsetY + time * 2f) - 0.5f;
            Vector2 turbulenceForce = new Vector2(noiseX, noiseY) * effectiveTurbulence;
            
            // calculate perpendicular drift (to spread particles sideways)
            Vector2 perpendicular = new Vector2(-flowDirection.y, flowDirection.x);
            float driftNoise = Mathf.PerlinNoise(p.noiseOffsetX + time, p.noiseOffsetY + time * 0.5f) - 0.5f;
            Vector2 driftForce = perpendicular * driftNoise * effectiveSpread;
            
            // add random burst when sound is loud (like explosive effect?)
            Vector2 burstForce = Vector2.zero;
            if (soundLevel > 0.3f) {
                burstForce = Random.insideUnitCircle * soundLevel * 2f;
            }

            // apply all forces to velocity
            p.velocity += flowDirection * flowStrength * p.speedMultiplier * speedBoost * dt;
            p.velocity += turbulenceForce * dt * 8f;
            p.velocity += driftForce * dt * 5f;
            p.velocity += burstForce * dt;

            // apply damping
            p.velocity *= damping;

            // clamp speed (also affected by per particle multiplier)
            float particleMaxSpeed = maxSpeed * p.speedMultiplier;
            if (p.velocity.magnitude > particleMaxSpeed) {
                p.velocity = p.velocity.normalized * particleMaxSpeed;
            }

            // update position
            p.position += p.velocity * dt;
            if (positionJitter > 0f) {
                p.position += Random.insideUnitCircle * positionJitter * dt;
            }

            // wrap at boundaries (this also updates transform if wrapped)
            bool wrapped = WrapPosition(p);

            // apply to transform (only if we didn't just wrap)
            if (!wrapped) {
                p.transform.position = new Vector3(p.position.x, p.position.y, 0);
            }
        }
    }
    
    // updates the trail duration for all particles
    void UpdateTrailLength() {
        foreach (ParticleData p in particles) {
            if (p.trail != null) {
                p.trail.time = trailLength;
            }
        }
    }
    
    // respawns a single particle at a random position
    // trails are allowed to fade naturally by pausing emission briefly
    void RespawnParticle(ParticleData p) {
        // calculate new position
        Vector2 newPos = new Vector2(
            Random.Range(boundsMin.x, boundsMax.x),
            Random.Range(boundsMin.y, boundsMax.y)
        );
        ScheduleFadeAndMove(p, newPos);
    }

    // to wrap particle position to opposite edge when it goes out of bounds
    // teleports the transform and pauses emission briefly to avoid streaks
    // returns true if wrapping happened
    bool WrapPosition(ParticleData p) {
        bool wrapped = false;

        // wrap X
        if (p.position.x < boundsMin.x) {
            p.position.x = boundsMax.x;
            wrapped = true;
        }
        else if (p.position.x > boundsMax.x) {
            p.position.x = boundsMin.x;
            wrapped = true;
        }

        // wrap Y
        if (p.position.y < boundsMin.y) {
            p.position.y = boundsMax.y;
            wrapped = true;
        }
        else if (p.position.y > boundsMax.y) {
            p.position.y = boundsMin.y;
            wrapped = true;
        }

        // if wrapped, schedule fade and move to avoid streaks
        if (wrapped) {
            Vector2 newPos = p.position;
            ScheduleFadeAndMove(p, newPos);
            // reset velocity to prevent sudden jerky movement after wrap
            p.velocity *= 0.5f;
        }
        
        return wrapped;
    }

    void ScheduleRespawn(ParticleData p) {
        if (!p.isPaused) {
            Vector2 newPos = new Vector2(
                Random.Range(boundsMin.x, boundsMax.x),
                Random.Range(boundsMin.y, boundsMax.y)
            );
            ScheduleFadeAndMove(p, newPos);
        }
    }

    void ScheduleFadeAndMove(ParticleData p, Vector2 newPos) {
        if (p == null || p.isPaused)
            return;
        StartCoroutine(FadeAndMoveCoroutine(p, newPos));
    }

    private System.Collections.IEnumerator FadeAndMoveCoroutine(ParticleData p, Vector2 newPos) {
        p.isPaused = true;
        if (p.trail != null) {
            p.trail.emitting = false;
        }

        // wait for the trail to fade, capped to avoid long pauses on very long trails
        float waitTime = Mathf.Min(trailLength, fadeWaitCap);
        yield return new WaitForSeconds(waitTime);

        // move to new position and restart emission with a fresh trail start
        p.position = newPos;
        p.velocity = Vector2.zero;
        p.transform.position = new Vector3(newPos.x, newPos.y, 0);

        if (p.trail != null) {
            p.trail.Clear(); // old trail already faded, clear to avoid reconnect
            p.trail.emitting = true;
        }

        p.isPaused = false;
    }

    // respawns all particles at random positions.
    // (got rid of clear trails stuff)
    public void ClearAndRespawn() {
        foreach (ParticleData p in particles) {
            // new random position
            p.position = new Vector2(
                Random.Range(boundsMin.x, boundsMax.x),
                Random.Range(boundsMin.y, boundsMax.y)
            );
            p.velocity = Vector2.zero;
            
            // refresh per-particle variation
            p.speedMultiplier = Random.Range(0.7f, 1.3f);
            p.noiseOffsetX = Random.Range(0f, 1000f);
            p.noiseOffsetY = Random.Range(0f, 1000f);
            
            // update transform and pause emission to avoid streaks
            p.transform.position = new Vector3(p.position.x, p.position.y, 0);
            if (p.trail != null) {
                StartCoroutine(PauseAndRestoreTrail(p.trail));
            }
        }
        
        Debug.Log("Particles cleared and respawned");
    }

    private System.Collections.IEnumerator PauseAndRestoreTrail(TrailRenderer tr) {
        if (tr == null) yield break;
        
        // store the original time
        float originalTime = tr.time;

        // stop emitting and squash the trail so no segment bridges 
        // gets rid of weird straight lines crossing screen from wrap
        tr.emitting = false;
        tr.time = 0f;

        // wait one frame
        yield return null;

        if (tr != null) {
            // restore time and re-enable emitting
            tr.time = originalTime;
            tr.emitting = true;
        }
    }

    // draw debug visualization
    void OnDrawGizmos() {
        if (!showDebugDots || particles == null)
            return;

        Gizmos.color = Color.yellow;
        foreach (ParticleData p in particles) {
            Gizmos.DrawSphere(new Vector3(p.position.x, p.position.y, 0), 0.05f);
        }
    }
}

