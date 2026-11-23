# Fluid-Sim

Development videos: [Simulation](https://youtu.be/rSKMYc1CQHE?si=KNw_i1sN2_CWEmzA) and [Rendering](https://youtu.be/kOkfC5fLfgE?si=1hXtw9nIiHllA6gn).
</br>Project created in Unity 2022.3

## 2D boundary-particle shader

[FluidSim2DWithBoundaryParticles.compute](Assets/Scripts/Sim2D/Compute/FluidSim2DWithBoundaryParticles.compute)
replaces the experimental mass shader, preserving its asset GUID and scene
references. It retains the symmetric pressure-force formulation
`m_i * m_j * (p_i / rho_i^2 + p_j / rho_j^2)`, followed by division by
particle mass for acceleration. Ordinary pressure is clamped to nonnegative
values. This differs from the legacy near-surface shader's pressure model.

**Particle Mass** defaults to **1** and is shared by fluid and boundary samples.
Both mass and **Target Density** (the boundary rest density) must be finite and
positive. Mass is included in both fluid density sums and in pressure forces.
Changing mass requires corresponding density-unit changes, including scaling
target density to preserve the same equilibrium for a fixed sample layout.

### Boundary contract

- `numParticles` is the **total** number of fluid and boundary samples. All
  particle buffers, spatial-hash buffers, and dispatches use this total.
- `ParticleTypes` is a `uint` buffer: **0 = fluid**, **1 = fixed boundary**.
  Types are reordered with positions, predicted positions, and velocities using
  `SortTarget_ParticleTypes`; sorted indices cannot identify particle type.
- Boundary predicted positions equal their fixed positions and their velocities
  are zero. They participate in hashing and both fluid density kernel sums.
  Boundary density is assigned `float2(targetDensity, 0)` without a neighbour
  sum; its near-density channel is unused.
- Boundary ordinary pressure is zero at rest density. For each fluid-boundary
  pair, the fluid particle's ordinary pressure is used as the neighbour's
  pressure **only for that pair**, with boundary density still equal to rest
  density. No mirrored pressure is stored or shared between fluid particles.
  Its force is added to the same ordinary-pressure sum as fluid neighbours.
- Boundary pairs are excluded from near-pressure and viscosity force sums.
  Fluid-fluid near-pressure and viscosity calculations remain in place.
  Boundary samples skip external-force, pressure, viscosity, and position
  integration work; only their fixed prediction and zero velocity are maintained.
- Neighbour searches check the actual grid cell after the hashed-key lookup,
  preventing duplicate contributions when different cells share a hash bucket.

`FluidSim2D` detects the optional `InitializeParticleTypes` kernel, allocates and
binds both type buffers, and initializes every sample as fluid on startup and
reset. Existing shaders without this kernel retain their original bindings and
collision handling. The new shader's first eight kernel indices are unchanged.

### Bounded simulation setup

1. Add [Spawner2DBounded](Assets/Scripts/Sim2D/Spawner2DBounded.cs). Configure its
   fluid spawn regions and its **Bounds Centre** / **Bounds Size** in world units.
2. Add [FluidSim2DBounded](Assets/Scripts/Sim2D/FluidSim2DBounded.cs), assign that
   spawner, and use `FluidSim2DWithBoundaryParticles.compute` as its compute shader.
   Set smoothing radius, particle mass, and target/rest density on the simulation.
   Boundary mass is the same mass as the fluid; there is no separate mass setting.
3. Assign the bounded simulation to the existing `ParticleDisplay2D` **Sim**
   reference. It renders the complete combined buffer with the existing particle
   shader and scale. Set **Boundary Colour** on the display to choose a solid
   colour for boundary particles (default white); fluid particles keep using the
   velocity colour map. Boundary colours follow the sorted particle-type flags
   and can be changed during play. Particle display scale remains a visual
   setting, not the sample spacing.

The bounded spawner reuses the original fluid-region spawning and appends a
rectangular shell of stationary boundary samples, including corners without
duplicates. For a 2D square lattice, sample spacing is
`spacing = sqrt(particleMass / targetDensity)`.
The inner cell counts are `ceil(boundsSize / spacing)` on each axis. Consequently
the **sampled inner box is rounded outward**, remains centered on Bounds Centre,
and may be less than one spacing larger per dimension than the requested box.
The first boundary centers are half a spacing outside that sampled inner border.
There are `ceil(smoothingRadius / spacing)` layers, giving a sampled shell depth
of at least the smoothing radius and less than one additional spacing. No spacing
is stretched to fit the requested dimensions. These are sample-cell dimensions,
not hard collision surfaces or the visual particle radius.

Bounds and spawn-region gizmos belong to the spawner. Green shows the requested
box; once samples have been generated, cyan shows their actual inner rectangle
and yellow shows their outer rectangle. The bounded components have **no obstacle
settings or analytic box collisions**. Pressure provides the boundary response;
there is no positional clamp or tunneling fallback.

Mass, rest density, smoothing radius, box settings, and spawn settings take effect
on **startup or R/reset**, not immediately during play. The bounded simulation
keeps using the active mass, rest density, and smoothing radius until reset so
that shader parameters remain consistent with the existing samples. R regenerates
fluid and boundary data, resizes the combined buffers/hash table as needed,
restores type flags, updates densities, and leaves the simulation paused.
Space resumes and Right Arrow single-steps as before. Other simulation controls
(gravity, pressure multipliers, viscosity, etc.) continue updating normally.

### Short-range boundary repulsion

`FluidSim2DBounded` adds a fluid-boundary force
`D * ((r0/r)^p1 - (r0/r)^p2) * (fluidPosition - boundaryPosition) / r^2`
to the pressure-force accumulator, before dividing by particle mass. It acts
only for `r < r0`; fluid-fluid interactions and fixed boundary samples are
unchanged. Ordinary mirrored pressure still contributes independently.

The **Boundary Repulsion** fields update during play:
- **Strength** is the scalar `D` (default 1); set it to 0 to disable repulsion.
- **Radius** is `r0`; 0 (default) uses the boundary spacing captured at startup
  or reset. A positive value overrides it. While repulsion is enabled, the
  effective radius (including automatic spacing) must not exceed the active
  smoothing radius; unsupported settings produce an explicit error. This keeps
  the original nine-cell `offsets2D` pressure search unchanged. With strength 0,
  a larger radius may be configured but must be corrected before enabling it.
- **Exponent 1 / 2** default to 12 / 6 and must satisfy `p1 > p2 > 0`.
- **Min Distance Fraction** defaults to 0.1. The magnitude uses
  `max(r, r0 * fraction)` while retaining the actual outward direction.
- **Max Force** defaults to 1000 and caps each fluid-boundary pair's repulsion,
  not the summed force or acceleration. Powers are evaluated in log space to
  avoid overflow before applying the cap.

At exact overlap, the direction points from the boundary sample toward the
active box centre (positive Y if both coincide). All parameters must be finite;
strength/radius must be nonnegative, the minimum-distance fraction must be in
`(0, 1]`, and the force cap must be positive. This regularization intentionally
departs from the singular formula at very small separations. Strong repulsion
still requires sufficiently small timesteps; it is not a no-escape constraint.

Both simulation variants share the buffer/dispatch lifecycle in
[FluidSim2DBase](Assets/Scripts/Sim2D/FluidSim2DBase.cs). Existing `FluidSim2D`
components, scene references, obstacle/box settings, and legacy compute shaders
remain supported; scenes are not automatically converted to the bounded variant.
Using the boundary shader with the old all-fluid spawner still provides no walls.

![Fluid Simulation](https://raw.githubusercontent.com/SebLague/Images/master/Fluid%20vid%20thumb.jpg)
![Fluid Rendering](https://raw.githubusercontent.com/SebLague/Images/refs/heads/master/FluidRendering.jpg)

With thanks to the following papers:
* Simulation:
* https://matthias-research.github.io/pages/publications/sca03.pdf
* https://web.archive.org/web/20250106201614/http://www.ligum.umontreal.ca/Clavet-2005-PVFS/pvfs.pdf
* https://sph-tutorial.physics-simulation.org/pdf/SPH_Tutorial.pdf
* https://web.archive.org/web/20140725014123/https://docs.nvidia.com/cuda/samples/5_Simulations/particles/doc/particles.pdf
* Rendering:
* https://developer.download.nvidia.com/presentations/2010/gdc/Direct3D_Effects.pdf
* https://cg.informatik.uni-freiburg.de/publications/2012_CGI_sprayFoamBubbles.pdf
