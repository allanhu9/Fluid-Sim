using Seb.Helpers;
using UnityEngine;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{

	public class ParticleDisplay3D : MonoBehaviour
	{
		public enum DisplayMode
		{
			None,
			Shaded3D,
			Billboard
		}

		[Header("Settings")] public DisplayMode mode;
		public float scale;
		public Gradient colourMap;
		public int gradientResolution;
		public float velocityDisplayMax;
		public int meshResolution;

		[Header("Boundary Particle Settings")] public bool showBoundaryParticles = true;
		public float boundaryScale = 1;
		[ColorUsage(false)] public Color boundaryColour = Color.magenta;

		[Header("References")] public FluidSim sim;
		public Shader shaderShaded;
		public Shader shaderBillboard;
		public Shader shaderBoundaryBillboard;

		Mesh mesh;
		Material mat;
		ComputeBuffer argsBuffer;
		Texture2D gradientTexture;
		DisplayMode modeOld;
		bool needsUpdate;

		Mesh boundaryMesh;
		Material boundaryMat;
		ComputeBuffer boundaryArgsBuffer;

		void LateUpdate()
		{
			UpdateSettings();

			if (mode != DisplayMode.None)
			{
				Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
				Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, bounds, argsBuffer);
			}

			// Boundary/wall particles are always rendered as flat-coloured billboards,
			// independently of whichever display mode the fluid itself is using.
			if (showBoundaryParticles && boundaryMat != null && sim.numBoundaryParticles > 0)
			{
				Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
				Graphics.DrawMeshInstancedIndirect(boundaryMesh, 0, boundaryMat, bounds, boundaryArgsBuffer);
			}
		}

		void UpdateSettings()
		{
			if (modeOld != mode)
			{
				modeOld = mode;
				if (mode != DisplayMode.None)
				{
					if (mode == DisplayMode.Billboard) mesh = QuadGenerator.GenerateQuadMesh();
					else mesh = SphereGenerator.GenerateSphereMesh(meshResolution);
					ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.positionBuffer.count);

					mat = mode switch
					{
						DisplayMode.Shaded3D => new Material(shaderShaded),
						DisplayMode.Billboard => new Material(shaderBillboard),
						_ => null
					};


					mat.SetBuffer("Positions", sim.positionBuffer);
					mat.SetBuffer("Velocities", sim.velocityBuffer);
					mat.SetBuffer("ParticleTypes", sim.particleTypeBuffer);
					mat.SetBuffer("DebugBuffer", sim.debugBuffer);
				}
			}

			if (mat != null)
			{
				if (needsUpdate)
				{
					needsUpdate = false;
					TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
					mat.SetTexture("ColourMap", gradientTexture);
				}

				mat.SetFloat("scale", scale * 0.01f);
				mat.SetFloat("velocityMax", velocityDisplayMax);

				Vector3 s = transform.localScale;
				transform.localScale = Vector3.one;
				var localToWorld = transform.localToWorldMatrix;
				transform.localScale = s;

				mat.SetMatrix("localToWorld", localToWorld);
			}

			UpdateBoundarySettings();
		}

		void UpdateBoundarySettings()
		{
			if (!showBoundaryParticles || sim.numBoundaryParticles <= 0) return;

			if (boundaryMat == null)
			{
				boundaryMesh = QuadGenerator.GenerateQuadMesh();
				boundaryMat = new Material(shaderBoundaryBillboard);
				boundaryMat.SetBuffer("Positions", sim.positionBuffer);
				boundaryMat.SetBuffer("ParticleTypes", sim.particleTypeBuffer);
				ComputeHelper.CreateArgsBuffer(ref boundaryArgsBuffer, boundaryMesh, sim.positionBuffer.count);
			}

			boundaryMat.SetFloat("scale", boundaryScale * 0.01f);
			boundaryMat.SetColor("boundaryColour", boundaryColour);
		}

		public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
		{
			if (texture == null)
			{
				texture = new Texture2D(width, 1);
			}
			else if (texture.width != width)
			{
				texture.Reinitialize(width, 1);
			}

			if (gradient == null)
			{
				gradient = new Gradient();
				gradient.SetKeys(
					new GradientColorKey[] { new(Color.black, 0), new(Color.black, 1) },
					new GradientAlphaKey[] { new(1, 0), new(1, 1) }
				);
			}

			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;

			Color[] cols = new Color[width];
			for (int i = 0; i < cols.Length; i++)
			{
				float t = i / (cols.Length - 1f);
				cols[i] = gradient.Evaluate(t);
			}

			texture.SetPixels(cols);
			texture.Apply();
		}

		private void OnValidate()
		{
			needsUpdate = true;
		}

		void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer);
			ComputeHelper.Release(boundaryArgsBuffer);
		}
	}
}