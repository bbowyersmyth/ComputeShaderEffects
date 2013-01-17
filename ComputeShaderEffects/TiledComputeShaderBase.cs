using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using PaintDotNet;
using SharpDX.Direct3D11;
using System.Windows.Forms;
using SharpDX.D3DCompiler;
using System.Runtime.InteropServices;
using PaintDotNet.PropertySystem;
using System.Configuration;

namespace ComputeShaderEffects
{
    public abstract class TiledComputeShaderBase : ComputeShaderBase
    {
        private static int BUFF_SIZE = Marshal.SizeOf(typeof(ColorBgra));

        public int ApronSize { get; set; }
        private System.Diagnostics.Stopwatch tmr;

        public TiledComputeShaderBase(string name, Image image, string subMenuName, PaintDotNet.Effects.EffectFlags flags)
            : base(name, image, subMenuName, flags)
        {
            MaximumRegionWidth = 1024;
            MaximumRegionHeight = 1024;
            CustomRegionHandling = true;
        }

        protected override void OnPreRender(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            KeyValueConfigurationElement displayTimer = GetDllConfig().AppSettings.Settings["Timer"];

            if (displayTimer != null && displayTimer.Value == "1")
            {
                this.tmr = new System.Diagnostics.Stopwatch();
                this.tmr.Start();
            }

            base.OnPreRender(dstArgs, srcArgs);
        }

        protected override void OnRenderRegion(Rectangle[] rois, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            Surface dst;
            Surface src;
            Rectangle previousTileRect = new Rectangle();
            Rectangle previousRect = new Rectangle();
            SharpDX.Direct3D11.Texture2D textureTile = null;
            SharpDX.Direct3D11.Buffer resultBuffer = null;
            SharpDX.Direct3D11.Buffer copyBuf = null;
            ShaderResourceView textureTileView = null;
            UnorderedAccessView resultView = null;
            SharpDX.Direct3D11.Buffer constBuffer = null;

            dst = dstArgs.Surface;
            src = srcArgs.Surface;

            constBuffer = CreateConstantBuffer(base.Device, Marshal.SizeOf(this.Consts));

            foreach (Rectangle rect in rois)
            {
                if (!this.IsInitialized) // || base.IsCancelRequested)
                    return;

                // Add apron
                Rectangle tileRect = AddApron(rect, this.ApronSize, srcArgs.Bounds);

                // Compute Shader Parameters
                this.Consts = SetRenderOptions(tileRect, rect, this.Consts);

                // Tile texture and view
                if (previousTileRect.Width != tileRect.Width || previousTileRect.Height != tileRect.Height)
                {
                    textureTileView.DisposeIfNotNull();
                    textureTile.DisposeIfNotNull();
                    textureTileView = CreateRegionView(out textureTile, base.Device, tileRect.Width, tileRect.Height);
                }

                // Result buffer and view
                if (previousRect.Width != rect.Width || previousRect.Height != rect.Height)
                {
                    resultView.DisposeIfNotNull();
                    resultBuffer.DisposeIfNotNull();
                    copyBuf.DisposeIfNotNull();
                    resultBuffer = CreateBuffer(base.Device, rect.Width * rect.Height * BUFF_SIZE, BUFF_SIZE);
                    resultView = CreateUnorderedAccessView(base.Device, resultBuffer);
                    copyBuf = CreateStagingBuffer(base.Device, base.Context, resultBuffer);
                }

                // Copy tile from src to texture
                SharpDX.DataBox dbox = base.Context.MapSubresource(textureTile, 0, MapMode.WriteDiscard, MapFlags.None);
                unsafe
                {
                    byte* textureBuffer = (byte*)dbox.DataPointer;

                    for (int y = tileRect.Top; y < tileRect.Bottom; y++)
                    {
                        //PaintDotNet.SystemLayer.Memory.Copy(textureBuffer, src.GetPointAddressUnchecked(tileRect.Left, y), width);
                        CustomCopy(textureBuffer, src.GetPointAddressUnchecked(tileRect.Left, y), tileRect.Width * BUFF_SIZE);
                        textureBuffer += dbox.RowPitch;
                    }
                    base.Context.UnmapSubresource(textureTile, 0);
                }

                // Update constants resource
                using (SharpDX.DataStream data = new SharpDX.DataStream(Marshal.SizeOf(this.Consts), true, true))
                {
                    byte[] constsBytes = RawSerialize(this.Consts);
                    data.Write(constsBytes, 0, constsBytes.Length);
                    data.Position = 0;
                    base.Context.UpdateSubresource(new SharpDX.DataBox(data.DataPointer), constBuffer, 0);
                }

                resourceViews[0] = textureTileView;

                RunComputeShader(base.Context,
                    shader,
                    resourceViews,
                    new UnorderedAccessView[] { resultView },
                    constBuffer,
                    (int)Math.Ceiling(rect.Width / (float)DimensionX),
                    (int)Math.Ceiling(rect.Height / (float)DimensionY));

                base.Context.CopyResource(resultBuffer, copyBuf);

                // Copy to destination pixels
                SharpDX.DataBox mappedResource = base.Context.MapSubresource(copyBuf, 0, MapMode.Read, MapFlags.None);
                CopyStreamToSurface(mappedResource, dst, rect);
                base.Context.UnmapSubresource(copyBuf, 0);

                previousTileRect = tileRect;
                previousRect = rect;

                if (this.tmr != null &&
                    rect.Top + rect.Height == src.Height &&
                    rect.Right == src.Width)
                {
                    this.tmr.Stop();
                    System.Windows.Forms.MessageBox.Show(this.tmr.ElapsedMilliseconds.ToString() + "ms");
                }
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
