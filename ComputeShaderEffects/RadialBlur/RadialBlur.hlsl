#define threads_x 64
#define threads_y 1
 
cbuffer Consts : register(c0)
{
	float2 imgExtent;
	float2 center;
	float2 regionOffset;
    int regionWidth;
	float theta;
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

float2 RotatePoint(float2 sample, float angle)
{
	float sine;
	float cosine;

	sincos(angle, sine, cosine);

	float x_p = (sample.x * cosine) - (sample.y * sine);
	float y_p = (sample.y * cosine) + (sample.x * sine);

	return float2(x_p, y_p);
}

[numthreads(threads_x, threads_y, 1)]
void CSMain( uint3 DTid : SV_DispatchThreadID )
{
	if (DTid.x > (uint)(regionWidth - 1))
	{
		return;
	}

	float2 pixelPoint = DTid.xy + regionOffset - center + 0.5;
	float2 samplePoint;
	float4 sampleColor;
	float3 cr=0;
	float ca=0;
	int samples = 0;
	
	for (float i = -1374; i < 1374; i+=2)
    {
		samplePoint = RotatePoint(pixelPoint, theta * i) + center;

		if (samplePoint.x >= 0 && samplePoint.y >= 0 && samplePoint.x <= imgExtent.x && samplePoint.y <= imgExtent.y)
		{
			sampleColor = Img.SampleLevel(g_SampTex, samplePoint / imgExtent, 0);
			cr += sampleColor.rgb * sampleColor.a;
			ca += sampleColor.a;
			samples++;
		}
    }

	if (ca > 0)
	{
		cr.rgb /= ca;
		ca /= samples;
	}
	else
	{
		cr = 0;
	}

	BufferOut[DTid.y * regionWidth + DTid.x] = Float4ToUint(float4(cr, ca));
}
