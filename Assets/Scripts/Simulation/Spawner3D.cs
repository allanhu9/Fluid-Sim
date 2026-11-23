using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid.Simulation
{

	public class Spawner3D : MonoBehaviour
	{
		public int particleSpawnDensity = 600;
		public float3 initialVel;
		public float jitterStrength;
		public bool showSpawnBounds;
		public SpawnRegion[] spawnRegions;

		[Header("Boundary Debug Info")] public bool showBoundaryGizmos = true;
		public int boundaryParticleCount;

		[Header("Debug Info")] public int debug_num_particles;
		public float debug_spawn_volume;

		BoundaryLayout? activeLayout;

		public SpawnData GetSpawnData()
		{
			List<float3> allPoints = new();
			List<float3> allVelocities = new();

			foreach (SpawnRegion region in spawnRegions)
			{
				int particlesPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
				(float3[] points, float3[] velocities) = SpawnCube(particlesPerAxis, region.centre, Vector3.one * region.size);
				allPoints.AddRange(points);
				allVelocities.AddRange(velocities);
			}

			return new SpawnData() { points = allPoints.ToArray(), velocities = allVelocities.ToArray() };
		}

		// Appends a layered shell of fixed boundary particles around boundsCentre/boundsSize to the
		// fluid points generated above, so that the fluid is contained by particles rather than a box collider.
		public SpawnData GetSpawnDataWithBoundary(float smoothingRadius, float particleMass, float restDensity, float boundaryLayerThicknessFactor, Vector3 boundsCentre, Vector3 boundsSize)
		{
			BoundaryLayout layout = CalculateBoundaryLayout(boundsCentre, boundsSize, smoothingRadius, particleMass, restDensity, boundaryLayerThicknessFactor);

			SpawnData fluid = GetSpawnData();
			int fluidCount = fluid.points.Length;
			int totalCount = checked(fluidCount + layout.particleCount);
			SpawnData data = new()
			{
				points = new float3[totalCount],
				velocities = new float3[totalCount],
				particleTypes = new uint[totalCount]
			};
			Array.Copy(fluid.points, data.points, fluidCount);
			Array.Copy(fluid.velocities, data.velocities, fluidCount);

			int outerWidth = checked(layout.innerCellCount.x + 2 * layout.layerCount);
			int outerHeight = checked(layout.innerCellCount.y + 2 * layout.layerCount);
			int outerDepth = checked(layout.innerCellCount.z + 2 * layout.layerCount);
			int index = fluidCount;
			for (int z = 0; z < outerDepth; z++)
			{
				for (int y = 0; y < outerHeight; y++)
				{
					for (int x = 0; x < outerWidth; x++)
					{
						bool insideX = x >= layout.layerCount && x < outerWidth - layout.layerCount;
						bool insideY = y >= layout.layerCount && y < outerHeight - layout.layerCount;
						bool insideZ = z >= layout.layerCount && z < outerDepth - layout.layerCount;
						if (insideX && insideY && insideZ) continue;

						data.points[index] = (float3)layout.centre + new float3(
							(x + 0.5f - outerWidth * 0.5f) * layout.spacing,
							(y + 0.5f - outerHeight * 0.5f) * layout.spacing,
							(z + 0.5f - outerDepth * 0.5f) * layout.spacing);
						data.particleTypes[index] = 1;
						index++;
					}
				}
			}

			activeLayout = layout;
			boundaryParticleCount = layout.particleCount;
			return data;
		}

		public static BoundaryLayout CalculateBoundaryLayout(Vector3 centre, Vector3 size, float smoothingRadius, float particleMass, float restDensity, float boundaryLayerThicknessFactor)
		{
			RequirePositive(size.x, "Bounds width");
			RequirePositive(size.y, "Bounds height");
			RequirePositive(size.z, "Bounds depth");
			RequirePositive(smoothingRadius, nameof(smoothingRadius));
			RequirePositive(particleMass, nameof(particleMass));
			RequirePositive(restDensity, nameof(restDensity));
			RequirePositive(boundaryLayerThicknessFactor, nameof(boundaryLayerThicknessFactor));
			if (!IsFinite(centre.x) || !IsFinite(centre.y) || !IsFinite(centre.z))
				throw new ArgumentOutOfRangeException(nameof(centre), "Bounds centre must be finite.");

			float spacing = (float)Math.Cbrt((double)particleMass / restDensity);
			RequirePositive(spacing, "Boundary spacing");
			int width = checked((int)Math.Ceiling((double)size.x / spacing));
			int height = checked((int)Math.Ceiling((double)size.y / spacing));
			int depth = checked((int)Math.Ceiling((double)size.z / spacing));
			int layers = checked((int)Math.Ceiling((double)(boundaryLayerThicknessFactor * smoothingRadius) / spacing));
			int outerWidth = checked(width + 2 * layers);
			int outerHeight = checked(height + 2 * layers);
			int outerDepth = checked(depth + 2 * layers);
			int count = checked((int)((long)outerWidth * outerHeight * outerDepth - (long)width * height * depth));
			return new BoundaryLayout(centre, spacing, new Vector3Int(width, height, depth), layers, count);
		}

		static void RequirePositive(float value, string name)
		{
			if (!(value > 0) || !IsFinite(value))
				throw new ArgumentOutOfRangeException(name, "Value must be finite and greater than zero.");
		}

		static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

		public readonly struct BoundaryLayout
		{
			public readonly Vector3 centre;
			public readonly float spacing;
			public readonly Vector3Int innerCellCount;
			public readonly int layerCount;
			public readonly int particleCount;
			public Vector3 innerSize => new(innerCellCount.x * spacing, innerCellCount.y * spacing, innerCellCount.z * spacing);
			public float depth => layerCount * spacing;
			public Vector3 outerSize => innerSize + Vector3.one * (2 * depth);

			public BoundaryLayout(Vector3 centre, float spacing, Vector3Int innerCellCount, int layerCount, int particleCount)
			{
				this.centre = centre;
				this.spacing = spacing;
				this.innerCellCount = innerCellCount;
				this.layerCount = layerCount;
				this.particleCount = particleCount;
			}
		}

		(float3[] p, float3[] v) SpawnCube(int numPerAxis, Vector3 centre, Vector3 size)
		{
			int numPoints = numPerAxis * numPerAxis * numPerAxis;
			float3[] points = new float3[numPoints];
			float3[] velocities = new float3[numPoints];

			int i = 0;

			for (int x = 0; x < numPerAxis; x++)
			{
				for (int y = 0; y < numPerAxis; y++)
				{
					for (int z = 0; z < numPerAxis; z++)
					{
						float tx = x / (numPerAxis - 1f);
						float ty = y / (numPerAxis - 1f);
						float tz = z / (numPerAxis - 1f);

						float px = (tx - 0.5f) * size.x + centre.x;
						float py = (ty - 0.5f) * size.y + centre.y;
						float pz = (tz - 0.5f) * size.z + centre.z;
						float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
						points[i] = new float3(px, py, pz) + jitter;
						velocities[i] = initialVel;
						i++;
					}
				}
			}

			return (points, velocities);
		}



		void OnValidate()
		{
			debug_spawn_volume = 0;
			debug_num_particles = 0;

			if (spawnRegions != null)
			{
				foreach (SpawnRegion region in spawnRegions)
				{
					debug_spawn_volume += region.Volume;
					int numPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
					debug_num_particles += numPerAxis * numPerAxis * numPerAxis;
				}
			}
		}

		void OnDrawGizmos()
		{
			if (showSpawnBounds && !Application.isPlaying)
			{
				foreach (SpawnRegion region in spawnRegions)
				{
					Gizmos.color = region.debugDisplayCol;
					Gizmos.DrawWireCube(region.centre, Vector3.one * region.size);
				}
			}

			if (showBoundaryGizmos && activeLayout.HasValue)
			{
				BoundaryLayout layout = activeLayout.Value;
				Gizmos.color = Color.cyan;
				Gizmos.DrawWireCube(layout.centre, layout.innerSize);
				Gizmos.color = Color.yellow;
				Gizmos.DrawWireCube(layout.centre, layout.outerSize);
			}
		}

		[System.Serializable]
		public struct SpawnRegion
		{
			public Vector3 centre;
			public float size;
			public Color debugDisplayCol;

			public float Volume => size * size * size;

			public int CalculateParticleCountPerAxis(int particleDensity)
			{
				int targetParticleCount = (int)(Volume * particleDensity);
				int particlesPerAxis = (int)Math.Cbrt(targetParticleCount);
				return particlesPerAxis;
			}
		}

		public struct SpawnData
		{
			public float3[] points;
			public float3[] velocities;
			public uint[] particleTypes; // null unless boundary particles were spawned; 0 = fluid, 1 = fixed boundary
		}
	}
}