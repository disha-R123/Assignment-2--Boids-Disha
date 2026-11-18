# Windbending — Gesture-Controlled Boids
 
**Boids Airbending simulation*.**
 
Wispy streams of wind drift across the screen. Raise a palm and they follow it, circle your hand and they swirl into a vortex, push your palms together and they compress into a spinning sphere of air. Pull your hands apart and the wind scatters. A wind sound rises and falls with the motion, so a fast gesture actually sounds like a gust.
 
## Gestures
 
| Gesture | Effect |
|---|---|
| **One open palm** | The whole formation follows your hand and tightens up |
| **One open palm, moving fast** | Boids speed up and accelerate harder the faster you move |
| **One open palm, drawn in circles** | The formation spins with your hand. Every cluster orbits in the same direction as your circle, harder the faster you circle |
| **Two open palms, far apart** | More chaos: the flock loosens and wanders |
| **Two open palms, brought together** | Groups are pulled to the point between your hands, shrink and speed up |
| **Two open palms, very close** | Boids switch to 3D orbits on a sphere between your palms, in tilted atom-style rings |
| **One pinching hand** (thumb + index) | Separation boost: boids push apart and take up more space |
| **No hands** | The flock drifts in its default rotating formation |
 
---
 
## Requirements
 
- Unity with the **Universal Render Pipeline (URP)**. The default trail shader is `Universal Render Pipeline/Particles/Unlit`. 
- A **MediaPipe** GameObject in the scene with the `MediaPipeBodyTracker` component, which handles the webcam and hand tracking. It exposes:
  - `LeftHandTracked`, `RightHandTracked` 
  - `LeftHandPosition`, `RightHandPosition`
  - `LeftHandPinch`, `RightHandPinch` 
- A camera tagged **MainCamera**, looking down the z-axis at the z = 0 plane (perspective or orthographic both work)
- A webcam
 
---
 
## How to Run
Everything is in the 'Gesture Flock' Scene within the Assets->327_CourseMaterial-> 05_Gesture Folder
 
### Flock
| Field | Description |
|---|---|
| `numberOfBoids` | Total boids (16–1000) |
| `numberOfGroups` | Number of separate groups (1–10) |
| `screenMargin` | Inset from the screen edges where boids turn back |
| `groupSpread` | How far apart group homes sit, as a fraction of the screen's half-size |
| `minSpeed` / `maxSpeed` | Speed limits |
| `maxForce` | Steering limit. Lower values give smoother turns |
 
### Boid Rules
| Field | Description |
|---|---|
| `neighborRadius` |
| `separationRadius` | 
| `separationWeight` | Strength of separation |
| `alignmentWeight` | Strength of matching neighbors' heading |
| `cohesionWeight` | Strength of moving toward the neighbors' center |
| `otherGroupAvoidRadius` / `Weight` | How strongly groups keep apart |
| `homeWeight` | Pull toward the group's home |
| `boundsWeight` | Strength of turning back at the screen edges |
| `wander` | Random steering once settled |

### Chaos
| Field | Description |
|---|---|
| `startChaosSeconds` | How long the opening chaos takes to settle |
| `startWander` | Random steering strength at the very start |
| `baselineChaos` | Disorder that remains after settling |
| `farHandsChaos` | Chaos level when two open palms are fully apart |
| `wanderChangeRate` | How quickly each boid's random direction drifts |
 
### Gestures
| Field | Description |
|---|---|
| `handsFarDistance` | 0.7 | Palm distance (viewport units) with no attraction |
| `handsNearDistance` | 0.1 | Palm distance for full attraction |
| `sphereRadius` | 0.35 | Sphere-orbit radius, as a fraction of the screen's half-height |
| `palmAccelBoost` | 3 | Acceleration multiplier for a fast-moving palm |
| `palmSpeedBoost` | 1.8 | Top-speed multiplier for a fast-moving palm |
| `palmSpeedSensitivity` | 1.5 | How easily palm movement reaches the full boost |
 
Finer gesture tuning (smoothing times, circle-detection thresholds, sphere strength, and so on) lives in the `const` values at the top of the script.
 
---
 
## WindSound
 
`WindSound` script turns the flock's motion into sound. Each frame it reads `InteractiveBody.AverageAcceleration`, which is how much the boids' velocities changed on average in the last physics step. It maps that onto a 0–1 **intensity**. The intensity is smoothed so the wind swells and fades instead of flickering, and it then drives the `AudioSource`'s volume.
 
Calm flocking gives a soft, low hush. Fast palm sweeps, gathering, or the opening chaos make the wind rise and brighten.
 
 
## How It Works
 
Each `FixedUpdate`:
 
1. **Read gestures.** Hand states come from MediaPipe and are converted to world positions on the boids' plane. Each gesture value (attraction, spread, pinch, follow, circling, palm motion) eases toward its target.
2. **Update the formation.** Group homes rotate on a screen-shaped ellipse. They are moved toward your open palm (follow) or the point between your palms (gather), and shrink as your palms come together.
3. **Set the rule weights.** Gestures scale the boid rule weights, speed limits and acceleration. Chaos starts at 1 and eases down to a baseline, which rises when your palms are far apart.
4. **Steer.** Every boid checks every other boid (O(n²)) for separation, same-group alignment and cohesion, and other-group avoidance. It then adds home pull, circulation, the optional sphere orbit, screen walls and smooth Perlin-noise wander.
5. **Move.** All boids apply their steering at once, speeds are clamped, and positions are updated. Average acceleration is recorded for `WindSound`.
### Circle detection
The script keeps a short history (about 0.8 s) of the open palm's position. It adds up the signed angle the hand sweeps around the center of that path. If the path is big enough and sweeps at least about half a turn, the sweep rate (rad/s) becomes the formation's spin. This way straight swipes and jitter don't count.
 
### Sphere orbit
When your palms come close, each boid holds a fixed distance from the point between your hands and travels along its own tilted axis. Each group gets a ring tilted 60° toward the camera and turned evenly around the view direction. Neighboring groups orbit in opposite directions, with a little per-boid jitter. Outside of sphere mode, boids are kept mostly flat on the z = 0 plane.
 
