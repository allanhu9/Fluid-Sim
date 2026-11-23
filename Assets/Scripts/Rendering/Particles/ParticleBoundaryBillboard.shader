// Camera-facing billboard used to draw the fixed boundary/wall particles of a bounded
// simulation with a single flat, user-settable colour (independent of the fluid's own
// display mode / velocity-based colour gradient).
Shader "Fluid/ParticleBoundaryBillboard" {
	Properties {

	}
	SubShader {

		Tags {"Queue"="Geometry" }

		Pass {

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.5

			#include "UnityCG.cginc"

			StructuredBuffer<float3> Positions;
			// Particles are re-sorted every step, so boundary samples are identified by type rather than index range.
			StructuredBuffer<uint> ParticleTypes;

			float scale;
			float3 boundaryColour;

			struct v2f
			{
				float4 pos : SV_POSITION;
			};

			v2f vert (appdata_full v, uint instanceID : SV_InstanceID)
			{
				v2f o;

				float3 centreWorld = Positions[instanceID];
				float3 objectVertPos = v.vertex * scale * 2;
				float4 viewPos = mul(UNITY_MATRIX_V, float4(centreWorld, 1)) + float4(objectVertPos, 0);
				o.pos = mul(UNITY_MATRIX_P, viewPos);
				// Collapse fluid instances to a degenerate point so they are culled
				if (ParticleTypes[instanceID] != 1) o.pos = float4(0, 0, 0, 0);

				return o;
			}

			float4 frag (v2f i) : SV_Target
			{
				return float4(boundaryColour, 1);
			}

			ENDCG
		}
	}
}
