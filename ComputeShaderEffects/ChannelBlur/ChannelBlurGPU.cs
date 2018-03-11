using System;
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
    public class ChannelBlurGPU : DualPassComputeShaderBase
    {
        private static int BUFF_SIZE = Marshal.SizeOf(typeof(uint));

        private int _redRadius;
        private int _greenRadius;
        private int _blueRadius;
        private int _alphaRadius;
        private bool _repeatEdgePixels;
        private Dimensions _blurDimensions;
        private float[] _redWeights;
        private float[] _greenWeights;
        private float[] _blueWeights;
        private float[] _alphaWeights;
        private SharpDX.DataStream _weightRedData;
        private SharpDX.DataStream _weightGreenData;
        private SharpDX.DataStream _weightBlueData;
        private SharpDX.DataStream _weightAlphaData;
        private SharpDX.Direct3D11.Buffer _weightRedBuffer;
        private SharpDX.Direct3D11.Buffer _weightGreenBuffer;
        private SharpDX.Direct3D11.Buffer _weightBlueBuffer;
        private SharpDX.Direct3D11.Buffer _weightAlphaBuffer;
        private ShaderResourceView _weightRedView;
        private ShaderResourceView _weightGreenView;
        private ShaderResourceView _weightBlueView;
        private ShaderResourceView _weightAlphaView;
        private bool _isVert = false;

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

        public static string StaticName => "(GPU) Channel Blur";

        public static Bitmap StaticIcon => new Bitmap(typeof(ChannelBlurGPU), "ChannelBlurIcon.png");

        public ChannelBlurGPU()
            : base(ChannelBlurGPU.StaticName, ChannelBlurGPU.StaticIcon, SubmenuNames.Blurs, EffectFlags.Configurable | EffectFlags.SingleThreaded)
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
            props.Add(StaticListChoiceProperty.CreateForEnum(PropertyNames.BlurDimensions, Dimensions.HorizontalAndVertical, false));
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
                    _redWeights = CreateBlurWeights(_redRadius);
                    _greenWeights = CreateBlurWeights(_greenRadius);
                    _blueWeights = CreateBlurWeights(_blueRadius);
                    _alphaWeights = CreateBlurWeights(_alphaRadius);

                    _weightRedView = CreateArrayView(_redWeights, Device, out _weightRedData, out _weightRedBuffer);
                    _weightGreenView = CreateArrayView(_greenWeights, Device, out _weightGreenData, out _weightGreenBuffer);
                    _weightBlueView = CreateArrayView(_blueWeights, Device, out _weightBlueData, out _weightBlueBuffer);
                    _weightAlphaView = CreateArrayView(_alphaWeights, Device, out _weightAlphaData, out _weightAlphaBuffer);

                    AddResourceViews(new ShaderResourceView[] { _weightRedView, _weightGreenView, _weightBlueView, _weightAlphaView });

                    // Control number of passes based on dimensions
                    Passes = (_blurDimensions == Dimensions.HorizontalAndVertical) ? 2 : 1;

                    ApronSize = Math.Max(Math.Max(Math.Max(_redRadius, _greenRadius), _blueRadius), _alphaRadius);
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
                    shaderPath = "ComputeShaderEffects.Shaders.ChannelBlurHorizClamp";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ChannelBlurHoriz";
                }
                _isVert = false;
            }
            else
            {
                if (_repeatEdgePixels)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ChannelBlurVertClamp";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.ChannelBlurVert";
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
            Constants blurConstants = (Constants)consts;

            blurConstants.RedWeightLength = (uint)_redWeights.Length;
            blurConstants.GreenWeightLength = (uint)_greenWeights.Length;
            blurConstants.BlueWeightLength = (uint)_blueWeights.Length;
            blurConstants.AlphaWeightLength = (uint)_alphaWeights.Length;
            blurConstants.MaxRadius = Math.Max(Math.Max(Math.Max(_redRadius, _greenRadius), _blueRadius), _alphaRadius);
            blurConstants.Width = tileRect.Width - 1;
            blurConstants.Height = tileRect.Height - 1;
            blurConstants.RectOffsetX = renderRect.Left - tileRect.Left;
            blurConstants.RectOffsetY = renderRect.Top - tileRect.Top;
            blurConstants.RectWidth = renderRect.Width;

            return blurConstants;
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
            _redRadius = newToken.GetProperty<Int32Property>(PropertyNames.RedRadius).Value;
            _greenRadius = newToken.GetProperty<Int32Property>(PropertyNames.GreenRadius).Value;
            _blueRadius = newToken.GetProperty<Int32Property>(PropertyNames.BlueRadius).Value;
            _alphaRadius = newToken.GetProperty<Int32Property>(PropertyNames.AlphaRadius).Value;
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
            if (_weightRedData != null)
            {
                _weightRedData.Close();
                _weightRedData.Dispose();
            }
            if (_weightGreenData != null)
            {
                _weightGreenData.Close();
                _weightGreenData.Dispose();
            }
            if (_weightBlueData != null)
            {
                _weightBlueData.Close();
                _weightBlueData.Dispose();
            }
            if (_weightAlphaData != null)
            {
                _weightAlphaData.Close();
                _weightAlphaData.Dispose();
            }
            _weightRedBuffer.DisposeIfNotNull();
            _weightGreenBuffer.DisposeIfNotNull();
            _weightBlueBuffer.DisposeIfNotNull();
            _weightAlphaBuffer.DisposeIfNotNull();
            _weightRedView.DisposeIfNotNull();
            _weightGreenView.DisposeIfNotNull();
            _weightBlueView.DisposeIfNotNull();
            _weightAlphaView.DisposeIfNotNull();
        }
    }
}
