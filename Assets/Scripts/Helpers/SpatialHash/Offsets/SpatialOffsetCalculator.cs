using Seb.Helpers;
using UnityEngine;

namespace Seb.Helpers.Internal
{
	public class SpatialOffsetCalculator
	{
		// Instantiated so that each calculator (one per simulation instance) gets its own independent
		// kernel/buffer bindings instead of sharing state through the cached Resources-loaded asset.
		readonly ComputeShader cs = Object.Instantiate(ComputeHelper.LoadComputeShader("SpatialOffsets"));
		static readonly int NumInputs = Shader.PropertyToID("numInputs");
		static readonly int Offsets = Shader.PropertyToID("Offsets");
		static readonly int SortedKeys = Shader.PropertyToID("SortedKeys");

		const int initKernel = 0;
		const int offsetsKernel = 1;


		// needsInit: Set to true if offsets buffer has not already been initialized with values >= its length.
		public void Run(bool needsInit, ComputeBuffer sortedKeys, ComputeBuffer offsets)
		{
			if (sortedKeys.count != offsets.count) throw new System.Exception("Count mismatch");
			cs.SetInt(NumInputs, sortedKeys.count);

			if (needsInit)
			{
				cs.SetBuffer(initKernel, Offsets, offsets);
				ComputeHelper.Dispatch(cs, sortedKeys.count, kernelIndex: initKernel);
			}

			cs.SetBuffer(offsetsKernel, Offsets, offsets);
			cs.SetBuffer(offsetsKernel, SortedKeys, sortedKeys);
			ComputeHelper.Dispatch(cs, sortedKeys.count, kernelIndex: offsetsKernel);
		}

		public void Release()
		{
			Object.Destroy(cs);
		}
	}
}