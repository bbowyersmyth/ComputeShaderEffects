#define threads_x 64
#define threads_y 1

cbuffer Consts : register(c0)
{
	int2 imgExtent;
	int2 center;
	int2 regionOffset;
    int regionWidth;
    int amount;
};

Texture2D<float4> Img : register(t0);
RWStructuredBuffer<uint> BufferOut : register(u0);

SamplerState g_SampTex
{
	AddressU = Clamp;
    AddressV = Clamp;
    Filter = MIN_MAG_MIP_LINEAR;
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

	int2 pixelPoint = DTid.xy + regionOffset - center;
	float4 sampleColor;
	int sampleCount;
	float4 c = 0;
	float fAmount = amount / 300.0;
			
	for (uint i = 0; i < 64; i+=2)
    {
        float scale = 1.0 - fAmount * (i / 63.0);
		float2 pt = pixelPoint * scale + center + 0.5;
	
		if (pt.x >= 0 && pt.y >= 0 && pt.x <= imgExtent.x && pt.y <= imgExtent.y)
		{	
			sampleColor = Img.SampleLevel(g_SampTex, pt / imgExtent, 0);
			sampleColor.rgb *= sampleColor.a;

			c += sampleColor;
			sampleCount++;
		}
    }

	if (c.a > 0)
	{
		c.rgb /= c.a;
		c.a /= sampleCount;
	}
	else
	{
		c = 0;
	}

	BufferOut[DTid.y * regionWidth + DTid.x] = Float4ToUint(c);
}
