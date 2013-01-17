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

StructuredBuffer<uint> Img : register(t0);
RWStructuredBuffer<uint> BufferOut : register(u0);

// Conversion functions
inline uint GetRed(   uint packedColor ) { return (packedColor >> 16) & 0x000000ff; }
inline uint GetGreen( uint packedColor ) { return (packedColor >>  8) & 0x000000ff; }
inline uint GetBlue(  uint packedColor ) { return (packedColor) & 0x000000ff; }
inline uint GetAlpha( uint packedColor ) { return (packedColor >> 24) & 0x000000ff; }
inline uint Uint4ToUint( uint4 ui ) { return clamp(ui.b, 0, 255) + (clamp(ui.g, 0, 255) << 8) + (clamp(ui.r, 0, 255) << 16) + (clamp(ui.a, 0, 255) << 24); }


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
			
	for (uint i = 0; i < 64; i++)
    {
        float scale = 1.0 - fAmount * (i / 63.0);
		int2 pt = pixelPoint * scale + center;
	
		if (pt.x >= 0 && pt.y >= 0 && pt.x <= imgExtent.x && pt.y <= imgExtent.y)
		{	
			uint imgColor = Img[pt.y * imgExtent.x + pt.x];
			sampleColor = float4(
					GetRed(imgColor),
					GetGreen(imgColor),
					GetBlue(imgColor),
					GetAlpha(imgColor)
				);
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

	BufferOut[DTid.y * regionWidth + DTid.x] = Uint4ToUint(c);
}
