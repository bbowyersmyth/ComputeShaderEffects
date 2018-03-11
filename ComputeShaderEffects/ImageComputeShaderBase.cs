using System;
using System.Configuration;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PaintDotNet;
using SharpDX.Direct3D11;

namespace ComputeShaderEffects
{
    public abstract class ImageComputeShaderBase : ComputeShaderBase
    {
        private static int s_BuffSize = Marshal.SizeOf(typeof(ColorBgra));

        private SharpDX.DataStream _imageData;
        private ShaderResourceView _imageView;
        private Texture2D _texture;
        private SharpDX.Direct3D11.Buffer _imageBuffer;
        private System.Diagnostics.Stopwatch _tmr;

        public bool IsLargeImage { get; set; }

        public ImageComputeShaderBase(string name, Image image, string subMenuName, PaintDotNet.Effects.EffectFlags flags)
            : base(name, image, subMenuName, flags)
        {
            DimensionX = 64;
            DimensionY = 1;
        }

        protected override void CleanUp()
        {
            if (_imageData != null)
            {
                _imageData.Close();
                _imageData.Dispose();
            }
            _imageView.DisposeIfNotNull();
            _texture.DisposeIfNotNull();
            _imageBuffer.DisposeIfNotNull();

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
                _tmr = new System.Diagnostics.Stopwatch();
                _tmr.Start();
            }

            base.OnPreRender(dstArgs, srcArgs);

            IsLargeImage = (srcArgs.Width > MaxTextureSize || srcArgs.Height > MaxTextureSize);

            try
            {
                if (IsInitialized)
                {
                    // Copy source image pixels
                    _imageData = SurfaceToStream(srcArgs.Surface);

                    if (IsLargeImage)
                    {
                        // Create the source image buffer and views
                        _imageBuffer = CreateBuffer(Device, _imageData, s_BuffSize);
                        _imageView = CreateView(Device, _imageBuffer);
                    }
                    else
                    {
                        // Create the source texture view
                        _imageView = CreateView(out _texture, Device, _imageData, srcArgs.Width, srcArgs.Height);
                    }
                }
            }
            catch (SharpDX.SharpDXException ex)
            {
                MessageBox.Show(ex.Message);
                IsInitialized = false;
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

            constBuffer = CreateConstantBuffer(Device, Marshal.SizeOf(Consts));

            foreach (Rectangle rect in rois)
            {
                if (!IsInitialized || IsCancelRequested)
                    return;

                // Compute Shader Parameters
                Consts = SetRenderOptions(rect, Consts);

                // Result buffer and view
                if (previousRect.Width != rect.Width || previousRect.Height != rect.Height)
                {
                    resultView.DisposeIfNotNull();
                    resultBuffer.DisposeIfNotNull();
                    copyBuf.DisposeIfNotNull();
                    resultBuffer = CreateBuffer(Device, rect.Width * rect.Height * s_BuffSize, s_BuffSize);
                    resultView = CreateUnorderedAccessView(Device, resultBuffer);
                    copyBuf = CreateStagingBuffer(Device, Context, resultBuffer);
                }

                // Update constants resource
                using (SharpDX.DataStream data = new SharpDX.DataStream(Marshal.SizeOf(Consts), true, true))
                {
                    byte[] constsBytes = RawSerialize(Consts);
                    data.Write(constsBytes, 0, constsBytes.Length);
                    data.Position = 0;
                    Context.UpdateSubresource(new SharpDX.DataBox(data.DataPointer), constBuffer, 0);
                }

                _resourceViews[0] = _imageView;

                RunComputeShader(Context,
                    _shader,
                    _resourceViews,
                    new UnorderedAccessView[] { resultView },
                    constBuffer,
                    (int)Math.Ceiling(rect.Width / (float)DimensionX),
                    (int)Math.Ceiling(rect.Height / (float)DimensionY));

                Context.CopyResource(resultBuffer, copyBuf);

                // Copy to destination pixels
                SharpDX.DataBox mappedResource = Context.MapSubresource(copyBuf, 0, MapMode.Read, MapFlags.None);
                CopyStreamToSurface(mappedResource, dst, rect);
                Context.UnmapSubresource(copyBuf, 0);

                previousRect = rect;

                if (_tmr != null &&
                    rect.Top + rect.Height == src.Height &&
                    rect.Right == src.Width)
                {
                    _tmr.Stop();
                    MessageBox.Show(_tmr.ElapsedMilliseconds.ToString() + "ms");
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
