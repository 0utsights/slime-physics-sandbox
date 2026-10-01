# Hideout — One-Bit Slime

A Unity / C# soft-body sandbox focused on responsive movement and deformation:
explore slopes, landings, drops, and a low passage at your own pace.
The physics prototype began in April 2026;
the playground and fixes are being prepared in October 2026.

**Status: local work in progress. The current changes have not been compiled,
play-tested, or profiled in Unity yet.**

## Open and play

1. Install Unity **6000.4.0f1**, matching `ProjectSettings/ProjectVersion.txt`.
2. Open this folder through Unity Hub and let the packages import.
3. Open `Assets/Scenes/SampleScene.unity` and press Play.
4. The playground creates its geometry at runtime; the original sample geometry
   stays in the saved scene. A/D or arrows move, Space/W/Up jumps,
   S/Down crouches, and R resets the complete body.

The render effect uses Unity's Built-in Render Pipeline. Switching to URP needs
a different rendering integration. Do not change pipelines just to import it.

## What is worth studying

- `SlimeBody`: a heavy center and counterclockwise perimeter nodes, radial distance
  constraints and neighbor springs, shape-matching recovery, compression pressure,
  crouch-dependent stiffness, and contact-aware force clamping.
- `SlimeNodeContact`: collision normals and a residual-overlap correction.
- `SlimeMesh`: a triangle fan with Catmull–Rom boundary smoothing.
- `SlimeController`: buffered jump input and coyote time.
- `OneBitCamera` / `PaletteManager`: luminance thresholding and two palette colors.
- `SlimePlayground`: an environment for trying different contacts, with controls
  and a full-body reset. There is no timer or finish condition.

This combines Unity's solver with custom recovery forces; it is not a new physics
engine. The mesh is a visual approximation and is not the collision boundary.

## Current repair work

- Corrected inward pressure normals on the counterclockwise initial perimeter.
- Corrected immediate palette assignment and released owned rendering materials.
- Added keyboard-null handling, opposing-input cancellation, timed jump buffering,
  and upward-facing support checks independent of smoothed deformation state.
- Retained remaining contacts when one collision exits.
- Added full-body reset, a physics sandbox, and shader inclusion for player builds.
- Moved global physics timing/iteration settings into the demo, with restoration.
- Added Edit Mode regressions for pressure direction and immediate palette colors.

## Verification before publishing

Run the Edit Mode tests in Window → General → Test Runner. Then verify:

- Jump and land repeatedly; confirm one press never permits unintended air jumps.
- Test contact transitions on the slope, between two surfaces, and at corners.
- Enter and leave the low passage without sinking or unstable node motion.
- Reset while airborne and while deformed; confirm the mesh and body recover.
- Leave the app unfocused, return, and verify input and timing.
- Make a Windows build; confirm the shader remains present and reset works.
- Profile CPU time and allocations before stating any performance numbers.

## Limitations and scope

The priorities are environment design and physics feel. Tune acceleration,
braking, jump response, landing recovery, and squeezing in the editor, one change
at a time. Judge each change on flat ground, slopes, ledges, and the low passage
before keeping it. A visually attractive environment should also make those
interactions easy to see and repeat.

Keep this as a mechanics demo. No networking, AI, extra levels, or account system
is needed. The contact correction currently queries one overlapping collider per
node; dense corners and fast movement still need testing. Node ordering may fold
under large deformation; a radial triangle fan cannot represent arbitrary concave
or self-intersecting shapes. Node count should be changed before play, not live.

The supplied editor tooling is useful for tuning. Remove unused parameters only
after checking serialization and behavior; do not expand the solver before a
stable baseline is measured. Describe which parts you implemented or adapted in
the eventual portfolio write-up, including AI/tool assistance where relevant.
