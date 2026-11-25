static const float PI = 3.1415926;

const float K_SpikyPow2;
const float K_SpikyPow3;
const float K_SpikyPow2Grad;
const float K_SpikyPow3Grad;

float LinearKernel(float dst, float radius)
{
	if (dst < radius)
    {
        return 1 - dst / radius;
    }
    return 0;
}

// Muller et al, 2003 poly6 smoothing kernel, used for everything but pressure and viscosity
float SmoothingKernelPoly6(float dst, float radius)
{
	if (dst < radius)
	{
		float scale = 315 / (64 * PI * pow(abs(radius), 9));
		float v = radius * radius - dst * dst;
		return v * v * v * scale;
	}
	return 0;
}

// Muller et al, 2003 Viscosity kernel (States that it greatly increased stability of simulation - may consider other options.)
float ViscosityKernel(float dst, float radius)
{
    if (dst < radius)
    {
        float h = abs(radius);
        float h2 = h * h;
        float h3 = h2 * h;
        float r = dst;
        float r2 = r * r;
        float r3 = r2 * r;
        float scale = 15 / (2 * PI * h3);
        float v = -r3/(2*h3) + r2/h2 + h/(2*r) - 1;
        return v * scale;
    }
    return 0;
}

// Muller et al, 2003 Laplacian of viscosity kernel
float LaplacianViscosityKernel(float dst, float radius)
{
    float scale = 45/(PI * pow(abs(radius), 6));
    return scale * (radius - dst);
}

float SpikyKernelPow3(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius - dst;
		return v * v * v * K_SpikyPow3;
	}
	return 0;
}

//Integrate[(h-r)^2 r^2 Sin[θ], {r, 0, h}, {θ, 0, π}, {φ, 0, 2*π}]
float SpikyKernelPow2(float dst, float radius)
{
	if (dst < radius)
	{
		float v = radius - dst;
		return v * v * K_SpikyPow2;
	}
	return 0;
}

float DerivativeSpikyPow3(float dst, float radius)
{
	if (dst <= radius)
	{
		float v = radius - dst;
		return -v * v * K_SpikyPow3Grad;
	}
	return 0;
}

float DerivativeSpikyPow2(float dst, float radius)
{
	if (dst <= radius)
	{
		float v = radius - dst;
		return -v * K_SpikyPow2Grad;
	}
	return 0;
}

float DensityKernel(float dst, float radius)
{
	//return SmoothingKernelPoly6(dst, radius);
	return SpikyKernelPow2(dst, radius);
}

float NearDensityKernel(float dst, float radius)
{
	return SpikyKernelPow3(dst, radius);
}

float DensityDerivative(float dst, float radius)
{
	return DerivativeSpikyPow2(dst, radius);
}

float NearDensityDerivative(float dst, float radius)
{
	return DerivativeSpikyPow3(dst, radius);
}

