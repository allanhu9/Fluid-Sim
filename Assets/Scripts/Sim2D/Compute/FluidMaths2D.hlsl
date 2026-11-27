static const float PI = 3.1415926;

const float Poly6ScalingFactor;
const float SpikyPow3ScalingFactor;
const float SpikyPow2ScalingFactor;
const float SpikyPow3DerivativeScalingFactor;
const float SpikyPow2DerivativeScalingFactor;

float SmoothingKernelPoly6(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius * radius - dst * dst;
		return v * v * v * Poly6ScalingFactor;
	}
	return 0;
}

float SpikyKernelPow3(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius - dst;
		return v * v * v * SpikyPow3ScalingFactor;
	}
	return 0;
}

float SpikyKernelPow2(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius - dst;
		return v * v * SpikyPow2ScalingFactor;
	}
	return 0;
}

float DerivativeSpikyPow3(float dst, float radius)
{
	if (dst <= radius)
	{
		float v = radius - dst;
		return -v * v * SpikyPow3DerivativeScalingFactor;
	}
	return 0;
}

float DerivativeSpikyPow2(float dst, float radius)
{
	if (dst <= radius)
	{
		float v = radius - dst;
		return -v * SpikyPow2DerivativeScalingFactor;
	}
	return 0;
}

// Muller et al, 2003 Laplacian of viscosity kernel
float LaplacianViscosityKernel(float dst, float radius)
{
    float scale = 45 / (PI * pow(abs(radius), 6));
    return scale * (radius - dst);
}

// Akinci et al, 2012 Cohesion kernel for surface tension. Support radius is split in two so that
// the kernel can turn negative (repulsive) at close range, preventing particle clustering.
float CohesionKernel(float dst, float radius)
{
    float scale = 32 / (PI * pow(abs(radius), 9));
    if (2 * dst > radius && dst <= radius)
    {
        float v = (radius - dst) * (radius - dst) * (radius - dst) * dst * dst * dst;
        return v * scale;
    }
    if (dst > 0 && 2 * dst <= radius)
    {
        float v = 2 * (radius - dst) * (radius - dst) * (radius - dst) * dst * dst * dst - pow(abs(radius), 6) / 64;
        return v * scale;
    }

    return 0;
}

// Derivative (w.r.t. dst) of the Akinci et al, 2012 cohesion kernel above. Mirrors the branches
// of CohesionKernel (the -radius^6/64 term in the second branch is constant w.r.t. dst, so it drops out).
float DerivativeCohesionKernel(float dst, float radius)
{
    float scale = 32 / (PI * pow(abs(radius), 9));
    if (2 * dst > radius && dst <= radius)
    {
        float v = 3 * dst * dst * (radius - dst) * (radius - dst) * (radius - 2 * dst);
        return v * scale;
    }
    if (dst > 0 && 2 * dst <= radius)
    {
        float v = 6 * dst * dst * (radius - dst) * (radius - dst) * (radius - 2 * dst);
        return v * scale;
    }

    return 0;
}
