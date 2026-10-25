using System;
using Unity.Mathematics;
using UnityEngine;

public class Spawner2DBounded : Spawner2D
{
	[Header("Boundary Settings")]
	public Vector2 boundsCentre;
	public Vector2 boundsSize = new(10, 6);
	public bool showBoundaryGizmos = true;

	[Header("Boundary Debug Info")]
	public int boundaryParticleCount;

	BoundaryLayout? activeLayout;

	public ParticleSpawnData GetSpawnData(float smoothingRadius, float particleMass, float restDensity)
	{
		BoundaryLayout layout = CalculateBoundaryLayout(boundsCentre, boundsSize, smoothingRadius, particleMass, restDensity);
		if (spawnRegions == null) throw new InvalidOperationException("Spawn regions must not be null.");
		if (spawnRegions.Length > 0)
		{
			RequirePositive(spawnDensity, nameof(spawnDensity));
			if (!IsFinite(initialVelocity.x) || !IsFinite(initialVelocity.y) || !IsFinite(jitterStr))
				throw new InvalidOperationException("Initial velocity and jitter must be finite.");
			foreach (SpawnRegion region in spawnRegions)
			{
				RequirePositive(region.size.x, "Spawn region width");
				RequirePositive(region.size.y, "Spawn region height");
				if (!IsFinite(region.position.x) || !IsFinite(region.position.y))
					throw new InvalidOperationException("Spawn region positions must be finite.");
			}
		}

		ParticleSpawnData fluid = base.GetSpawnData();
		int fluidCount = fluid.positions.Length;
		int totalCount = checked(fluidCount + layout.particleCount);
		ParticleSpawnData data = new(totalCount)
		{
			particleTypes = new uint[totalCount]
		};
		Array.Copy(fluid.positions, data.positions, fluidCount);
		Array.Copy(fluid.velocities, data.velocities, fluidCount);
		Array.Copy(fluid.spawnIndices, data.spawnIndices, fluidCount);

		int outerWidth = checked(layout.innerCellCount.x + 2 * layout.layerCount);
		int outerHeight = checked(layout.innerCellCount.y + 2 * layout.layerCount);
		int index = fluidCount;
		for (int y = 0; y < outerHeight; y++)
		{
			for (int x = 0; x < outerWidth; x++)
			{
				bool insideX = x >= layout.layerCount && x < outerWidth - layout.layerCount;
				bool insideY = y >= layout.layerCount && y < outerHeight - layout.layerCount;
				if (insideX && insideY) continue;

				data.positions[index] = (float2)layout.centre + new float2(
					(x + 0.5f - outerWidth * 0.5f) * layout.spacing,
					(y + 0.5f - outerHeight * 0.5f) * layout.spacing);
				data.particleTypes[index] = 1;
				data.spawnIndices[index] = -1;
				index++;
			}
		}

		activeLayout = layout;
		boundaryParticleCount = layout.particleCount;
		spawnParticleCount = totalCount;
		return data;
	}

	public static BoundaryLayout CalculateBoundaryLayout(Vector2 centre, Vector2 size, float smoothingRadius, float particleMass, float restDensity)
	{
		RequirePositive(size.x, "Bounds width");
		RequirePositive(size.y, "Bounds height");
		RequirePositive(smoothingRadius, nameof(smoothingRadius));
		RequirePositive(particleMass, nameof(particleMass));
		RequirePositive(restDensity, nameof(restDensity));
		if (!IsFinite(centre.x) || !IsFinite(centre.y))
			throw new ArgumentOutOfRangeException(nameof(centre), "Bounds centre must be finite.");

		float spacing = (float)Math.Sqrt((double)particleMass / restDensity);
		RequirePositive(spacing, "Boundary spacing");
		int width = checked((int)Math.Ceiling((double)size.x / spacing));
		int height = checked((int)Math.Ceiling((double)size.y / spacing));
		int layers = checked((int)Math.Ceiling((double)(3*smoothingRadius) / spacing));
		int outerWidth = checked(width + 2 * layers);
		int outerHeight = checked(height + 2 * layers);
		int count = checked((int)((long)outerWidth * outerHeight - (long)width * height));
		return new BoundaryLayout(centre, spacing, new Vector2Int(width, height), layers, count);
	}

	static void RequirePositive(float value, string name)
	{
		if (!(value > 0) || !IsFinite(value))
			throw new ArgumentOutOfRangeException(name, "Value must be finite and greater than zero.");
	}

	static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

	public readonly struct BoundaryLayout
	{
		public readonly Vector2 centre;
		public readonly float spacing;
		public readonly Vector2Int innerCellCount;
		public readonly int layerCount;
		public readonly int particleCount;
		public Vector2 innerSize => new(innerCellCount.x * spacing, innerCellCount.y * spacing);
		public float depth => layerCount * spacing;
		public Vector2 outerSize => innerSize + Vector2.one * (2 * depth);

		public BoundaryLayout(Vector2 centre, float spacing, Vector2Int innerCellCount, int layerCount, int particleCount)
		{
			this.centre = centre;
			this.spacing = spacing;
			this.innerCellCount = innerCellCount;
			this.layerCount = layerCount;
			this.particleCount = particleCount;
		}
	}

	void Reset()
	{
		spawnDensity = 55;
		spawnRegions = new[]
		{
			new SpawnRegion { size = new Vector2(4, 3), debugCol = Color.cyan }
		};
		showSpawnBoundsGizmos = true;
	}

	protected override void OnDrawGizmos()
	{
		base.OnDrawGizmos();
		if (!showBoundaryGizmos) return;

		Gizmos.color = new Color(0, 1, 0, 0.4f);
		Gizmos.DrawWireCube(boundsCentre, boundsSize);
		if (activeLayout.HasValue)
		{
			BoundaryLayout layout = activeLayout.Value;
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireCube(layout.centre, layout.innerSize);
			Gizmos.color = Color.yellow;
			Gizmos.DrawWireCube(layout.centre, layout.outerSize);
		}
	}
}
