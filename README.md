# Slime Physics Sandbox

A Unity / C# soft-body physics prototype focused on slime deformation and movement through a 2D environment.

[Portfolio](https://johnsurles.com/#slime-physics-sandbox) · [GitHub profile](https://github.com/0utsights)

## The sandbox

Explore slopes, landings, drops, and tight spaces at your own pace. The focus is the environment and how the slime moves, compresses, and recovers its shape.

- A deformable body built with **Rigidbody2D, distance joints, and springs**.
- Shape recovery, compression pressure, and a procedural mesh for deformation.
- Buffered jumping, coyote time, and contact-aware force handling.
- A full-body reset for repeating movement and contact experiments.
- A two-color presentation using Unity's Built-in Render Pipeline.

**Prototype status:** the latest changes still need compilation, playtesting, and profiling in Unity. A playable build has not yet been published.

## Open in Unity

1. Install **Unity 6000.4.0f1**, matching `ProjectSettings/ProjectVersion.txt`.
2. Add this folder in Unity Hub and let the packages import.
3. Open `Assets/Scenes/SampleScene.unity` and press Play.

The sandbox builds its geometry at runtime. The original sample geometry stays in the saved scene.

| Action | Controls |
| --- | --- |
| Move | A / D or Left / Right |
| Jump | Space, W, or Up |
| Squeeze | S or Down |
| Reset body | R |

## How it works

The slime has a heavy center and a ring of lighter perimeter bodies. Radial distance constraints tether the perimeter to the center, while neighbor springs connect the ring. Shape recovery and compression pressure help the body return toward its rest shape; crouching reduces stiffness so it can deform into smaller spaces.

Contact normals guide recovery forces along surfaces. A smoothed procedural mesh draws the boundary, while the perimeter colliders handle contact with the environment.

| Component | Responsibility |
| --- | --- |
| `SlimeBody` | Body construction, recovery, pressure, crouching, and reset |
| `SlimeNodeContact` | Contact tracking and residual-overlap correction |
| `SlimeController` | Movement input, jump buffering, and coyote time |
| `SlimeMesh` | Triangle fan and Catmull-Rom boundary smoothing |
| `SlimePlayground` | Environment, controls, presentation, and reset |
| `OneBitCamera` / `PaletteManager` | Luminance threshold and two-color palette |

The implementation extends Unity's 2D solver with custom recovery forces. The visual mesh is an approximation of the body, not its collision boundary.

## Validation and tuning

Edit Mode regression cases cover outward pressure normals and immediate palette assignment. Run them through **Window > General > Test Runner**. These cases have been added but have not yet been run in the editor.

Playtesting priorities:

- Movement acceleration, braking, and jump response on flat ground.
- Landing recovery and contact transitions on slopes, ledges, and corners.
- Entering and leaving the low passage without sinking or unstable node motion.
- Resetting while airborne or compressed, and returning after the app loses focus.
- Shader inclusion in a Windows build, plus CPU and allocation profiling.

Tune one parameter at a time and repeat the same interactions before keeping a change.

## Current limits

The overlap correction queries one overlapping collider per node, so dense corners and fast movement need further testing. Extreme deformation can fold the perimeter, and a radial triangle fan cannot represent arbitrary concave or self-intersecting shapes. Set the node count before entering Play Mode.

The project uses the Built-in Render Pipeline; a URP version would need a different rendering integration.
