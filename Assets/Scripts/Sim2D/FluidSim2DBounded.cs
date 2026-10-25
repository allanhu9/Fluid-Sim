using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
	public class FluidSim2DBounded : FluidSim2DBase
	{
		[Header("Boundary Repulsion")]
		[Min(0)] public float boundaryRepulsionStrength = 1;
		[Min(0)] public float boundaryRepulsionRadius;
		public float boundaryRepulsionExponent1 = 12;
		public float boundaryRepulsionExponent2 = 6;
		[Range(0.001f, 1)] public float boundaryRepulsionMinDistanceFraction = 0.1f;
		[Min(0.0001f)] public float boundaryRepulsionMaxForce = 1000;

		[Header("Spawner")]
		public Spawner2DBounded spawner2D;

		public int numFluidParticles { get; private set; }
		public int numBoundaryParticles { get; private set; }

		float activeSmoothingRadius;
		float activeParticleMass;
		float activeTargetDensity;
		float activeBoundarySpacing;
		Vector2 activeBoundsCentre;

		protected override bool RequiresBoundaryParticles => true;
		protected override bool RegenerateOnReset => true;
		protected override float SimulationSmoothingRadius => activeSmoothingRadius;
		protected override float SimulationParticleMass => activeParticleMass;
		protected override float SimulationTargetDensity => activeTargetDensity;

		protected override Spawner2D.ParticleSpawnData CreateSpawnData()
		{
			if (spawner2D == null) throw new System.InvalidOperationException("A bounded particle spawner must be assigned.");
			Spawner2D.ParticleSpawnData data = spawner2D.GetSpawnData(smoothingRadius, particleMass, targetDensity);
			activeSmoothingRadius = smoothingRadius;
			activeParticleMass = particleMass;
			activeTargetDensity = targetDensity;
			activeBoundarySpacing = (float)System.Math.Sqrt((double)particleMass / targetDensity);
			activeBoundsCentre = spawner2D.boundsCentre;
			numBoundaryParticles = spawner2D.boundaryParticleCount;
			numFluidParticles = data.positions.Length - numBoundaryParticles;
			return data;
		}

		protected override void SetAdditionalSettings()
		{
			if (!IsFinite(boundaryRepulsionStrength) || boundaryRepulsionStrength < 0 ||
				!IsFinite(boundaryRepulsionRadius) || boundaryRepulsionRadius < 0 ||
				!IsFinite(boundaryRepulsionExponent1) || !IsFinite(boundaryRepulsionExponent2) ||
				boundaryRepulsionExponent2 <= 0 || boundaryRepulsionExponent1 <= boundaryRepulsionExponent2 ||
				!IsFinite(boundaryRepulsionMinDistanceFraction) || boundaryRepulsionMinDistanceFraction <= 0 || boundaryRepulsionMinDistanceFraction > 1 ||
				!IsFinite(boundaryRepulsionMaxForce) || boundaryRepulsionMaxForce <= 0)
			{
				throw new System.ArgumentException("Boundary repulsion requires finite D >= 0, radius >= 0 (0 uses spacing), p1 > p2 > 0, minimum-distance fraction in (0, 1], and maximum force > 0.");
			}

			float repulsionRadius = boundaryRepulsionRadius > 0 ? boundaryRepulsionRadius : activeBoundarySpacing;
			if (boundaryRepulsionStrength > 0 && repulsionRadius > activeSmoothingRadius)
			{
				throw new System.ArgumentOutOfRangeException(nameof(boundaryRepulsionRadius),
					"Effective boundary repulsion radius (including automatic boundary spacing) must not exceed the active smoothing radius while repulsion is enabled.");
			}

			compute.SetFloat("boundaryRepulsionStrength", boundaryRepulsionStrength);
			compute.SetFloat("boundaryRepulsionRadius", repulsionRadius);
			compute.SetFloat("boundaryRepulsionExponent1", boundaryRepulsionExponent1);
			compute.SetFloat("boundaryRepulsionExponent2", boundaryRepulsionExponent2);
			compute.SetFloat("boundaryRepulsionMinDistanceFraction", boundaryRepulsionMinDistanceFraction);
			compute.SetFloat("boundaryRepulsionMaxForce", boundaryRepulsionMaxForce);
			compute.SetVector("boundaryRepulsionCentre", activeBoundsCentre);
		}

		static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

		void Reset()
		{
			iterationsPerFrame = 3;
			gravity = -12;
			smoothingRadius = 0.35f;
			targetDensity = 55;
			pressureMultiplier = 500;
			nearPressureMultiplier = 2;
			interactionRadius = 2;
			interactionStrength = 10;
		}
	}
}
