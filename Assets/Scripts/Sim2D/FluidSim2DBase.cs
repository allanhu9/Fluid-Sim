using Seb.Helpers;
using UnityEngine;
using Unity.Mathematics;

namespace Seb.Fluid2D.Simulation
{
	public abstract class FluidSim2DBase : MonoBehaviour
	{
		public event System.Action SimulationStepCompleted;

		[Header("Simulation Settings")]
		public float timeScale = 1;
		public float maxTimestepFPS = 60; // if time-step dips lower than this fps, simulation will run slower (set to 0 to disable)
		public int iterationsPerFrame;
		public float gravity;
		public float smoothingRadius = 2;
		[Min(0.0001f)] public float particleMass = 1;
		public float targetDensity;
		public float pressureMultiplier;
		public float nearPressureMultiplier;
		public float viscosityStrength;
		public float cohesionStrength; // alpha: Akinci et al, 2012 surface tension (cohesion + curvature) strength

		[Header("Interaction Settings")]
		public float interactionRadius;

		public float interactionStrength;

		[Header("References")]
		public ComputeShader compute;

		// Buffers
		public ComputeBuffer positionBuffer { get; private set; }
		public ComputeBuffer velocityBuffer { get; private set; }
		public ComputeBuffer densityBuffer { get; private set; }
		public ComputeBuffer surfaceNormalBuffer { get; private set; }
		public ComputeBuffer particleTypeBuffer { get; private set; }

		ComputeBuffer sortTarget_Position;
		ComputeBuffer sortTarget_PredicitedPosition;
		ComputeBuffer sortTarget_Velocity;
		ComputeBuffer sortTarget_ParticleType;

		ComputeBuffer predictedPositionBuffer;
		SpatialHash spatialHash;

		// Kernel IDs
		const int externalForcesKernel = 0;
		const int spatialHashKernel = 1;
		const int reorderKernel = 2;
		const int copybackKernel = 3;
		const int densityKernel = 4;
		const int pressureKernel = 5;
		const int viscosityKernel = 6;
		const int updatePositionKernel = 7;
		int initializeParticleTypesKernel = -1;
		int surfaceNormalsKernel = -1;
		int surfaceTensionKernel = -1;

		// State
		bool isPaused;
		Spawner2D.ParticleSpawnData spawnData;
		bool pauseNextFrame;
		bool ownsCompute;

		public int numParticles { get; private set; }

		protected abstract Spawner2D.ParticleSpawnData CreateSpawnData();
		protected virtual bool RequiresBoundaryParticles => false;
		protected virtual bool RegenerateOnReset => false;
		protected virtual float SimulationSmoothingRadius => smoothingRadius;
		protected virtual float SimulationParticleMass => particleMass;
		protected virtual float SimulationTargetDensity => targetDensity;
		protected virtual void SetAdditionalSettings() { }
		protected virtual void DrawSimulationGizmos() { }

		protected virtual void Start()
		{
			Debug.Log("Controls: Space = Play/Pause, R = Reset, LMB = Attract, RMB = Repel");

			Init();
		}

		void Init()
		{
			if (compute == null) throw new System.InvalidOperationException("A simulation compute shader must be assigned.");
			if (RequiresBoundaryParticles && !compute.HasKernel("InitializeParticleTypes"))
				throw new System.InvalidOperationException("The bounded simulation requires FluidSim2DWithBoundaryParticles.compute.");

			// Instantiate a unique copy of the compute shader so that buffer/uniform bindings
			// don't collide with other FluidSim2D instances sharing the same shader asset.
			compute = Instantiate(compute);
			ownsCompute = true;

			float deltaTime = 1 / 60f;
			Time.fixedDeltaTime = deltaTime;

			InitializeSimulation();
		}

		void InitializeSimulation()
		{
			Spawner2D.ParticleSpawnData initialData = CreateSpawnData();
			if (initialData.positions.Length == 0)
				throw new System.InvalidOperationException("The simulation requires at least one particle.");
			ReleaseBuffers();
			spawnData = initialData;
			numParticles = spawnData.positions.Length;
			spatialHash = new SpatialHash(numParticles);

			// Create buffers
			positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

			sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
			sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

			// This optional kernel identifies shaders that support sorted particle-type flags.
			if (compute.HasKernel("InitializeParticleTypes"))
			{
				initializeParticleTypesKernel = compute.FindKernel("InitializeParticleTypes");
				particleTypeBuffer = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
				sortTarget_ParticleType = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
				ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypes", initializeParticleTypesKernel, externalForcesKernel, densityKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
				ComputeHelper.SetBuffer(compute, sortTarget_ParticleType, "SortTarget_ParticleTypes", reorderKernel, copybackKernel);
			}

			// These optional kernels add Akinci et al, 2012 surface tension (cohesion + curvature);
			// only supported by compute shaders with boundary-particle support.
			if (compute.HasKernel("CalculateSurfaceTension"))
			{
				surfaceNormalsKernel = compute.FindKernel("CalculateSurfaceNormals");
				surfaceTensionKernel = compute.FindKernel("CalculateSurfaceTension");
				surfaceNormalBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

				ComputeHelper.SetBuffer(compute, surfaceNormalBuffer, "SurfaceNormals", surfaceNormalsKernel, surfaceTensionKernel);
				ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", surfaceNormalsKernel, surfaceTensionKernel);
				ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", surfaceNormalsKernel, surfaceTensionKernel);
				ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", surfaceTensionKernel);
				ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", surfaceNormalsKernel, surfaceTensionKernel);
				ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", surfaceNormalsKernel, surfaceTensionKernel);
				if (particleTypeBuffer != null)
				{
					ComputeHelper.SetBuffer(compute, particleTypeBuffer, "ParticleTypes", surfaceNormalsKernel, surfaceTensionKernel);
				}
			}

			// Init compute
			ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);

			ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel);
			ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
			ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);

			ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel, copybackKernel);
			ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel, copybackKernel);

			compute.SetInt("numParticles", numParticles);
			SetInitialBufferData(spawnData);
		}


		protected virtual void Update()
		{
			if (!isPaused)
			{
				float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity; // If framerate dips too low, run the simulation slower than real-time
				float dt = Mathf.Min(Time.deltaTime * timeScale, maxDeltaTime);
				RunSimulationFrame(dt);
			}

			if (pauseNextFrame)
			{
				isPaused = true;
				pauseNextFrame = false;
			}

			HandleInput();
		}

		void RunSimulationFrame(float frameTime)
		{
			float timeStep = frameTime / iterationsPerFrame;

			UpdateSettings(timeStep);

			for (int i = 0; i < iterationsPerFrame; i++)
			{
				RunSimulationStep();
				SimulationStepCompleted?.Invoke();
			}
		}

		void RunSimulationStep()
		{
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: externalForcesKernel);

			RunSpatial();

			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);
			if (surfaceTensionKernel >= 0 && cohesionStrength != 0)
			{
				// Normals must be computed for every particle (in their own pass) before the surface
				// tension pass, since each particle's curvature force reads its neighbours' normals.
				ComputeHelper.Dispatch(compute, numParticles, kernelIndex: surfaceNormalsKernel);
				ComputeHelper.Dispatch(compute, numParticles, kernelIndex: surfaceTensionKernel);
			}
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);
		}

		void RunSpatial()
		{
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);
			spatialHash.Run();

			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
		}

		void UpdateSettings(float deltaTime)
		{
			float mass = SimulationParticleMass;
			float radius = SimulationSmoothingRadius;
			float restDensity = SimulationTargetDensity;
			if (!(mass > 0) || float.IsInfinity(mass))
			{
				throw new System.ArgumentOutOfRangeException(nameof(particleMass), "Particle mass must be finite and greater than zero.");
			}
			if (particleTypeBuffer != null && (!(restDensity > 0) || float.IsInfinity(restDensity)))
			{
				throw new System.ArgumentOutOfRangeException(nameof(targetDensity), "Boundary rest density must be finite and greater than zero.");
			}

			compute.SetFloat("deltaTime", deltaTime);
			compute.SetFloat("gravity", gravity);
			compute.SetFloat("smoothingRadius", radius);
			// Legacy compute shaders ignore uniforms they do not declare.
			compute.SetFloat("particleMass", mass);
			compute.SetFloat("targetDensity", restDensity);
			compute.SetFloat("pressureMultiplier", pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength", viscosityStrength);
			compute.SetFloat("cohesionStrength", cohesionStrength);
			SetAdditionalSettings();

			compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(radius, 8)));
			compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(radius, 5)));
			compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(radius, 4)));
			compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(radius, 5) * Mathf.PI));
			compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(radius, 4) * Mathf.PI));

			// Mouse interaction settings:
			Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
			bool isPullInteraction = Input.GetMouseButton(0);
			bool isPushInteraction = Input.GetMouseButton(1);
			float currInteractStrength = 0;
			if (isPushInteraction || isPullInteraction)
			{
				currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;
			}

			compute.SetVector("interactionInputPoint", mousePos);
			compute.SetFloat("interactionInputStrength", currInteractStrength);
			compute.SetFloat("interactionInputRadius", interactionRadius);
		}

		void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
		{
			float2[] allPoints = new float2[spawnData.positions.Length]; //
			System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

			positionBuffer.SetData(allPoints);
			predictedPositionBuffer.SetData(allPoints);
			velocityBuffer.SetData(spawnData.velocities);
			if (spawnData.particleTypes != null)
			{
				particleTypeBuffer.SetData(spawnData.particleTypes);
			}
			else if (initializeParticleTypesKernel >= 0)
			{
				ComputeHelper.Dispatch(compute, numParticles, kernelIndex: initializeParticleTypesKernel);
			}
		}

		void HandleInput()
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				isPaused = !isPaused;
			}

			if (Input.GetKeyDown(KeyCode.RightArrow))
			{
				isPaused = false;
				pauseNextFrame = true;
			}

			if (Input.GetKeyDown(KeyCode.R))
			{
				ResetSimulation();
			}
		}

		public void ResetSimulation()
		{
			isPaused = true;
			pauseNextFrame = false;
			if (RegenerateOnReset)
			{
				InitializeSimulation();
				UpdateSettings(0);
				RunSpatial();
				ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
			}
			else
			{
				// Reset positions, the run single frame to get density etc (for debug purposes) and then reset positions again
				SetInitialBufferData(spawnData);
				RunSimulationStep();
				SetInitialBufferData(spawnData);
			}
		}


		void ReleaseBuffers()
		{
			ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer, surfaceNormalBuffer, particleTypeBuffer, sortTarget_Position, sortTarget_Velocity, sortTarget_PredicitedPosition, sortTarget_ParticleType);
			spatialHash?.Release();
			spatialHash = null;
			particleTypeBuffer = null;
			sortTarget_ParticleType = null;
			initializeParticleTypesKernel = -1;
			surfaceNormalBuffer = null;
			surfaceNormalsKernel = -1;
			surfaceTensionKernel = -1;
		}

		protected virtual void OnDestroy()
		{
			ReleaseBuffers();
			if (ownsCompute) Destroy(compute);
		}

		protected virtual void OnDrawGizmos()
		{
			DrawSimulationGizmos();

			if (Application.isPlaying)
			{
				Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
				bool isPullInteraction = Input.GetMouseButton(0);
				bool isPushInteraction = Input.GetMouseButton(1);
				bool isInteracting = isPullInteraction || isPushInteraction;
				if (isInteracting)
				{
					Gizmos.color = isPullInteraction ? Color.green : Color.red;
					Gizmos.DrawWireSphere(mousePos, interactionRadius);
				}
			}
		}
	}
}