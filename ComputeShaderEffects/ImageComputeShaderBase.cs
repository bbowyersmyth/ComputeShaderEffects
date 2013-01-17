using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using PaintDotNet;
using SharpDX.Direct3D11;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Configuration;

namespace ComputeShaderEffects
{
    public abstract class ImageComputeShaderBase : ComputeShaderBase
    {
        private static int BUFF_SIZE = Marshal.SizeOf(typeof(ColorBgra));

        private SharpDX.DataStream imageData;
        private ShaderResourceView imageView;
        private Texture2D texture;
        private SharpDX.Direct3D11.Buffer imageBuffer;
        public bool IsLargeImage { get; set; }
        private System.Diagnostics.Stopwatch tmr;

        public ImageComputeShaderBase(string name, Image image, string subMenuName, PaintDotNet.Effects.EffectFlags flags)
            : base(name, image, subMenuName, flags)
        {
            DimensionX = 64;
            DimensionY = 1;
        }

        protected override void CleanUp()
        {            
            if (imageData != null)
            {
                imageData.Close();
                imageData.Dispose();
            }
            imageView.DisposeIfNotNull();
            texture.DisposeIfNotNull();
            imageBuffer.DisposeIfNotNull();
            
            base.CleanUp();
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    CleanUp();
                }
                catch
                {
                }
            }

            base.OnDispose(disposing);
        }

        protected override void OnPreRender(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            KeyValueConfigurationElement displayTimer = GetDllConfig().AppSettings.Settings["Timer"];

            CleanUp();

            if (displayTimer != null && displayTimer.Value == "1")
            {
                this.tmr = new System.Diagnostics.Stopwatch();
                this.tmr.Start();
            }

            base.OnPreRender(dstArgs, srcArgs);

            this.IsLargeImage = (srcArgs.Width > base.MaxTextureSize || srcArgs.Height > base.MaxTextureSize);

            try
            {                
                if (this.IsInitialized)
                {
                    // Copy source image pixels
                    imageData = SurfaceToStream(srcArgs.Surface);

                    if (this.IsLargeImage)
                    {
                        // Create the source image buffer and views
                        imageBuffer = CreateBuffer(base.Device, imageData, BUFF_SIZE);
                        imageView = CreateView(base.Device, imageBuffer);
                    }
                    else
                    {
                        // Create the source texture view
                        imageView = CreateView(out texture, base.Device, imageData, srcArgs.Width, srcArgs.Height);
                    }
                }
            }
            catch (SharpDX.SharpDXException ex)
            {
                MessageBox.Show(ex.Message);
                this.IsInitialized = false;
            }
        }

        protected override void OnRenderRegion(Rectangle[] rois, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            Surface dst;
            Surface src;
            Rectangle previousRect = new Rectangle();
            SharpDX.Direct3D11.Buffer resultBuffer = null;
            SharpDX.Direct3D11.Buffer copyBuf = null;
            UnorderedAccessView resultView = null;
            SharpDX.Direct3D11.Buffer constBuffer = null;

            dst = dstArgs.Surface;
            src = srcArgs.Surface;

            constBuffer = CreateConstantBuffer(base.Device, Marshal.SizeOf(this.Consts));

            foreach (Rectangle rect in rois)
            {
                if (!this.IsInitialized || base.IsCancelRequested)
                    return;
                
                // Compute Shader Parameters
                this.Consts = SetRenderOptions(rect, this.Consts);

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
                
                // Update constants resource
                using (SharpDX.DataStream data = new SharpDX.DataStream(Marshal.SizeOf(this.Consts), true, true))
                {
                    byte[] constsBytes = RawSerialize(this.Consts);
                    data.Write(constsBytes, 0, constsBytes.Length);
                    data.Position = 0;
                    base.Context.UpdateSubresource(new SharpDX.DataBox(data.DataPointer), constBuffer, 0);
                }

                resourceViews[0] = imageView;

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

                previousRect = rect;

                if (this.tmr != null && 
                    rect.Top + rect.Height == src.Height &&
                    rect.Right == src.Width)
                {
                    this.tmr.Stop();
                    System.Windows.Forms.MessageBox.Show(this.tmr.ElapsedMilliseconds.ToString() + "ms");
                }
            }

            resultView.DisposeIfNotNull();
            resultBuffer.DisposeIfNotNull();
            copyBuf.DisposeIfNotNull();
            constBuffer.DisposeIfNotNull();
        }

        protected virtual object SetRenderOptions(Rectangle renderRect, object consts)
        {
            return null;
        }
    }
}
