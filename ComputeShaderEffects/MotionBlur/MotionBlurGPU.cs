using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Rendering;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;

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
            this.X = x;
            this.Y = y;
        }
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "(GPU) Motion Blur")]
    public class MotionBlurGPU : ComputeShaderEffects.TiledComputeShaderBase
    {
        private static int BUFF_SIZE = Marshal.SizeOf(typeof(uint)); 
        
        private double angle;
        private bool centered;
        private int distance;
        private bool repeatEdgePixels;
        private PointType[] points;
        private SharpDX.DataStream pointData;
        private SharpDX.Direct3D11.Buffer pointBuffer;
        private ShaderResourceView pointView;
        
        public enum PropertyNames
        {
            Angle,
            Centered,
            Distance,
            RepeatEdgePixels
        }

        public static string StaticName
        {
            get
            {
                return "(GPU) Motion Blur";
            }
        }

        public static Bitmap StaticIcon
        {
            get
            {
                return new Bitmap(typeof(MotionBlurGPU), "MotionBlurIcon.png");
            }
        }

        public MotionBlurGPU()
            : base(MotionBlurGPU.StaticName, MotionBlurGPU.StaticIcon, SubmenuNames.Blurs, PaintDotNet.Effects.EffectFlags.Configurable | PaintDotNet.Effects.EffectFlags.SingleThreaded)
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
                if (this.repeatEdgePixels)
                {
                    shaderPath = "ComputeShaderEffects.Shaders.MotionBlurClamp.fx";
                }
                else
                {
                    shaderPath = "ComputeShaderEffects.Shaders.MotionBlur.fx";
                }

                base.Consts = new Constants();
                base.SetShader(shaderPath);

                if (base.IsInitialized)
                {
                    // Copy points
                    pointData = new SharpDX.DataStream(this.points.Length * Marshal.SizeOf(typeof(PointType)), true, true);

                    foreach (PointType pt in this.points)
                    {
                        pointData.Write<PointType>(pt);
                    }
                    
                    // Create the compute shader buffer and views for common data
                    pointBuffer = CreateBuffer(base.Device, pointData, Marshal.SizeOf(typeof(PointType)));
                    pointView = CreateView(base.Device, pointBuffer);
                    base.AddResourceViews(new ShaderResourceView[] { pointView } );

                    base.ApronSize = (int)Math.Ceiling(Math.Max(
                            Math.Abs(this.points[this.points.Length-1].X), 
                            Math.Abs(this.points[this.points.Length-1].Y)));
                }
            }
            catch (SharpDX.SharpDXException ex)
            {
                MessageBox.Show(ex.Message);
                base.IsInitialized = false;
            }
        }

        protected override object SetRenderOptions(Rectangle tileRect, Rectangle renderRect, object consts)
        {
            Constants motionBlurConstants = (Constants)consts;

            motionBlurConstants.PointsCount = points.Length;
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
            this.angle = newToken.GetProperty<DoubleProperty>(PropertyNames.Angle).Value;
            this.distance = newToken.GetProperty<Int32Property>(PropertyNames.Distance).Value;
            this.centered = newToken.GetProperty<BooleanProperty>(PropertyNames.Centered).Value;
            this.repeatEdgePixels = newToken.GetProperty<BooleanProperty>(PropertyNames.RepeatEdgePixels).Value;
            PointF start = new PointF(0f, 0f);
            double theta = (((this.angle + 180.0) * 2.0) * 3.1415926535897931) / 360.0;
            double alpha = this.distance;
            double x = alpha * Math.Cos(theta);
            double y = alpha * Math.Sin(theta);
            PointF end = new PointF((float)x, (float)-y);
            int pointCount;

            if (this.centered)
            {
                start.X = -end.X / 2f;
                start.Y = -end.Y / 2f;
                end.X /= 2f;
                end.Y /= 2f;
            }

            pointCount = ((1 + this.distance) * 3) / 2;
            this.points = new PointType[pointCount / 2];

            if (this.points.Length == 1)
            {
                points[0].X = 0;
                points[0].Y = 0;
            }
            else
            {
                for (int i = 0; i < this.points.Length; i++)
                {
                    float frac = ((float)i * 2) / ((float)(pointCount - 1));
                    PointF pt = Lerp(start, end, frac);
                    points[i].X = (float)pt.X;
                    points[i].Y = (float)pt.Y;
                }
            }

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        private void CleanUpLocal()
        {
            if (pointData != null)
            {
                pointData.Close();
                pointData.Dispose();
            }
            pointBuffer.DisposeIfNotNull();
            pointView.DisposeIfNotNull();
        }

        private static PointF Lerp(PointF a, PointF b, float t)
        {
            return new PointF(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        }
    }
}