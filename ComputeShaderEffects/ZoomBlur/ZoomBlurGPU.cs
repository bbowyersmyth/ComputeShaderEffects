using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using SlimDX.Direct3D11;
using SlimDX.D3DCompiler;

namespace ComputeShaderEffects.ZoomBlur
{
    [StructLayout(LayoutKind.Sequential)]
    struct Constants
    {
        public int Width;
        public int Height;
        public int CenterX;
        public int CenterY;
        public int RectOffsetX;
        public int RectOffsetY;
        public int RectWidth;
        public int Amount;
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "(GPU) Zoom Blur")]
    public class ZoomBlurGPU : ComputeShaderEffects.ImageComputeShaderBase
    {
        private static int BUFF_SIZE = Marshal.SizeOf(typeof(uint)); 
        
        private int amount;
        private Point center;
        
        public enum PropertyNames
        {
            Amount,
            Center
        }

        public static string StaticName
        {
            get
            {
                return "(GPU) Zoom Blur";
            }
        }

        public static Bitmap StaticIcon
        {
            get
            {
                return new Bitmap(typeof(ZoomBlurGPU), "ZoomBlurIcon.png");
            }
        }

        public ZoomBlurGPU()
            : base(ZoomBlurGPU.StaticName, ZoomBlurGPU.StaticIcon, SubmenuNames.Blurs, PaintDotNet.Effects.EffectFlags.Configurable | PaintDotNet.Effects.EffectFlags.SingleThreaded)
        {
            MaximumRegionWidth = 512;
            MaximumRegionHeight = 512;
            CustomRegionHandling = true;
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = PropertyBasedEffect.CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.Amount, ControlInfoPropertyNames.DisplayName, "Zoom Amount");
            configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.DisplayName, "Center");
            configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.SliderSmallChangeX, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.SliderLargeChangeX, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.UpDownIncrementX, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.SliderSmallChangeY, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.SliderLargeChangeY, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.UpDownIncrementY, 0.01);
            ImageResource imageResource = ImageResource.FromImage(base.EnvironmentParameters.SourceSurface.CreateAliasedBitmap());
            configUI.SetPropertyControlValue(PropertyNames.Center, ControlInfoPropertyNames.StaticImageUnderlay, imageResource);

            return configUI;
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>();

            props.Add(new Int32Property(PropertyNames.Amount, 10, 0, 100));
            props.Add(new DoubleVectorProperty(PropertyNames.Center, Pair.Create<double, double>(0.0, 0.0), Pair.Create<double, double>(-2.0, -2.0), Pair.Create<double, double>(2.0, 2.0)));
        
            return new PropertyCollection(props);
        }

        protected override void OnPreRender(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            string shaderPath;

            CleanUp();

            base.OnPreRender(dstArgs, srcArgs);
            
            try
            {
                if (this.IsLargeImage)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ZoomBlurBuff.fx";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ZoomBlur.fx";
                }

                // Create DirectX device and shaders
                base.SetShader(shaderPath);
                base.Consts = new Constants();
            }
            catch (SlimDX.Direct3D11.Direct3D11Exception ex)
            {
                MessageBox.Show(ex.Message);
                this.IsInitialized = false;
            }
        }

        protected override object SetRenderOptions(Rectangle renderRect, object consts)
        {
            Constants blurConstants = (Constants)consts;

            blurConstants.Width = base.SrcArgs.Surface.Width;
            blurConstants.Height = base.SrcArgs.Surface.Height;
            blurConstants.CenterX = this.center.X;
            blurConstants.CenterY = this.center.Y;
            blurConstants.RectOffsetX = renderRect.Left;
            blurConstants.RectOffsetY = renderRect.Top;
            blurConstants.RectWidth = renderRect.Width;
            blurConstants.Amount = this.amount;

            return blurConstants;
        }
        
        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            this.amount = newToken.GetProperty<Int32Property>(PropertyNames.Amount).Value;
            this.center.X = (int)((1 + newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Center).ValueX) * (srcArgs.Width / 2.0));
            this.center.Y = (int)((1 + newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Center).ValueY) * (srcArgs.Height / 2.0));

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }
    }
}