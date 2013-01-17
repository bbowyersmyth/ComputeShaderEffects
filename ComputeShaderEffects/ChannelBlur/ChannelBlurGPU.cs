using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using SlimDX.Direct3D11;
using SlimDX.D3DCompiler;

namespace ComputeShaderEffects.ChannelBlur
{
    [StructLayout(LayoutKind.Sequential)]
    struct Constants
    {
        public int Width;
        public int Height;
        public int RectOffsetX;
        public int RectOffsetY;
        public int RectWidth;
        public uint RedWeightLength;
        public uint GreenWeightLength;
        public uint BlueWeightLength;
        public uint AlphaWeightLength;
        public int MaxRadius;
        public int Padding1;
        public int Padding2;
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "(GPU) Channel Blur")]
    public class ChannelBlurGPU : ComputeShaderEffects.DualPassComputeShaderBase
    {
        private static int BUFF_SIZE = Marshal.SizeOf(typeof(uint)); 
        
        private int redRadius;
        private int greenRadius;
        private int blueRadius;
        private int alphaRadius;
        private bool repeatEdgePixels;
        private Dimensions blurDimensions;
        private float[] redWeights;
        private float[] greenWeights;
        private float[] blueWeights;
        private float[] alphaWeights;
        private SlimDX.DataStream weightRedData;
        private SlimDX.DataStream weightGreenData;
        private SlimDX.DataStream weightBlueData;
        private SlimDX.DataStream weightAlphaData;
        private SlimDX.Direct3D11.Buffer weightRedBuffer;
        private SlimDX.Direct3D11.Buffer weightGreenBuffer;
        private SlimDX.Direct3D11.Buffer weightBlueBuffer;
        private SlimDX.Direct3D11.Buffer weightAlphaBuffer;
        private ShaderResourceView weightRedView;
        private ShaderResourceView weightGreenView;
        private ShaderResourceView weightBlueView;
        private ShaderResourceView weightAlphaView;
        private bool isVert = false;

        private System.Diagnostics.Stopwatch tmr;

        public enum PropertyNames
        {
            RepeatEdgePixels,
            BlurDimensions,
            RedRadius,
            GreenRadius,
            BlueRadius,
            AlphaRadius
        }

        public enum Dimensions
        {
            HorizontalAndVertical,
            HorizontalOnly,
            VerticalOnly
        }

        public static string StaticName
        {
            get
            {
                return "(GPU) Channel Blur";
            }
        }

        public static Bitmap StaticIcon
        {
            get
            {
                return new Bitmap(typeof(ChannelBlurGPU), "ChannelBlurIcon.png");
            }
        }

        public ChannelBlurGPU()
            : base(ChannelBlurGPU.StaticName, ChannelBlurGPU.StaticIcon, SubmenuNames.Blurs, PaintDotNet.Effects.EffectFlags.Configurable | PaintDotNet.Effects.EffectFlags.SingleThreaded)
        {
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = PropertyBasedEffect.CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.RedRadius, ControlInfoPropertyNames.DisplayName, "Red Radius");
            configUI.SetPropertyControlValue(PropertyNames.GreenRadius, ControlInfoPropertyNames.DisplayName, "Green Radius");
            configUI.SetPropertyControlValue(PropertyNames.BlueRadius, ControlInfoPropertyNames.DisplayName, "Blue Radius");
            configUI.SetPropertyControlValue(PropertyNames.AlphaRadius, ControlInfoPropertyNames.DisplayName, "Alpha Radius");
            configUI.SetPropertyControlValue(PropertyNames.BlurDimensions, ControlInfoPropertyNames.DisplayName, "Blur Dimensions");
            PropertyControlInfo propInfo = configUI.FindControlForPropertyName(PropertyNames.BlurDimensions);
            propInfo.SetValueDisplayName(Dimensions.HorizontalAndVertical, "Horizontal and Vertical");
            propInfo.SetValueDisplayName(Dimensions.HorizontalOnly, "Horizontal Only");
            propInfo.SetValueDisplayName(Dimensions.VerticalOnly, "Vertical Only");
            configUI.SetPropertyControlValue(PropertyNames.RepeatEdgePixels, ControlInfoPropertyNames.DisplayName, "Edge Behavior");
            configUI.SetPropertyControlValue(PropertyNames.RepeatEdgePixels, ControlInfoPropertyNames.Description, "Repeat Edge Pixels");

            return configUI;
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>();

            props.Add(new Int32Property(PropertyNames.RedRadius, 0, 0, 200));
            props.Add(new Int32Property(PropertyNames.GreenRadius, 0, 0, 200));
            props.Add(new Int32Property(PropertyNames.BlueRadius, 0, 0, 200));
            props.Add(new Int32Property(PropertyNames.AlphaRadius, 0, 0, 200));
            props.Add(StaticListChoiceProperty.CreateForEnum<Dimensions>(PropertyNames.BlurDimensions, Dimensions.HorizontalAndVertical, false));
            props.Add(new BooleanProperty(PropertyNames.RepeatEdgePixels, true));
            
            return new PropertyCollection(props);
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    CleanUpLocal();
                }
                catch 
                {
                }
            }

            base.OnDispose(disposing);
        }

        protected override void OnPreRender(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            CleanUpLocal();

            base.OnPreRender(dstArgs, srcArgs);
            
            try
            {
                if (this.IsInitialized)
                {
                    // Copy weights
                    this.redWeights = CreateBlurWeights(this.redRadius);
                    this.greenWeights = CreateBlurWeights(this.greenRadius);
                    this.blueWeights = CreateBlurWeights(this.blueRadius);
                    this.alphaWeights = CreateBlurWeights(this.alphaRadius);
                    
                    weightRedView = CreateArrayView(this.redWeights, base.Device, out weightRedData, out weightRedBuffer);
                    weightGreenView = CreateArrayView(this.greenWeights, base.Device, out weightGreenData, out weightGreenBuffer);
                    weightBlueView = CreateArrayView(this.blueWeights, base.Device, out weightBlueData, out weightBlueBuffer);
                    weightAlphaView = CreateArrayView(this.alphaWeights, base.Device, out weightAlphaData, out weightAlphaBuffer);

                    base.AddResourceViews(new ShaderResourceView[] { weightRedView, weightGreenView, weightBlueView, weightAlphaView });

                    // Control number of passes based on dimensions
                    base.Passes = (this.blurDimensions == Dimensions.HorizontalAndVertical) ? 2 : 1;

                    base.ApronSize = Math.Max(Math.Max(Math.Max(this.redRadius, this.greenRadius), this.blueRadius), this.alphaRadius);
                }
            }
            catch (SlimDX.Direct3D11.Direct3D11Exception ex)
            {
                MessageBox.Show(ex.Message);
                this.IsInitialized = false;
            }

            base.OnPreRenderComplete(dstArgs, srcArgs);
        }

        protected override void OnBeginPass(int pass, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            string shaderPath;

            if (pass == 1 && (this.blurDimensions == Dimensions.HorizontalAndVertical || this.blurDimensions == Dimensions.HorizontalOnly))
            {
                if (this.repeatEdgePixels)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ChannelBlurHorizClamp";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ChannelBlurHoriz";
                }
                isVert = false;
            }
            else
            {
                if (this.repeatEdgePixels)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ChannelBlurVertClamp";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ChannelBlurVert";
                }
                isVert = true;
            }

            shaderPath += ".fx";

            base.Consts = new Constants();
            base.SetShader(shaderPath);

            base.OnBeginPass(pass, dstArgs, srcArgs);
        }

        protected override object SetRenderOptions(Rectangle tileRect, Rectangle renderRect, object consts)
        {
            Constants blurConstants = (Constants)consts;

            blurConstants.RedWeightLength = (uint)redWeights.Length;
            blurConstants.GreenWeightLength = (uint)greenWeights.Length;
            blurConstants.BlueWeightLength = (uint)blueWeights.Length;
            blurConstants.AlphaWeightLength = (uint)alphaWeights.Length;
            blurConstants.MaxRadius = Math.Max(Math.Max(Math.Max(this.redRadius, this.greenRadius), this.blueRadius), this.alphaRadius);
            blurConstants.Width = tileRect.Width - 1;
            blurConstants.Height = tileRect.Height - 1;
            blurConstants.RectOffsetX = renderRect.Left - tileRect.Left;
            blurConstants.RectOffsetY = renderRect.Top - tileRect.Top;
            blurConstants.RectWidth = renderRect.Width;

            return blurConstants;
        }

        protected override Rectangle AddApron(Rectangle rect, int apronRadius, Rectangle maxBounds)
        {
            if (isVert)
                rect.Inflate(0, apronRadius);
            else
                rect.Inflate(apronRadius, 0);

            rect.Intersect(maxBounds);

            return rect;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            this.redRadius = newToken.GetProperty<Int32Property>(PropertyNames.RedRadius).Value;
            this.greenRadius = newToken.GetProperty<Int32Property>(PropertyNames.GreenRadius).Value;
            this.blueRadius = newToken.GetProperty<Int32Property>(PropertyNames.BlueRadius).Value;
            this.alphaRadius = newToken.GetProperty<Int32Property>(PropertyNames.AlphaRadius).Value;
            this.repeatEdgePixels = newToken.GetProperty<BooleanProperty>(PropertyNames.RepeatEdgePixels).Value;
            this.blurDimensions = (Dimensions)newToken.GetProperty<StaticListChoiceProperty>(PropertyNames.BlurDimensions).Value;
            KeyValueConfigurationElement displayTimer = GetDllConfig().AppSettings.Settings["Timer"];

            if (displayTimer != null && displayTimer.Value == "1")
            {
                this.tmr = new System.Diagnostics.Stopwatch();
                this.tmr.Start();
            }

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        private static float[] CreateBlurWeights(int radius)
        {
            float[] kernel = new float[radius * 2 + 1];

            for (int i = 0; i <= radius; ++i)
            {
                kernel[i] = 16 * (i + 1);
                kernel[kernel.Length - i - 1] = kernel[i];
            }

            float div = kernel.Sum();
            for (int i = 0; i < kernel.Length; i++)
            {
                kernel[i] /= div;
            }

            return kernel;
        }

        private void CleanUpLocal()
        {
            if (weightRedData != null)
            {
                weightRedData.Close();
                weightRedData.Dispose();
            }
            if (weightGreenData != null)
            {
                weightGreenData.Close();
                weightGreenData.Dispose();
            }
            if (weightBlueData != null)
            {
                weightBlueData.Close();
                weightBlueData.Dispose();
            }
            if (weightAlphaData != null)
            {
                weightAlphaData.Close();
                weightAlphaData.Dispose();
            }
            weightRedBuffer.DisposeIfNotNull();
            weightGreenBuffer.DisposeIfNotNull();
            weightBlueBuffer.DisposeIfNotNull();
            weightAlphaBuffer.DisposeIfNotNull();
            weightRedView.DisposeIfNotNull();
            weightGreenView.DisposeIfNotNull();
            weightBlueView.DisposeIfNotNull();
            weightAlphaView.DisposeIfNotNull();
        }
    }
}
