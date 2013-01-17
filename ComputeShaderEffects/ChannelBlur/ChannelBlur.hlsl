#define threads_x 16
#define threads_y 16

cbuffer Consts : register(c0)
{
	int2 tileExtent;
	int2 regionOffset;
    int regionWidth;
    uint redWeightLength;
    uint greenWeightLength;
    uint blueWeightLength;
    uint alphaWeightLength;
    int maxRadius;
	int padding1;
	int padding2;
};

Texture2D<float4> Img : register(t0);
StructuredBuffer<float> RedWeights : register(t1);
StructuredBuffer<float> GreenWeights : register(t2);
StructuredBuffer<float> BlueWeights : register(t3);
StructuredBuffer<float> AlphaWeights : register(t4);
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
	uint4 uc = clamp(round(ui * 255), 0, 255);
	return uc.b + (uc.g << 8) + (uc.r << 16) + (uc.a << 24); 
}

[numthreads(threads_x, threads_y, 1)]
void CSMain( uint3 DTid : SV_DispatchThreadID )
{
	if (DTid.x > (uint)(regionWidth - 1))
	{
		return;
	}

	int2 pixelPoint = DTid.xy + regionOffset + 0.5;
	float4 sampleColor;
	float4 c = 0;
	float2 pt;
	int4 radius;
	int4 weightIndex = 0;
	float4 totalUsedWeight = 0;
	float3 totalAlphas = 0;
	bool inBounds;
	
	radius = int4(redWeightLength / 2, greenWeightLength / 2, blueWeightLength / 2, alphaWeightLength / 2);

	for (int i = -maxRadius; i <= maxRadius + 1; i++)
    {
		#if VERT
			// Vertical pass
			pt = int2(pixelPoint.x, pixelPoint.y + i);
		#else
			// Horizontal pass
			pt = int2(pixelPoint.x + i, pixelPoint.y);
		#endif

		#if !CLAMP
			#if VERT
				inBounds = (pt.y >= 0 && pt.y <= tileExtent.y);
			#else
				inBounds = (pt.x >= 0 && pt.x <= tileExtent.x);
			#endif
		#endif

		float4 sampleColor = Img.SampleLevel(g_SampTex, pt / tileExtent, 0);
		float4 blurredColor = 0;
		float3 alphas = 0;
		
		// Red Blur	
		if (-radius.r <= i && i <= radius.r)
		{
			#if !CLAMP
				if (inBounds)
				{
			#endif
			alphas.r = sampleColor.a * RedWeights[weightIndex.r];
			blurredColor.r = sampleColor.r * alphas.r;
			#if !CLAMP
					totalUsedWeight.r += RedWeights[weightIndex.r];
				}
			#endif
			weightIndex.r++;
		}

		// Green Blur	
		if (-radius.g <= i && i <= radius.g)
		{
			#if !CLAMP
				if (inBounds)
				{
			#endif
			alphas.g = sampleColor.a * GreenWeights[weightIndex.g];
			blurredColor.g = sampleColor.g * alphas.g;
			#if !CLAMP
					totalUsedWeight.g += GreenWeights[weightIndex.g];
				}
			#endif
			weightIndex.g++;
		}

		// Blue Blur	
		if (-radius.b <= i && i <= radius.b)
		{
			#if !CLAMP
				if (inBounds)
				{
			#endif
			alphas.b = sampleColor.a * BlueWeights[weightIndex.b];
			blurredColor.b = sampleColor.b * alphas.b;
			#if !CLAMP
					totalUsedWeight.b += BlueWeights[weightIndex.b];
				}
			#endif
			weightIndex.b++;
		}

		// Alpha Blur	
		if (-radius.a <= i && i <= radius.a)
		{
			#if !CLAMP
				if (inBounds)
				{
			#endif
			blurredColor.a = sampleColor.a * AlphaWeights[weightIndex.a];
			#if !CLAMP
					totalUsedWeight.a += AlphaWeights[weightIndex.a];
				}
			#endif
			weightIndex.a++;
		}

		#if !CLAMP
			if (inBounds)
			{
		#endif
		totalAlphas += alphas;
		c += blurredColor;
		#if !CLAMP
			}
		#endif
    }

	if (c.a > 0)
	{
		c.rgb /= totalAlphas.rgb;
	}
	else
	{
		c = 0;
	}

	#if !CLAMP
		// Average the remaining weights
		c /= totalUsedWeight;
	#endif
	
	BufferOut[DTid.y * regionWidth + DTid.x] = Float4ToUint(c);
}
