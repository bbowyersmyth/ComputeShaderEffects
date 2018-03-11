using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using SharpDX.Direct3D11;

namespace ComputeShaderEffects.MotionBlur
{
    [StructLayout(LayoutKind.Sequential)]
    struct Constants
    {
        public int Width;
        public int Height;
        public int PointsCount;
        public int Padding;
        public int RectOffsetX;
        public int RectOffsetY;
        public int RectWidth;
        public int Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PointType
    {
        public float X;
        public float Y;

        public PointType(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "(GPU) Motion Blur")]
    public class MotionBlurGPU : TiledComputeShaderBase
    {
        private double _angle;
        private bool _centered;
        private int _distance;
        private bool _repeatEdgePixels;
        private PointType[] _points;
        private SharpDX.DataStream _pointData;
        private SharpDX.Direct3D11.Buffer _pointBuffer;
        private ShaderResourceView _pointView;

        public enum PropertyNames
        {
            Angle,
            Centered,
            Distance,
            RepeatEdgePixels
        }

        public static string StaticName => "(GPU) Motion Blur";

        public static Bitmap StaticIcon => new Bitmap(typeof(MotionBlurGPU), "MotionBlurIcon.png");

        public MotionBlurGPU()
            : base(MotionBlurGPU.StaticName, MotionBlurGPU.StaticIcon, SubmenuNames.Blurs, EffectFlags.Configurable | EffectFlags.SingleThreaded)
        {
            DimensionX = 256;
            DimensionY = 1;
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = PropertyBasedEffect.CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.Angle, ControlInfoPropertyNames.DisplayName, "Angle");
            configUI.SetPropertyControlType(PropertyNames.Angle, PropertyControlType.AngleChooser);
            configUI.SetPropertyControlValue(PropertyNames.Centered, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlValue(PropertyNames.Centered, ControlInfoPropertyNames.Description, "Centered");
            configUI.SetPropertyControlValue(PropertyNames.Distance, ControlInfoPropertyNames.DisplayName, "Distance");
            configUI.SetPropertyControlValue(PropertyNames.RepeatEdgePixels, ControlInfoPropertyNames.DisplayName, "Edge Behavior");
            configUI.SetPropertyControlValue(PropertyNames.RepeatEdgePixels, ControlInfoPropertyNames.Description, "Repeat Edge Pixels");

            return configUI;
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>();

            props.Add(new DoubleProperty(PropertyNames.Angle, 25.0, -180.0, 180.0));
            props.Add(new BooleanProperty(PropertyNames.Centered, true));
            props.Add(new Int32Property(PropertyNames.Distance, 10, 1, 200));
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
            string shaderPath;

            CleanUpLocal();
            base.OnPreRender(dstArgs, srcArgs);

            try
            {
                if (_repeatEdgePixels)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.MotionBlurClamp.fx";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.MotionBlur.fx";
                }

                Consts = new Constants();
                SetShader(shaderPath);

                if (IsInitialized)
                {
                    // Copy points
                    _pointData = new SharpDX.DataStream(_points.Length * Marshal.SizeOf(typeof(PointType)), true, true);

                    foreach (PointType pt in _points)
                    {
                        _pointData.Write(pt);
                    }

                    // Create the compute shader buffer and views for common data
                    _pointBuffer = CreateBuffer(Device, _pointData, Marshal.SizeOf(typeof(PointType)));
                    _pointView = CreateView(Device, _pointBuffer);
                    AddResourceViews(new ShaderResourceView[] { _pointView });

                    ApronSize = (int)Math.Ceiling(Math.Max(
                        Math.Abs(_points[_points.Length - 1].X),
                        Math.Abs(_points[_points.Length - 1].Y)));
                }
            }
            catch (SharpDX.SharpDXException ex)
            {
                MessageBox.Show(ex.Message);
                IsInitialized = false;
            }
        }

        protected override object SetRenderOptions(Rectangle tileRect, Rectangle renderRect, object consts)
        {
            Constants motionBlurConstants = (Constants)consts;

            motionBlurConstants.PointsCount = _points.Length;
            motionBlurConstants.Padding = 0;
            motionBlurConstants.Padding2 = 0;
            motionBlurConstants.Width = tileRect.Width - 1;
            motionBlurConstants.Height = tileRect.Height - 1;
            motionBlurConstants.RectOffsetX = renderRect.Left - tileRect.Left;
            motionBlurConstants.RectOffsetY = renderRect.Top - tileRect.Top;
            motionBlurConstants.RectWidth = renderRect.Width;

            return motionBlurConstants;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            _angle = newToken.GetProperty<DoubleProperty>(PropertyNames.Angle).Value;
            _distance = newToken.GetProperty<Int32Property>(PropertyNames.Distance).Value;
            _centered = newToken.GetProperty<BooleanProperty>(PropertyNames.Centered).Value;
            _repeatEdgePixels = newToken.GetProperty<BooleanProperty>(PropertyNames.RepeatEdgePixels).Value;
            PointF start = new PointF(0f, 0f);
            double theta = (((_angle + 180.0) * 2.0) * 3.1415926535897931) / 360.0;
            double alpha = _distance;
            double x = alpha * Math.Cos(theta);
            double y = alpha * Math.Sin(theta);
            PointF end = new PointF((float)x, (float)-y);
            int pointCount;

            if (_centered)
            {
                start.X = -end.X / 2f;
                start.Y = -end.Y / 2f;
                end.X /= 2f;
                end.Y /= 2f;
            }

            pointCount = ((1 + _distance) * 3) / 2;
            _points = new PointType[pointCount / 2];

            if (_points.Length == 1)
            {
                _points[0].X = 0;
                _points[0].Y = 0;
            }
            else
            {
                for (int i = 0; i < _points.Length; i++)
                {
                    float frac = ((float)i * 2) / ((float)(pointCount - 1));
                    PointF pt = Lerp(start, end, frac);
                    _points[i].X = (float)pt.X;
                    _points[i].Y = (float)pt.Y;
                }
            }

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        private void CleanUpLocal()
        {
            if (_pointData != null)
            {
                _pointData.Close();
                _pointData.Dispose();
            }
            _pointBuffer.DisposeIfNotNull();
            _pointView.DisposeIfNotNull();
        }

        private static PointF Lerp(PointF a, PointF b, float t)
        {
            return new PointF(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        }
    }
}