using System;
using System.Configuration;
using System.Drawing;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using PaintDotNet;
using PaintDotNet.PropertySystem;

namespace ComputeShaderEffects
{
    public abstract class TiledComputeShaderBase : ComputeShaderBase
    {
        private static int s_BuffSize = Marshal.SizeOf(typeof(ColorBgra));

        protected System.Diagnostics.Stopwatch _tmr;

        public int ApronSize { get; set; }

        public TiledComputeShaderBase(string name, Image image, string subMenuName, PaintDotNet.Effects.EffectFlags flags)
            : base(name, image, subMenuName, flags)
        {
            MaximumRegionWidth = 2048;
            MaximumRegionHeight = 512;
            CustomRegionHandling = true;
        }

        protected override void OnPreRender(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            KeyValueConfigurationElement displayTimer = GetDllConfig().AppSettings.Settings["Timer"];

            if (displayTimer != null && displayTimer.Value == "1")
            {
                _tmr = new System.Diagnostics.Stopwatch();
                _tmr.Start();
            }

            base.OnPreRender(dstArgs, srcArgs);
        }

        protected override unsafe void OnRenderRegion(Rectangle[] rois, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            Surface dst;
            Surface src;
            Rectangle previousTileRect = new Rectangle();
            Rectangle previousResultRect = new Rectangle();
            SharpDX.Direct3D11.Texture2D textureTile = null;
            SharpDX.Direct3D11.Buffer resultBuffer = null;
            SharpDX.Direct3D11.Buffer copyBuf = null;
            ShaderResourceView textureTileView = null;
            UnorderedAccessView resultView = null;
            SharpDX.Direct3D11.Buffer constBuffer = null;
            bool isComplete = false;

            dst = dstArgs.Surface;
            src = srcArgs.Surface;

            constBuffer = CreateConstantBuffer(Device, Marshal.SizeOf(Consts));

            foreach (Rectangle rect in rois)
            {
                if (!this.IsInitialized)
                    return;

                // Add apron
                Rectangle tileRect = AddApron(rect, ApronSize, srcArgs.Bounds);

                // Compute Shader Parameters
                Consts = SetRenderOptions(tileRect, rect, Consts);

                // Tile texture and view
                if (previousTileRect.Width != tileRect.Width || previousTileRect.Height != tileRect.Height)
                {
                    textureTileView.DisposeIfNotNull();
                    textureTile.DisposeIfNotNull();
                    textureTileView = CreateRegionView(out textureTile, Device, tileRect.Width, tileRect.Height);
                }

                // Result buffer and view
                if (previousResultRect.Width != rect.Width || previousResultRect.Height != rect.Height)
                {
                    resultView.DisposeIfNotNull();
                    resultBuffer.DisposeIfNotNull();
                    copyBuf.DisposeIfNotNull();
                    resultBuffer = CreateBuffer(Device, rect.Width * rect.Height * s_BuffSize, s_BuffSize);
                    resultView = CreateUnorderedAccessView(Device, resultBuffer);
                    copyBuf = CreateStagingBuffer(Device, Context, resultBuffer);
                }


                // Copy tile from src to texture
                SharpDX.DataBox dbox = Context.MapSubresource(textureTile, 0, MapMode.WriteDiscard, MapFlags.None);
                IntPtr textureBuffer = dbox.DataPointer;
                ColorBgra* srcPointer = src.GetPointPointer(tileRect.Left, tileRect.Top);
                int length = tileRect.Width * s_BuffSize;
                int sourceStride = src.Stride;
                int dstStride = dbox.RowPitch;
                int tileBottom = tileRect.Bottom;

                for (int y = tileRect.Top; y < tileBottom; y++)
                {
                    BufferUtil.Copy((void*)textureBuffer, srcPointer, length);
                    textureBuffer = IntPtr.Add(textureBuffer, dstStride);
                    srcPointer = (ColorBgra*)((byte*)srcPointer + sourceStride);
                }
                Context.UnmapSubresource(textureTile, 0);

                // Update constants resource
                unsafe
                {
                    byte[] constsBytes = RawSerialize(Consts);
                    fixed (byte* p = constsBytes)
                    {
                        var box = new SharpDX.DataBox((IntPtr)p);

                        Context.UpdateSubresource(box, constBuffer);
                    }
                }

                _resourceViews[0] = textureTileView;

                RunComputeShader(Context,
                    _shader,
                    _resourceViews,
                    new UnorderedAccessView[] { resultView },
                    constBuffer,
                    (int)Math.Ceiling(rect.Width / (float)DimensionX),
                    (int)Math.Ceiling(rect.Height / (float)DimensionY));

                base.Context.CopyResource(resultBuffer, copyBuf);

                // Copy to destination pixels
                SharpDX.DataBox mappedResource = Context.MapSubresource(copyBuf, 0, MapMode.Read, MapFlags.None);
                CopyStreamToSurface(mappedResource, dst, rect);
                Context.UnmapSubresource(copyBuf, 0);

                previousTileRect = tileRect;
                previousResultRect = rect;

                if (_tmr != null &&
                    rect.Top + rect.Height == src.Height &&
                    rect.Right == src.Width)
                {
                    isComplete = true;
                }
            }

            if (isComplete)
            {
                _tmr.Stop();
                System.Windows.Forms.MessageBox.Show(_tmr.ElapsedMilliseconds.ToString() + "ms");
            }

            textureTileView.DisposeIfNotNull();
            textureTile.DisposeIfNotNull();
            resultView.DisposeIfNotNull();
            resultBuffer.DisposeIfNotNull();
            copyBuf.DisposeIfNotNull();
            constBuffer.DisposeIfNotNull();
        }

        protected virtual Rectangle AddApron(Rectangle rect, int apronRadius, Rectangle maxBounds)
        {
            rect.Inflate(apronRadius, apronRadius);
            rect.Intersect(maxBounds);

            return rect;
        }

        protected virtual object SetRenderOptions(Rectangle tileRect, Rectangle renderRect, object consts)
        {
            return null;
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            return null;
        }
    }
}
