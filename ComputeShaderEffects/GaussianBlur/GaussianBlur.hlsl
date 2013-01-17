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
	float totalUsedWeight = 0;
	int weightIndex = 0;

	for (int i = -radius; i <= radius + 1; i++, weightIndex++)
    {
		#if VERT
			// Vertical pass
			pt = float2(pixelPoint.x, pixelPoint.y + i);
		#else
			// Horizontal pass
			pt = float2(pixelPoint.x + i, pixelPoint.y);
		#endif

		#if !CLAMP
			#if VERT
				if (pt.y >= 0 && pt.y <= tileExtent.y)
			#else
				if (pt.x >= 0 && pt.x <= tileExtent.x)
			#endif
			{
		#endif

		sampleColor = Img.SampleLevel(g_SampTex, pt / tileExtent, 0);
		
		c += sampleColor * Weights[weightIndex];

		#if !CLAMP
				totalUsedWeight += Weights[weightIndex];
			} // End If (Bounds)
		#endif
    }

	#if !CLAMP
		// Average the remaining weights
		c /= totalUsedWeight;
	#endif
		
	BufferOut[DTid.y * regionWidth + DTid.x] = Float4ToUint(c);
}
