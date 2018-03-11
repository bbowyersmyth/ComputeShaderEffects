using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

namespace ComputeShaderEffects.RadialBlur
{
    [StructLayout(LayoutKind.Sequential)]
    struct Constants
    {
        public float Width;
        public float Height;
        public float CenterX;
        public float CenterY;
        public float RectOffsetX;
        public float RectOffsetY;
        public int RectWidth;
        public float Theta;
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "(GPU) Radial Blur")]
    public class RadialBlurGPU : ImageComputeShaderBase
    {
        private double _angle;
        private Point _center;
        private float _theta;

        public enum PropertyNames
        {
            Angle,
            Center
        }

        public static string StaticName => "(GPU) Radial Blur";

        public static Bitmap StaticIcon => new Bitmap(typeof(RadialBlurGPU), "RadialBlurIcon.png");

        public RadialBlurGPU()
            : base(RadialBlurGPU.StaticName, RadialBlurGPU.StaticIcon, SubmenuNames.Blurs, EffectFlags.Configurable | EffectFlags.SingleThreaded)
        {
            MaximumRegionWidth = 2048;
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = PropertyBasedEffect.CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.Angle, ControlInfoPropertyNames.DisplayName, "Angle");
            configUI.FindControlForPropertyName(PropertyNames.Angle).ControlType.Value = PropertyControlType.AngleChooser;
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

            props.Add(new DoubleProperty(PropertyNames.Angle, 2.0, 0.0, 360.0));
            props.Add(new DoubleVectorProperty(PropertyNames.Center, Pair.Create<double, double>(0.0, 0.0), Pair.Create<double, double>(-2.0, -2.0), Pair.Create<double, double>(2.0, 2.0)));

            return new PropertyCollection(props);
        }

        protected override void OnPreRender(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            string shaderPath;

            base.OnPreRender(dstArgs, srcArgs);

            try
            {
                if (IsLargeImage)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.RadialBlurBuff.fx";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.RadialBlur.fx";
                }

                // Create DirectX device and shaders
                SetShader(shaderPath);
                Consts = new Constants();
                _theta = (float)this._angle / (float)1374.0 * (float)0.0174532925; // (float)Math.Cos(this.angle / 57.295779513082320876798154814105 / 1375.0);
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
            blurConstants.Theta = _theta;

            return blurConstants;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            _angle = newToken.GetProperty<DoubleProperty>(PropertyNames.Angle).Value;
            _center.X = (int)((1 + newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Center).ValueX) * (srcArgs.Width / 2.0));
            _center.Y = (int)((1 + newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Center).ValueY) * (srcArgs.Height / 2.0));

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }
    }
}