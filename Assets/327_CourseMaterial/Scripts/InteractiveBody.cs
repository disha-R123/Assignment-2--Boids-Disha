using UnityEngine;

public class InteractiveBody : MonoBehaviour
{
    [Header("Flock")]
    [Range(16, 1000)] public int numberOfBoids = 120;
    [Range(1, 10)] public int numberOfGroups = 3;
    [Range(0f, 0.3f)] public float screenMargin = 0.04f;     // Inset from the screen edges where boids turn back
    [Range(0.1f, 0.95f)] public float groupSpread = 0.6f;    // Group homes, as a fraction of the screen's half-width and half-height
    public float minSpeed = 1.2f;
    public float maxSpeed = 3.5f;
    public float maxForce = 6f;            // Steering limit, so turns stay smooth

    [Header("Boid Rules")]
    public float neighborRadius = 1.2f;    // How far a boid can "see" its own group
    public float separationRadius = 0.35f; // Personal space, applies to every group
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;
    public float otherGroupAvoidRadius = 0.8f;
    public float otherGroupAvoidWeight = 1.2f;
    public float homeWeight = 0.4f;        // Pull toward the group's home area
    public float boundsWeight = 3f;
    public float wander = 0.3f;            // Random steering once the flock has settled

    [Header("Formation")]
    public float circulation = 0.6f;       // Orbit around each group's home
    public float homeDrift = 0.12f;        // How fast the group homes slowly rotate

    [Header("Trails")]
    public Color trailColorA = new Color(0.39f, 0.63f, 1f);  // Light blue (100, 160, 255)
    public Color trailColorB = new Color(0.78f, 0.63f, 1f);  // Light purple (200, 160, 255)
    public float trailSeconds = 4f;        // Duration of the trail (longer = more lingering smoke)
    public float trailWidth = 0.04f;       // Width right behind the boid
    public float smokeSpread = 3f;         // How much wider the trail gets toward its tail, like smoke expanding
    [Range(0f, 1f)] public float smokeOpacity = 0.45f; // Opacity right behind the boid; it fades to 0 at the tail
    public Shader trailShader;

    [Header("Chaos")]
    public float startChaosSeconds = 8f;   // How long the opening chaos takes to settle
    public float startWander = 5f;         // Random steering strength at the very start
    [Range(0f, 1f)] public float baselineChaos = 0.25f; // Disorder that remains after settling
    [Range(0f, 1f)] public float farHandsChaos = 0.7f;  // Baseline chaos when two open palms are fully apart
    public float wanderChangeRate = 0.8f;  // How quickly each boid's random direction drifts

    [Header("Gestures")]
    public float handsFarDistance = 0.7f;  // Viewport units. Palms this far apart = no attraction
    public float handsNearDistance = 0.1f; // Palms this close = full attraction
    public float sphereRadius = 0.35f;     // Sphere radius, as a fraction of the screen's half-height
    public float palmAccelBoost = 3f;      // Acceleration multiplier when the open palm moves fast
    public float palmSpeedBoost = 1.8f;    // Top-speed multiplier when the open palm moves fast
    public float palmSpeedSensitivity = 1.5f; // How easily palm movement reaches the full boost

    // Gesture tuning
    const float gestureSmoothing = 0.25f;      // Seconds to ease in and out of a gesture
    const float followSeconds = 0.4f;          // How smoothly the gather / follow point tracks your hands
    // Two open palms
    const float maxCohesionBoost = 3f;         // Cohesion multiplier at full attraction
    const float closeHandsAccelBoost = 2.5f;   // Acceleration multiplier with palms together
    const float closeHandsSpeedBoost = 1.6f;   // Top-speed multiplier with palms together
    const float closestGroupSpread = 0.2f;     // Formation size with palms together, as a fraction of groupSpread
    const float sphereStartAttraction = 0.5f;  // Attraction where the sphere orbit starts forming
    const float sphereWeight = 2.5f;           // How strongly boids hold their spherical orbits
    const float sphereAxisJitter = 0.35f;      // Per-boid tilt variation (0 = clean rings, 1 = all over the sphere)
    // One open palm (follow + circle swirl)
    const float followSpread = 0.45f;          // Formation size while following, as a fraction of groupSpread
    const float circleWindowSeconds = 0.8f;    // How much recent hand motion is checked for a circle
    const float circleMinRadius = 0.03f;       // Viewport units; smaller wiggles don't count as circles
    const float circleMinSweep = 3f;           // Radians the hand must sweep within the window (about half a turn)
    const float circleFullSpeed = 6f;          // Hand turning speed (rad/s) that counts as full strength
    const float formationSpinGain = 0.5f;      // How much of the hand's turning speed the formation follows
    const float circleCirculationBoost = 2f;   // Extra orbit strength for every cluster while circling
    // One pinch
    const float maxSeparationBoost = 4f;       // Separation multiplier
    const float pinchSeparationRadiusBoost = 2f;

    // Average acceleration of all boids this step (world units per second squared). Read by WindSound.
    public float AverageAcceleration { get; private set; }

    struct BodyProperty
    {
        public Vector3 position, velocity, acceleration;
        public int group;
    }

    // Boids
    BodyProperty[] bp;
    GameObject[] body;       // Empty objects that carry the trails
    TrailRenderer[] trailRenderer;
    Vector3[] orbitAxis;     // Each boid's spin axis for the sphere orbit
    float[] noiseSeed;
    Vector3[] homes;
    float startTime, homeAngle, currentMaxSpeed;
    Material trailMaterial;

    // Screen
    Camera cam;
    Vector3 screenCenter, boundsMin, boundsMax; // Visible screen area on the boids' plane
    Vector2 screenHalf;

    // Hands (0 = left, 1 = right)
    MediaPipeBodyTracker mp;
    readonly bool[] handTracked = new bool[2], handPinched = new bool[2];
    readonly Vector3[] handWorld = new Vector3[2];
    int leadHand = -1;
    Vector3 gatherPoint, followPoint;
    const int circleHistory = 128;
    readonly Vector2[,] circlePos = new Vector2[2, circleHistory];
    readonly float[,] circleTime = new float[2, circleHistory];
    readonly int[] circleCount = new int[2], circleHead = new int[2];

    // Gesture values, smoothed (0-1 unless noted)
    float handAttraction;  // 0 = palms far apart or not both open, 1 = palms together
    float handSpread;      // 0 = palms together or no two-palm gesture, 1 = palms far apart
    float pinchAmount;     // 1 = one pinching hand
    float followAmount;    // 1 = following an open palm
    float circleAmount;    // 1 = circling at circleFullSpeed or faster
    float circleSpin;      // Palm turning speed in rad/s; + = counterclockwise on screen
    float chaos;           // 1 = fully chaotic, falls to the current baseline
    float accelerationMultiplier = 1f;
    float sphereAmount;    // 1 = full spherical orbits
    float palmMotion;      // 0 = open palm still, 1 = open palm moving fast
    Vector3 previousLead;  // Open palm position last step, for measuring its speed
    bool hadLead;

    // ---------------------------------------------------------------- Setup

    void Start()
    {
        mp = FindObjectOfType<MediaPipeBodyTracker>();
        if (mp == null) Debug.LogWarning("InteractiveBody could not locate a MediaPipeBodyTracker in the scene.");
        cam = Camera.main;
        numberOfGroups = Mathf.Clamp(numberOfGroups, 1, numberOfBoids);
        homes = new Vector3[numberOfGroups];
        currentMaxSpeed = maxSpeed;
        startTime = Time.time;

        CreateTrailMaterial();
        UpdateScreenBounds();
        gatherPoint = followPoint = screenCenter;
        UpdateHomes();
        CreateBoids();
    }

    // One material shared by every trail.
    void CreateTrailMaterial()
    {
        trailMaterial = new Material(trailShader != null ? trailShader : Shader.Find("Universal Render Pipeline/Particles/Unlit"));
    }

    void CreateBoids()
    {
        bp = new BodyProperty[numberOfBoids];
        body = new GameObject[numberOfBoids];
        trailRenderer = new TrailRenderer[numberOfBoids];
        orbitAxis = new Vector3[numberOfBoids];
        noiseSeed = new float[numberOfBoids];
        for (int i = 0; i < numberOfBoids; i++)
        {
            body[i] = new GameObject("Boid " + i);

            // Start scattered: every group mixed across the whole screen, flying in random directions.
            int group = i * numberOfGroups / numberOfBoids;
            bp[i].group = group;
            bp[i].position = new Vector3(Random.Range(boundsMin.x, boundsMax.x), Random.Range(boundsMin.y, boundsMax.y), Random.Range(-0.3f, 0.3f));
            Vector2 heading = Random.insideUnitCircle.normalized;
            bp[i].velocity = new Vector3(heading.x, heading.y, 0f) * Random.Range(minSpeed, maxSpeed);
            body[i].transform.position = bp[i].position;
            noiseSeed[i] = Random.Range(0f, 1000f);

            // Sphere orbit axis: each group's ring is tilted 60 degrees toward the camera and turned evenly around
            // the view direction (atom-style rings); neighboring groups orbit in opposite directions.
            Vector3 groupAxis = Quaternion.AngleAxis(group * 180f / numberOfGroups, Vector3.forward)
                              * (Quaternion.AngleAxis(60f, Vector3.right) * Vector3.up);
            if (group % 2 == 1) groupAxis = -groupAxis;
            orbitAxis[i] = (groupAxis + Random.insideUnitSphere * sphereAxisJitter).normalized;

            // Smoky trail: colors blend from trailColorA (first group) to trailColorB (last group).
            // The trail widens and pales toward its tail while fading out softly.
            float blend = numberOfGroups > 1 ? group / (float)(numberOfGroups - 1) : 0f;
            Color c = Color.Lerp(trailColorA, trailColorB, Mathf.Clamp01(blend + Random.Range(-0.08f, 0.08f)));
            Color tail = Color.Lerp(c, Color.white, 0.5f);
            trailRenderer[i] = body[i].AddComponent<TrailRenderer>();
            trailRenderer[i].sharedMaterial = trailMaterial;
            trailRenderer[i].time = trailSeconds;
            trailRenderer[i].widthMultiplier = trailWidth;
            trailRenderer[i].widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.25f, 1f), new Keyframe(1f, smokeSpread));
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(c, 0f), new GradientColorKey(tail, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(smokeOpacity, 0f), new GradientAlphaKey(smokeOpacity * 0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            trailRenderer[i].colorGradient = gradient;
        }
    }

    // ---------------------------------------------------------------- Simulation

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        UpdateScreenBounds(); // Recomputed each step so resizing the Game view still works.
        UpdateGestures(dt);

        // Formation rotation: a slow drift plus the circling palm's turning speed,
        // capped so the boids can keep up with their moving homes.
        float maxHomeSpin = maxSpeed * 0.8f / Mathf.Max(0.01f, screenHalf.x * groupSpread);
        homeAngle += (homeDrift + Mathf.Clamp(circleSpin * formationSpinGain, -maxHomeSpin, maxHomeSpin)) * dt;
        UpdateHomes();

        // Chaos starts at 1 and eases down to the baseline, so the flocks form gradually.
        // Two open palms: the farther apart they are, the higher the baseline.
        float settle = Mathf.SmoothStep(0f, 1f, (Time.time - startTime) / Mathf.Max(0.01f, startChaosSeconds));
        float chaosFloor = Mathf.Lerp(baselineChaos, Mathf.Max(baselineChaos, farHandsChaos), handSpread);
        chaos = Mathf.Max(chaosFloor, 1f - settle);
        float order = 1f - chaos;                       // Scales the organizing rules
        float wanderStrength = Mathf.Lerp(wander, startWander, chaos);

        // Gestures change the rule weights; the rules themselves stay pure boids.
        float sepWeight = separationWeight * Mathf.Lerp(1f, maxSeparationBoost, pinchAmount);
        float sepRadius = separationRadius * Mathf.Lerp(1f, pinchSeparationRadiusBoost, pinchAmount);
        float cohWeight = cohesionWeight * Mathf.Lerp(1f, maxCohesionBoost, handAttraction);
        float avoidWeight = otherGroupAvoidWeight * (1f - 0.8f * handAttraction);
        float homePull = homeWeight * (1f + handAttraction + followAmount);
        // Boosts: two palms close together, or one open palm moving fast (the faster it moves, the bigger the boost).
        float palmBoost = palmMotion * followAmount;
        accelerationMultiplier = Mathf.Lerp(1f, closeHandsAccelBoost, handAttraction) * Mathf.Lerp(1f, palmAccelBoost, palmBoost);
        currentMaxSpeed = maxSpeed * Mathf.Lerp(1f, closeHandsSpeedBoost, handAttraction) * Mathf.Lerp(1f, palmSpeedBoost, palmBoost);

        // Palms close together: the boids switch to orbiting on a sphere around the point between your palms.
        sphereAmount = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(sphereStartAttraction, 1f, handAttraction));
        float sphereR = Mathf.Max(0.05f, sphereRadius * screenHalf.y);
        float formation = 1f - 0.85f * sphereAmount;   // Fades out the flat formation while the sphere forms
        homePull *= formation;
        cohWeight *= formation;
        avoidWeight *= formation;
        wanderStrength *= 1f - 0.7f * sphereAmount;

        // 1. Steering: every boid looks at its neighbors.
        for (int i = 0; i < numberOfBoids; i++)
        {
            Vector3 p = bp[i].position, v = bp[i].velocity;
            int group = bp[i].group;
            Vector3 separation = Vector3.zero, headingSum = Vector3.zero, centerSum = Vector3.zero, avoid = Vector3.zero;
            int neighbors = 0;
            for (int j = 0; j < numberOfBoids; j++)
            {
                if (j == i) continue;
                Vector3 away = p - bp[j].position;
                float d2 = away.sqrMagnitude;
                if (d2 < 1e-6f) continue;
                float d = Mathf.Sqrt(d2);
                if (d < sepRadius) separation += away / d2; // Closer boids push harder
                if (bp[j].group == group)
                {
                    if (d < neighborRadius)
                    {
                        headingSum += bp[j].velocity;
                        centerSum += bp[j].position;
                        neighbors++;
                    }
                }
                else if (d < otherGroupAvoidRadius)
                {
                    avoid += away / d2; // Keeps the groups separate
                }
            }

            Vector3 acc = Vector3.zero;
            acc += Steer(separation, v) * sepWeight;
            if (neighbors > 0)
            {
                acc += Steer(headingSum / neighbors, v) * alignmentWeight * order;
                acc += Steer(centerSum / neighbors - p, v) * cohWeight * order;
            }
            acc += Steer(avoid, v) * avoidWeight * order;
            acc += Steer(homes[group] - p, v) * homePull * order;

            // Circulation: orbit the group's home; neighboring groups spin in opposite directions.
            // While the open palm circles, every cluster orbits in the hand's direction, and harder.
            Vector3 fromHome = p - homes[group];
            float spin = group % 2 == 0 ? -1f : 1f;
            if (circleAmount > 0.15f) spin = Mathf.Sign(circleSpin);
            acc += Steer(new Vector3(-fromHome.y, fromHome.x, 0f) * spin, v)
                * circulation * (1f + circleAmount * circleCirculationBoost) * order * formation;

            // Sphere orbit: hold a fixed distance from the center and travel along this boid's own tilted orbit.
            if (sphereAmount > 0.001f)
            {
                Vector3 fromCenter = p - gatherPoint;
                float dist = fromCenter.magnitude;
                Vector3 outward = dist > 1e-4f ? fromCenter / dist : Random.onUnitSphere;
                Vector3 along = Vector3.Cross(orbitAxis[i], outward);
                if (along.sqrMagnitude < 1e-4f) along = Vector3.Cross(Vector3.right, outward);
                Vector3 toShell = -outward * (dist - sphereR) / sphereR; // Back onto the sphere surface
                acc += Steer(along.normalized + toShell * 2f, v) * sphereWeight * sphereAmount;
            }

            // Screen walls: turn back when crossing into the margin on any edge.
            Vector3 wall = Vector3.zero;
            if (p.x < boundsMin.x) wall.x = 1f; else if (p.x > boundsMax.x) wall.x = -1f;
            if (p.y < boundsMin.y) wall.y = 1f; else if (p.y > boundsMax.y) wall.y = -1f;
            acc += Steer(wall, v) * boundsWeight;

            // Smooth wander: each boid follows its own drifting noise direction (swirls, not jitter).
            float t = Time.time * wanderChangeRate;
            acc += new Vector3(Mathf.PerlinNoise(noiseSeed[i], t) - 0.5f, Mathf.PerlinNoise(noiseSeed[i] + 37f, t) - 0.5f, 0f) * 2f * wanderStrength;

            // Keep the flock mostly flat facing the camera, except while orbiting the sphere (which needs depth).
            acc.z -= (p.z * 2f + v.z) * (1f - sphereAmount);
            bp[i].acceleration = acc;
        }

        // 2. Move: apply every boid's steering after all of them have looked.
        float accelerationSum = 0f;
        for (int i = 0; i < numberOfBoids; i++)
        {
            Vector3 before = bp[i].velocity;
            bp[i].velocity += bp[i].acceleration * accelerationMultiplier * dt;
            float speed = bp[i].velocity.magnitude;
            if (speed > 1e-4f) bp[i].velocity *= Mathf.Clamp(speed, minSpeed, currentMaxSpeed) / speed;
            bp[i].position += bp[i].velocity * dt;
            body[i].transform.position = bp[i].position;
            accelerationSum += (bp[i].velocity - before).magnitude / dt; // How much this boid's velocity actually changed
        }
        AverageAcceleration = accelerationSum / numberOfBoids;
    }

    // Classic Reynolds steering: desired velocity minus current velocity, limited by maxForce.
    // Returns zero when there is no direction (for example, no boids close enough to separate from).
    Vector3 Steer(Vector3 desiredDirection, Vector3 velocity)
    {
        if (desiredDirection.sqrMagnitude < 1e-8f) return Vector3.zero;
        return Vector3.ClampMagnitude(desiredDirection.normalized * currentMaxSpeed - velocity, maxForce);
    }

    // Group homes sit on an ellipse that turns with homeAngle (slow drift + palm circling).
    // An open palm moves the formation to it; two close palms shrink it and gather it between them.
    void UpdateHomes()
    {
        Vector3 center = Vector3.Lerp(screenCenter, followPoint, followAmount);
        center = Vector3.Lerp(center, gatherPoint, handAttraction);
        if (numberOfGroups == 1) { homes[0] = center; return; }
        float spread = groupSpread * Mathf.Lerp(1f, closestGroupSpread, handAttraction) * Mathf.Lerp(1f, followSpread, followAmount);
        for (int g = 0; g < numberOfGroups; g++)
        {
            // Homes rotate on an ellipse shaped to the screen, and bob gently.
            float angle = g * 2f * Mathf.PI / numberOfGroups + homeAngle;
            homes[g] = center + new Vector3(Mathf.Cos(angle) * screenHalf.x * spread,
                Mathf.Sin(angle) * screenHalf.y * spread + Mathf.Sin(Time.time * 0.31f + g * 1.7f) * screenHalf.y * 0.08f, 0f);
        }
    }

    // ---------------------------------------------------------------- Gestures

    void UpdateGestures(float dt)
    {
        if (mp == null) mp = FindObjectOfType<MediaPipeBodyTracker>();
        bool canMap = mp != null && cam != null;
        for (int hand = 0; hand < 2; hand++)
        {
            handTracked[hand] = mp != null && (hand == 0 ? mp.LeftHandTracked : mp.RightHandTracked);
            handPinched[hand] = handTracked[hand] && IsPinching(hand);
            if (handTracked[hand] && canMap) handWorld[hand] = HandWorldPosition(hand);
        }
        float k = Smooth(dt, gestureSmoothing);

        bool bothOpen = handTracked[0] && handTracked[1] && !handPinched[0] && !handPinched[1];
        int singleHand = handTracked[0] != handTracked[1] ? (handTracked[0] ? 0 : 1) : -1; // Exactly one hand visible
        leadHand = singleHand >= 0 && !handPinched[singleHand] ? singleHand : -1;

        // One open palm: the formation follows it, and moving it in circles swirls the formation.
        float omega = 0f;
        for (int hand = 0; hand < 2; hand++)
        {
            if (hand == leadHand && canMap) omega = MeasureCircle(hand, handWorld[hand]);
            else circleCount[hand] = 0;
        }
        circleSpin = Mathf.Lerp(circleSpin, omega, k);
        circleAmount = Mathf.Lerp(circleAmount, Mathf.Clamp01(Mathf.Abs(omega) / circleFullSpeed), k);

        float targetFollow = 0f, targetMotion = 0f;
        if (leadHand >= 0 && canMap)
        {
            Vector3 lead = handWorld[leadHand];
            if (followAmount < 0.01f) followPoint = lead; // Fresh start; the effect itself still eases in
            followPoint = Vector3.Lerp(followPoint, lead, Smooth(dt, followSeconds));
            targetFollow = 1f;
            // Palm speed in screen-heights per second; slow drift and tracking jitter (under 0.1) don't count.
            if (!hadLead) previousLead = lead;
            float palmSpeed = (lead - previousLead).magnitude / dt / Mathf.Max(0.01f, screenHalf.y * 2f);
            targetMotion = Mathf.Clamp01((palmSpeed - 0.1f) * palmSpeedSensitivity);
            previousLead = lead;
        }
        hadLead = leadHand >= 0 && canMap;
        followAmount = Mathf.Lerp(followAmount, targetFollow, k);
        // Rises quickly when you move the palm, settles smoothly when you slow down.
        palmMotion = Mathf.Lerp(palmMotion, targetMotion, targetMotion > palmMotion ? Smooth(dt, 0.1f) : k);

        // Two open palms: closer = more attraction, farther = more chaos.
        float targetAttraction = 0f, targetSpread = 0f;
        if (bothOpen)
        {
            float distance = Vector2.Distance(mp.LeftHandPosition, mp.RightHandPosition);
            targetAttraction = Mathf.InverseLerp(handsFarDistance, handsNearDistance, distance);
            targetSpread = 1f - targetAttraction;
            if (canMap) gatherPoint = Vector3.Lerp(gatherPoint, (handWorld[0] + handWorld[1]) * 0.5f, Smooth(dt, followSeconds));
        }
        // One pinching hand: separation.
        float targetPinch = singleHand >= 0 && handPinched[singleHand] ? 1f : 0f;

        handAttraction = Mathf.Lerp(handAttraction, targetAttraction, k);
        handSpread = Mathf.Lerp(handSpread, targetSpread, k);
        pinchAmount = Mathf.Lerp(pinchAmount, targetPinch, k);
    }

    // Records the palm's position and returns how fast it is circling (rad/s, + = counterclockwise),
    // or 0 if the recent motion is not a circle. It adds up the angle the hand sweeps around the center
    // of its recent path, so straight swipes and small jitters don't count.
    float MeasureCircle(int hand, Vector2 position)
    {
        int head = (circleHead[hand] + 1) % circleHistory;
        circleHead[hand] = head;
        circlePos[hand, head] = position;
        circleTime[hand, head] = Time.time;
        circleCount[hand] = Mathf.Min(circleCount[hand] + 1, circleHistory);

        // Samples inside the time window, newest first.
        int n = 0;
        while (n < circleCount[hand] && Time.time - circleTime[hand, (head - n + circleHistory) % circleHistory] <= circleWindowSeconds) n++;
        if (n < 8) return 0f;
        float duration = Time.time - circleTime[hand, (head - (n - 1) + circleHistory) % circleHistory];
        if (duration < 0.2f) return 0f;

        Vector2 center = Vector2.zero;
        for (int s = 0; s < n; s++) center += circlePos[hand, (head - s + circleHistory) % circleHistory];
        center /= n;
        float radius = 0f, sweep = 0f;
        for (int s = n - 1; s >= 0; s--)
        {
            Vector2 offset = circlePos[hand, (head - s + circleHistory) % circleHistory] - center;
            radius += offset.magnitude;
            if (s > 0)
            {
                Vector2 next = circlePos[hand, (head - s + 1 + circleHistory) % circleHistory] - center;
                sweep += Vector2.SignedAngle(offset, next) * Mathf.Deg2Rad;
            }
        }
        radius /= n;
        float minRadius = circleMinRadius * screenHalf.y * 2f; // Viewport units to world units
        if (radius < minRadius || Mathf.Abs(sweep) < circleMinSweep) return 0f;
        return sweep / duration;
    }

    // Pinch detection: thumb and index fingertip touching.
    bool IsPinching(int hand)
    {
        return hand == 0 ? mp.LeftHandPinch : mp.RightHandPinch;
    }

    static float Smooth(float dt, float seconds)
    {
        return 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, seconds));
    }

    // ---------------------------------------------------------------- Screen

    // The visible rectangle on the boids' plane (z = 0), for perspective or orthographic cameras.
    void UpdateScreenBounds()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return; // Needs a camera tagged MainCamera
        Vector3 lowerLeft = cam.ViewportToWorldPoint(new Vector3(0f, 0f, -cam.transform.position.z));
        Vector3 upperRight = cam.ViewportToWorldPoint(new Vector3(1f, 1f, -cam.transform.position.z));
        screenCenter = (lowerLeft + upperRight) * 0.5f;
        screenCenter.z = 0f;
        screenHalf = new Vector2(Mathf.Abs(upperRight.x - lowerLeft.x), Mathf.Abs(upperRight.y - lowerLeft.y)) * 0.5f;
        Vector3 inset = new Vector3(screenHalf.x * (1f - screenMargin * 2f), screenHalf.y * (1f - screenMargin * 2f), 0f);
        boundsMin = screenCenter - inset;
        boundsMax = screenCenter + inset;
    }

    Vector3 HandWorldPosition(int hand)
    {
        Vector3 viewport = hand == 0 ? mp.LeftHandPosition : mp.RightHandPosition;
        // Flip both axes: mirror left/right like a selfie camera, and MediaPipe's y points down.
        return cam.ViewportToWorldPoint(new Vector3(1f - viewport.x, 1f - viewport.y, -cam.transform.position.z));
    }
}