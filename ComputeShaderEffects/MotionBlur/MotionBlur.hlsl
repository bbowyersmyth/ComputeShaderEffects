//#define threads_x 16
//#define threads_y 16
#define threads_x 256
#define threads_y 1

cbuffer Consts : register(c0)
{
	int2 imgExtent;
	int pointCount;
	int padding;
	int2 regionOffset;
    uint regionWidth;
    int padding2;
};

Texture2D<float4> Img : register(t0);
StructuredBuffer<float2> Points : register(t1);
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
	if (DTid.x > regionWidth - 1)
	{
		return;
	}

	float2 pixelPoint = DTid.xy + regionOffset + 0.5;
	float4 sampleColor;
	int samples = 0;
	float4 c = 0;
	
	for (int i = 0; i < pointCount; i++)
    {
		float2 pt = pixelPoint + Points[i];
		
		#if !CLAMP
			if (pt.x >= 0 && pt.y >= 0 && pt.x <= imgExtent.x && pt.y <= imgExtent.y)
			{
		#endif

			sampleColor = Img.SampleLevel(g_SampTex, pt / imgExtent, 0);
			sampleColor.rgb *= sampleColor.a;

			c += sampleColor;

		#if !CLAMP
				samples++;
			} // End If (Bounds)
		#endif
    }

	if (c.a > 0)
	{
		c.rgb /= c.a;

		#if CLAMP
			c.a /= pointCount;
		#else
			if (samples > 0)
			{
				c.a /= samples;
			}
		#endif
	}
	else
	{
		c = 0;
	}

	BufferOut[DTid.y * regionWidth + DTid.x] = Float4ToUint(c);
}
