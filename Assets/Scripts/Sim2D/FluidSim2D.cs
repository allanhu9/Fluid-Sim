using UnityEngine;

namespace Seb.Fluid2D.Simulation
{
	public class FluidSim2D : FluidSim2DBase
	{
		[Header("Collision Settings")]
		[Range(0, 1)] public float collisionDamping = 0.95f;
		public Vector2 boundsSize;
		public Vector2 boundsCentre;
		public Vector2 obstacleSize;
		public Vector2 obstacleCentre;

		[Header("Spawner")]
		public Spawner2D spawner2D;

		protected override Spawner2D.ParticleSpawnData CreateSpawnData()
		{
			if (spawner2D == null) throw new System.InvalidOperationException("A particle spawner must be assigned.");
			return spawner2D.GetSpawnData();
		}

		protected override void SetAdditionalSettings()
		{
			compute.SetFloat("collisionDamping", collisionDamping);
			compute.SetVector("boundsSize", boundsSize);
			compute.SetVector("boundsCentre", boundsCentre);
			compute.SetVector("obstacleSize", obstacleSize);
			compute.SetVector("obstacleCentre", obstacleCentre);
		}

		protected override void DrawSimulationGizmos()
		{
			Gizmos.color = new Color(0, 1, 0, 0.4f);
			Gizmos.DrawWireCube(boundsCentre, boundsSize);
			Gizmos.DrawWireCube(obstacleCentre, obstacleSize);
		}
	}
}
