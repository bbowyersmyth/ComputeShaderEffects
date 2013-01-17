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

StructuredBuffer<uint> Img : register(t0);
RWStructuredBuffer<uint> BufferOut : register(u0);

// Conversion functions
inline uint GetRed(   uint packedColor ) { return (packedColor >> 16) & 0x000000ff; }
inline uint GetGreen( uint packedColor ) { return (packedColor >>  8) & 0x000000ff; }
inline uint GetBlue(  uint packedColor ) { return (packedColor) & 0x000000ff; }
inline uint GetAlpha( uint packedColor ) { return (packedColor >> 24) & 0x000000ff; }
inline uint Uint4ToUint( uint4 ui ) { return clamp(ui.b, 0, 255) + (clamp(ui.g, 0, 255) << 8) + (clamp(ui.r, 0, 255) << 16) + (clamp(ui.a, 0, 255) << 24); }

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

	float2 pixelPoint = DTid.xy + regionOffset - center;
	float2 samplePoint;
	float4 sampleColor;
	float3 cr=0;
	float ca=0;
	int samples = 0;
	uint imgColor;

	for (float i = -1374; i < 1374; i++)
    {
		samplePoint = RotatePoint(pixelPoint, theta * i) + center;

		if (samplePoint.x >= 0 && samplePoint.y >= 0 && samplePoint.x <= imgExtent.x && samplePoint.y <= imgExtent.y)
		{
			imgColor = Img[int(round(samplePoint.y) * imgExtent.x + round(samplePoint.x))];
			sampleColor = float4(
					GetRed(imgColor),
					GetGreen(imgColor),
					GetBlue(imgColor),
					GetAlpha(imgColor)
				);
			
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

	BufferOut[DTid.y * regionWidth + DTid.x] = Uint4ToUint(float4(cr, ca));
}
