using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using SharpDX.Direct3D11;

namespace ComputeShaderEffects.GaussianBlur
{
    [StructLayout(LayoutKind.Sequential)]
    struct Constants
    {
        public int Width;
        public int Height;
        public int WeightLength;
        public int Radius;
        public int RectOffsetX;
        public int RectOffsetY;
        public int RectWidth;
        public int Padding2;
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "(GPU) Gaussian Blur")]
    public class GaussianBlurGPU : DualPassComputeShaderBase
    {
        private int _radius;
        private bool _repeatEdgePixels;
        private Dimensions _blurDimensions;
        private float[] _weights;
        private SharpDX.DataStream _weightData;
        private SharpDX.Direct3D11.Buffer _weightBuffer;
        private ShaderResourceView _weightView;
        private bool _isVert = false;

        public enum PropertyNames
        {
            Radius,
            RepeatEdgePixels,
            BlurDimensions
        }

        public enum Dimensions
        {
            HorizontalAndVertical,
            HorizontalOnly,
            VerticalOnly
        }

        public static string StaticName => "(GPU) Gaussian Blur";

        public static Bitmap StaticIcon => new Bitmap(typeof(GaussianBlurGPU), "GaussianBlurIcon.png");

        public GaussianBlurGPU()
            : base(GaussianBlurGPU.StaticName, GaussianBlurGPU.StaticIcon, SubmenuNames.Blurs, EffectFlags.Configurable | EffectFlags.SingleThreaded)
        {
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = PropertyBasedEffect.CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.Radius, ControlInfoPropertyNames.DisplayName, "Radius");
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

            props.Add(new Int32Property(PropertyNames.Radius, 2, 0, 200));
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
                if (IsInitialized)
                {
                    // Copy weights
                    _weights = CreateBlurWeights(_radius);
                    _weightView = CreateArrayView(_weights, Device, out _weightData, out _weightBuffer);
                    AddResourceViews(new ShaderResourceView[] { _weightView });

                    // Control number of passes based on dimensions
                    Passes = (_blurDimensions == Dimensions.HorizontalAndVertical) ? 2 : 1;

                    ApronSize = _radius;
                }
            }
            catch (SharpDX.SharpDXException ex)
            {
                MessageBox.Show(ex.Message);
                IsInitialized = false;
            }

            base.OnPreRenderComplete(dstArgs, srcArgs);
        }

        protected override void OnBeginPass(int pass, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            string shaderPath;

            if (pass == 1 && (_blurDimensions == Dimensions.HorizontalAndVertical || _blurDimensions == Dimensions.HorizontalOnly))
            {
                if (_repeatEdgePixels)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.GaussianBlurHorizClamp";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.GaussianBlurHoriz";
                }
                _isVert = false;
            }
            else
            {
                if (_repeatEdgePixels)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.GaussianBlurVertClamp";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.GaussianBlurVert";
                }
                _isVert = true;
            }

            shaderPath += ".fx";

            Consts = new Constants();
            SetShader(shaderPath);

            base.OnBeginPass(pass, dstArgs, srcArgs);
        }

        protected override object SetRenderOptions(Rectangle tileRect, Rectangle renderRect, object consts)
        {
            Constants gaussianBlurConstants = (Constants)consts;

            gaussianBlurConstants.Width = tileRect.Width - 1;
            gaussianBlurConstants.Height = tileRect.Height - 1;
            gaussianBlurConstants.WeightLength = _weights.Length;
            gaussianBlurConstants.Radius = _radius;
            gaussianBlurConstants.RectOffsetX = renderRect.Left - tileRect.Left;
            gaussianBlurConstants.RectOffsetY = renderRect.Top - tileRect.Top;
            gaussianBlurConstants.RectWidth = renderRect.Width;

            return gaussianBlurConstants;
        }

        protected override Rectangle AddApron(Rectangle rect, int apronRadius, Rectangle maxBounds)
        {
            if (_isVert)
                rect.Inflate(0, apronRadius);
            else
                rect.Inflate(apronRadius, 0);

            rect.Intersect(maxBounds);

            return rect;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            _radius = newToken.GetProperty<Int32Property>(PropertyNames.Radius).Value;
            _repeatEdgePixels = newToken.GetProperty<BooleanProperty>(PropertyNames.RepeatEdgePixels).Value;
            _blurDimensions = (Dimensions)newToken.GetProperty<StaticListChoiceProperty>(PropertyNames.BlurDimensions).Value;

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
            if (_weightData != null)
            {
                _weightData.Close();
                _weightData.Dispose();
            }
            _weightBuffer.DisposeIfNotNull();
            _weightView.DisposeIfNotNull();
        }
    }
}