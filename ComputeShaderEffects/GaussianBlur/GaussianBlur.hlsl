#define threads_x 16
#define threads_y 16

cbuffer Consts : register(c0)
{
	int2 tileExtent;
	int weightLength;
	int radius;
	int2 regionOffset;
	int regionWidth;
	int padding2;
};

Texture2D<float4> Img : register(t0);
StructuredBuffer<float> Weights : register(t1);
RWStructuredBuffer<uint> BufferOut : register(u0);

SamplerState g_SampTex
{
	AddressU = Clamp;
	AddressV = Clamp;
	Filter = MIN_MAG_MIP_POINT;
};

// Conversion functions
inline uint Float4ToUint( float4 ui )
{
	uint4 uc = round(ui * 255);
	uc.g = uc.g << 8;
	uc.r = uc.r << 16;
	uc.a = uc.a << 24;
	return dot(uc, 1);	// Add all components
}


[numthreads(threads_x, threads_y, 1)]
void CSMain( uint3 DTid : SV_DispatchThreadID )
{
	if (DTid.x > (uint)(regionWidth - 1))
	{
		return;
	}

	float2 pixelPoint = DTid.xy + regionOffset;
	float4 c = 0;
	float totalUsedWeight = 0;
	uint localWeightLength = weightLength;
	uint startIndex = 0;

#if VERT
	pixelPoint.y -= radius;
#else
	pixelPoint.x -= radius;
#endif

#if !CLAMP
#if VERT
	if (pixelPoint.y + localWeightLength > tileExtent.y)
	{
		localWeightLength = tileExtent.y - pixelPoint.y;
	}
	if (pixelPoint.y < 0)
	{
		startIndex = abs(pixelPoint.y);
		pixelPoint.y = 0;
	}
#else
	if (pixelPoint.x + localWeightLength > tileExtent.x)
	{
		localWeightLength = tileExtent.x - pixelPoint.x;
	}
	if (pixelPoint.x < 0)
	{
		startIndex = abs(pixelPoint.x);
		pixelPoint.x = 0;
	}
#endif
#endif

	pixelPoint += 0.5;	// Texel offset

	for (uint weightIndex = startIndex; weightIndex < localWeightLength; weightIndex++)
	{
		c += Img.SampleLevel(g_SampTex, pixelPoint / tileExtent, 0) * 
				Weights[weightIndex];

#if !CLAMP
		totalUsedWeight += Weights[weightIndex];
#endif

#if VERT
		pixelPoint.y++;
#else
		pixelPoint.x++;
#endif
	}

#if !CLAMP
	// Average the remaining weights
	c /= totalUsedWeight;
#endif

	BufferOut[DTid.y * regionWidth + DTid.x] = Float4ToUint(c);
}
