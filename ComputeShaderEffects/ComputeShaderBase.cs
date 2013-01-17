using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using PaintDotNet.Effects;
using PaintDotNet.PropertySystem;
using PaintDotNet;
using SlimDX.Direct3D11;
using System.Runtime.InteropServices;
using System.IO;
using System.Configuration;
using SlimDX.D3DCompiler;
using System.Windows.Forms;

namespace ComputeShaderEffects
{
    public abstract class ComputeShaderBase : PaintDotNet.Effects.PropertyBasedEffect
    {
        private static int COLOR_SIZE = Marshal.SizeOf(typeof(ColorBgra));
        private bool newRender = false;

        private object consts;
        private Device device;
        private DeviceContext context;
        protected ComputeShader shader;
        private ShaderBytecode shaderCode;
        protected ShaderResourceView[] resourceViews = new ShaderResourceView[1];
        private bool isInitialized;

        public object Consts
        {
            get
            {
                return this.consts;
            }
            set
            {
                this.consts = value;
            }
        }
        public DeviceContext Context
        {
            get
            {
                return this.context;
            }
            set
            {
                this.context = value;
            }
        }
        public Device Device
        {
            get
            {
                return this.device;
            }
            set
            {
                this.device = value;
            }
        }
        public bool IsInitialized
        {
            get
            {
                return this.isInitialized;
            }
            set
            {
                this.isInitialized = value;
            }
        }
        public int DimensionX = 16;
        public int DimensionY = 16;

        protected ComputeShaderBase(string name, Image image, string subMenuName, PaintDotNet.Effects.EffectFlags flags)
            : base(name, image, subMenuName, flags)
        {
            MaxTextureSize = 8216;
            CustomRegionHandling = false;
        }

        public int MaximumRegionWidth { get; set; }
        public int MaximumRegionHeight { get; set; }
        public int MaxTextureSize { get; private set; }
        public bool CustomRegionHandling { get; set; }

        /*
        internal unsafe static void CopyStreamToSurface(SlimDX.DataBox dbox, Surface dst, Rectangle rect)
        {
            byte* textureBuffer = (byte*)dbox.Data.DataPointer;
            ulong stride = (ulong)(rect.Width * COLOR_SIZE);

            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                ColorBgra* pDstPixels = dst.GetPointAddressUnchecked(rect.Left, y);
                PaintDotNet.SystemLayer.Memory.Copy(pDstPixels, textureBuffer, stride);
                textureBuffer += stride;
            }
        }
         */

        internal unsafe static void CopyStreamToSurface(SlimDX.DataBox dbox, Surface dst, Rectangle rect)
        {
            byte* textureBuffer = (byte*)dbox.Data.DataPointer;

            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                CustomCopy(dst.GetPointAddressUnchecked(rect.Left, y), textureBuffer, rect.Width * COLOR_SIZE);
                textureBuffer += rect.Width * COLOR_SIZE;
            }
        }

        internal static unsafe void CustomCopy(void* dest, void* src, int count) {
            int block; 
            
            block = count >> 3; 
            
            long* pDest = (long*)dest; 
            long* pSrc = (long*)src; 
            
            for (int i = 0; i < block; i++) 
            { 
                *pDest = *pSrc; pDest++; pSrc++; 
            } 
            
            dest = pDest; 
            src = pSrc; 
            count = count - (block << 3); 
            
            if (count > 0) 
            { 
                byte* pDestB = (byte*)dest; 
                byte* pSrcB = (byte*)src; 
                
                for (int i = 0; i < count; i++) 
                { 
                    *pDestB = *pSrcB; pDestB++; pSrcB++; 
                } 
            } 
        }

        internal static SlimDX.Direct3D11.ShaderResourceView CreateArrayView(float[] values, Device device, out SlimDX.DataStream data, out SlimDX.Direct3D11.Buffer buff)
        {
            data = new SlimDX.DataStream(values.Length * Marshal.SizeOf(typeof(float)), true, true);
            data.WriteRange<float>(values);

            // Create the compute shader buffer and views for common data
            buff = CreateBuffer(device, data, Marshal.SizeOf(typeof(float)));
            return CreateView(device, buff);
        }

        internal static SlimDX.Direct3D11.Buffer CreateStagingBuffer(Device device, DeviceContext context, SlimDX.Direct3D11.Buffer buffer)
        {
            BufferDescription desc = buffer.Description;

            desc.CpuAccessFlags = CpuAccessFlags.Read;
            desc.Usage = ResourceUsage.Staging;
            desc.BindFlags = BindFlags.None;
            desc.OptionFlags = ResourceOptionFlags.StructuredBuffer;

            return new SlimDX.Direct3D11.Buffer(device, desc);
        }

        internal static SlimDX.Direct3D11.Buffer CreateBuffer(Device device, SlimDX.DataStream initData, int stride)
        {
            BufferDescription desc = new BufferDescription
            {
                BindFlags = BindFlags.ShaderResource,
                SizeInBytes = (int)initData.Length,
                StructureByteStride = stride,
                OptionFlags = ResourceOptionFlags.StructuredBuffer
            };

            initData.Position = 0;

            return new SlimDX.Direct3D11.Buffer(device, initData, desc);
        }

        internal static SlimDX.Direct3D11.Buffer CreateBuffer(Device device, int sizeInBytes, int stride)
        {
            BufferDescription desc = new BufferDescription
            {
                BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                SizeInBytes = sizeInBytes,
                StructureByteStride = stride,
                OptionFlags = ResourceOptionFlags.StructuredBuffer
            };

            return new SlimDX.Direct3D11.Buffer(device, desc);
        }

        internal static SlimDX.Direct3D11.Buffer CreateConstantBuffer(Device device, int size)
        {
            BufferDescription desc = new BufferDescription
            {
                BindFlags = BindFlags.ConstantBuffer,
                SizeInBytes = size,
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None
            };

            return new SlimDX.Direct3D11.Buffer(device, desc);
        }

        internal static void CreateDevice(out Device device, out DeviceContext context, out bool isInitialized)
        {
            try
            {
                FeatureLevel[] level = new FeatureLevel[] { FeatureLevel.Level_10_0 };
                device = new Device(DriverType.Hardware, DeviceCreationFlags.SingleThreaded, level);

                if (!device.CheckFeatureSupport(Feature.ComputeShaders))
                {
                    // GPU does not support compute shaders
                    device.Dispose();
                    device = new Device(DriverType.Warp, DeviceCreationFlags.SingleThreaded, level);

                    if (!device.CheckFeatureSupport(Feature.ComputeShaders))
                    {
                        // This version of Warp does not support compute shaders
                        device.Dispose();

                        isInitialized = false;
                        context = null;
                    }
                    else
                    {
                        isInitialized = true;
                        context = device.ImmediateContext;
                    }
                }
                else
                {
                    isInitialized = true;
                    context = device.ImmediateContext;
                }
            }
            catch
            {
                device = null;
                context = null;
                isInitialized = false;
            }

            if (!isInitialized)
            {
                System.Windows.Forms.MessageBox.Show("Device creation failed.\n\nPlease ensure that you have the latest drivers for your "
                    + "video card and that it supports DirectCompute.", "Hardware Accelerated Blur Pack");
            }
        }

        internal static ShaderResourceView CreateRegionView(out Texture2D texture, Device device, int width, int height)
        {
            Texture2DDescription texDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SlimDX.DXGI.Format.B8G8R8A8_UNorm,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.Write,
                SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0)
            };

            texture = new Texture2D(device, texDesc);

            ShaderResourceViewDescription desc = new ShaderResourceViewDescription
            {
                Dimension = ShaderResourceViewDimension.Texture2D,
                Format = texDesc.Format,
                MipLevels = 1,
                ArraySize = texDesc.ArraySize
            };

            return new ShaderResourceView(device, texture, desc);
        }

        internal static void CreateShader(Device device, out ShaderBytecode shaderCode, out ComputeShader shader, string shaderPath)
        {
            SlimDX.DataStream data = new SlimDX.DataStream(GetEmbeddedContent(shaderPath), true, false);
            shaderCode = new ShaderBytecode(data);
            shader = new ComputeShader(device, shaderCode);
        }

        internal static ShaderResourceView CreateView(out Texture2D texture, Device device, SlimDX.DataStream ds, int width, int height)
        {
            Texture2DDescription texDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = SlimDX.DXGI.Format.B8G8R8A8_UNorm,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.Write,
                SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0)
            };

            SlimDX.DataRectangle data = new SlimDX.DataRectangle(width * COLOR_SIZE, ds);
            texture = new Texture2D(device, texDesc, data);

            ShaderResourceViewDescription desc = new ShaderResourceViewDescription
            {
                Dimension = ShaderResourceViewDimension.Texture2D,
                Format = texDesc.Format,
                MipLevels = 1,
                ArraySize = texDesc.ArraySize
            };

            return new ShaderResourceView(device, texture, desc);
        }

        internal static UnorderedAccessView CreateUnorderedAccessView(Device device, SlimDX.Direct3D11.Buffer buffer)
        {
            UnorderedAccessViewDescription desc = new UnorderedAccessViewDescription
            {
                Dimension = UnorderedAccessViewDimension.Buffer,
                FirstElement = 0,
                Format = SlimDX.DXGI.Format.Unknown,
                ElementCount = buffer.Description.SizeInBytes / buffer.Description.StructureByteStride
            };

            return new UnorderedAccessView(device, buffer, desc);
        }

        internal static ShaderResourceView CreateView(Device device, SlimDX.Direct3D11.Buffer buffer)
        {
            ShaderResourceViewDescription desc = new ShaderResourceViewDescription
            {
                Dimension = ShaderResourceViewDimension.ExtendedBuffer,
                FirstElement = 0,
                Format = SlimDX.DXGI.Format.Unknown,
                ElementCount = buffer.Description.SizeInBytes / buffer.Description.StructureByteStride
            };

            return new ShaderResourceView(device, buffer, desc);
        }

        internal static byte[] GetEmbeddedContent(string resourceName)
        {
            Stream resourceStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            BinaryReader reader = new BinaryReader(resourceStream);
            return reader.ReadBytes((int)resourceStream.Length);
        }

        internal static Configuration GetDllConfig()
        {
            var configFile = System.Reflection.Assembly.GetExecutingAssembly().Location + ".config";
            var map = new ExeConfigurationFileMap
            {
                ExeConfigFilename = configFile
            };
            return ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);
        }

        internal static void RunComputeShader(DeviceContext context, ComputeShader shader, ShaderResourceView[] views, UnorderedAccessView[] unordered, SlimDX.Direct3D11.Buffer constParams, int x, int y)
        {
            ComputeShaderWrapper cs = context.ComputeShader;

            cs.Set(shader);
            cs.SetShaderResources(views, 0, views.Length);
            cs.SetUnorderedAccessViews(unordered, 0, unordered.Length, new int[] { 0 });
            cs.SetConstantBuffer(constParams, 0);
            context.Dispatch(x, y, 1);
        }

        internal static SlimDX.DataStream SurfaceToStream(Surface src)
        {
            //int height = src.Height;
            //int byteWidth = src.Width * COLOR_SIZE;
            //SlimDX.DataStream imageStream = new SlimDX.DataStream(src.Width * height * COLOR_SIZE, true, true);

            //for (int imgY = 0; imgY < height; imgY++)
            //{
            //    imageStream.WriteRange(src.GetPointPointer(0, imgY), byteWidth);
            //}

            //return imageStream;

            return new SlimDX.DataStream(src.Scan0.Pointer, src.Width * src.Height * COLOR_SIZE, true, false);
        }

        protected abstract override PropertyCollection OnCreatePropertyCollection();

        protected virtual void OnPreRender(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            CleanUp();

            try
            {
                // Create DirectX device and shaders
                CreateDevice(out device, out context, out this.isInitialized);
            }
            catch (SlimDX.Direct3D11.Direct3D11Exception ex)
            {
                MessageBox.Show(ex.Message);
                this.isInitialized = false;
            }
        }

        protected void SetShader(string path)
        {
            shaderCode.DisposeIfNotNull();
            shader.DisposeIfNotNull();
            CreateShader(device, out shaderCode, out shader, path);
        }

        protected virtual void CleanUp()
        {
            shader.DisposeIfNotNull();
            shaderCode.DisposeIfNotNull();
            context.DisposeIfNotNull();
            device.DisposeIfNotNull();
        }

        protected void AddResourceViews(ShaderResourceView[] views)
        {
            resourceViews = new ShaderResourceView[views.Length + 1];
            System.Array.Copy(views, 0, resourceViews, 1, views.Length);
        }

        protected override void OnRender(Rectangle[] rois, int startIndex, int length)
        {
            if (length == 0)
                return;

            if (this.CustomRegionHandling && FullImageSelected(base.SrcArgs.Bounds))
            {
                if (this.newRender)
                {
                    this.newRender = false;
                    this.OnRenderRegion(SliceRectangles(new Rectangle[] { this.EnvironmentParameters.GetSelection(base.SrcArgs.Bounds).GetBoundsInt() }), base.DstArgs, base.SrcArgs);
                }
            }
            else
            {
                this.OnRenderRegion(SliceRectangles(rois.Skip<Rectangle>(startIndex).Take<Rectangle>(length).ToArray<Rectangle>()), base.DstArgs, base.SrcArgs);
            }
        }

        protected virtual void OnRenderRegion(Rectangle[] rois, RenderArgs dstArgs, RenderArgs srcArgs)
        {
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            this.newRender = true;
            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
            this.OnPreRender(dstArgs, srcArgs);
        }

        internal Rectangle[] SliceRectangles(Rectangle[] rois)
        {
            if (rois.Length == 0 || (this.MaximumRegionHeight == 0 && this.MaximumRegionWidth == 0))
                return rois;

            // Re-slice regions
            List<Rectangle> sizedRegions = new List<Rectangle>();
            Rectangle[] rectCopy = rois;

            // Resize width
            foreach (Rectangle rect in rectCopy)
            {
                if (this.MaximumRegionWidth > 0 && rect.Width > this.MaximumRegionWidth)
                {
                    int sliceCount = (int)Math.Ceiling((double)rect.Width / (double)this.MaximumRegionWidth);

                    for (int i = 0; i < sliceCount; i++)
                    {
                        if (i < sliceCount - 1)
                        {
                            sizedRegions.Add(new Rectangle(rect.X + (this.MaximumRegionWidth * i), rect.Y, this.MaximumRegionWidth, rect.Height));
                        }
                        else
                        {
                            int remainingWidth = rect.Width - this.MaximumRegionWidth * (sliceCount - 1);
                            sizedRegions.Add(new Rectangle(rect.Right - remainingWidth, rect.Y, remainingWidth, rect.Height));
                        }
                    }
                }
                else
                {
                    sizedRegions.Add(rect);
                }
            }

            rectCopy = sizedRegions.ToArray();
            sizedRegions.Clear();

            // Resize height
            foreach (Rectangle rect in rectCopy)
            {
                if (this.MaximumRegionHeight > 0 && rect.Height > this.MaximumRegionHeight)
                {
                    int sliceCount = (int)Math.Ceiling((double)rect.Height / (double)this.MaximumRegionHeight);

                    for (int i = 0; i < sliceCount; i++)
                    {
                        if (i < sliceCount - 1)
                        {
                            sizedRegions.Add(new Rectangle(rect.X, rect.Y + (this.MaximumRegionHeight * i), rect.Width, this.MaximumRegionHeight));
                        }
                        else
                        {
                            int remainingHeight = rect.Height - this.MaximumRegionHeight * (sliceCount - 1);
                            sizedRegions.Add(new Rectangle(rect.X, rect.Bottom - remainingHeight, rect.Width, remainingHeight));
                        }
                    }
                }
                else
                {
                    sizedRegions.Add(rect);
                }
            }

            return sizedRegions.ToArray();
        }

        internal bool FullImageSelected(Rectangle bounds)
        {
            Rectangle[] rois = this.EnvironmentParameters.GetSelection(bounds).GetRegionScansReadOnlyInt();
            return (rois.Length == 1 && rois[0] == bounds);
        }

        internal static byte[] RawSerialize(object value)
        {
            int rawsize = Marshal.SizeOf(value);
            byte[] rawdata = new byte[rawsize];

            GCHandle handle = GCHandle.Alloc(rawdata, GCHandleType.Pinned);
            Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
            handle.Free();

            return rawdata;
        }
    }
}
