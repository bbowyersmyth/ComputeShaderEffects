using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

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
    public class ZoomBlurGPU : ImageComputeShaderBase
    {
        private int _amount;
        private Point _center;

        public enum PropertyNames
        {
            Amount,
            Center
        }

        public static string StaticName => "(GPU) Zoom Blur";

        public static Bitmap StaticIcon => new Bitmap(typeof(ZoomBlurGPU), "ZoomBlurIcon.png");

        public ZoomBlurGPU()
            : base(ZoomBlurGPU.StaticName, ZoomBlurGPU.StaticIcon, SubmenuNames.Blurs, EffectFlags.Configurable | EffectFlags.SingleThreaded)
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
            ImageResource imageResource = ImageResource.FromImage(EnvironmentParameters.SourceSurface.CreateAliasedBitmap());
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
                if (IsLargeImage)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ZoomBlurBuff.fx";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ZoomBlur.fx";
                }

                // Create DirectX device and shaders
                SetShader(shaderPath);
                Consts = new Constants();
            }
            catch (SharpDX.SharpDXException ex)
            {
                MessageBox.Show(ex.Message);
                IsInitialized = false;
            }
        }

        protected override object SetRenderOptions(Rectangle renderRect, object consts)
        {
            Constants blurConstants = (Constants)consts;

            blurConstants.Width = SrcArgs.Surface.Width;
            blurConstants.Height = SrcArgs.Surface.Height;
            blurConstants.CenterX = _center.X;
            blurConstants.CenterY = _center.Y;
            blurConstants.RectOffsetX = renderRect.Left;
            blurConstants.RectOffsetY = renderRect.Top;
            blurConstants.RectWidth = renderRect.Width;
            blurConstants.Amount = _amount;

            return blurConstants;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            _amount = newToken.GetProperty<Int32Property>(PropertyNames.Amount).Value;
            _center.X = (int)((1 + newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Center).ValueX) * (srcArgs.Width / 2.0));
            _center.Y = (int)((1 + newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Center).ValueY) * (srcArgs.Height / 2.0));

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }
    }
}