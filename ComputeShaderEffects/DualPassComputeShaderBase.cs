using System.Drawing;
using System.Runtime.InteropServices;
using PaintDotNet;

namespace ComputeShaderEffects
{
    public abstract class DualPassComputeShaderBase : TiledComputeShaderBase
    {
        private int _pass;

        public int Passes { get; set; }

        protected DualPassComputeShaderBase(string name, Image image, string subMenuName, PaintDotNet.Effects.EffectFlags flags)
            : base(name, image, subMenuName, flags)
        {
            Passes = 2;
        }

        protected virtual void OnBeginPass(int pass, RenderArgs dstArgs, RenderArgs srcArgs)
        {
        }

        protected override void OnPreRender(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            base.OnPreRender(dstArgs, srcArgs);
        }

        protected virtual void OnPreRenderComplete(RenderArgs dstArgs, RenderArgs srcArgs)
        {
            Rectangle[] rois;
            Surface surfaceCopy;
            RenderArgs args;
            RenderArgs currentSourceArgs;
            RenderArgs currentDestinationArgs;
            Rectangle boundingTile;

            if (FullImageSelected(srcArgs.Bounds))
            {
                boundingTile = dstArgs.Bounds;
            }
            else
            {
                boundingTile = EnvironmentParameters.SelectionBounds;
                boundingTile.Inflate(ApronSize, ApronSize);
                boundingTile.Intersect(dstArgs.Bounds);
            }

            rois = SliceRectangles(new Rectangle[] { boundingTile });

            surfaceCopy = new Surface(dstArgs.Width, dstArgs.Height, SurfaceCreationFlags.DoNotZeroFillHint);
            args = new RenderArgs(surfaceCopy);

            currentSourceArgs = srcArgs;
            currentDestinationArgs = args;

            _pass = 1;
            OnBeginPass(_pass, currentDestinationArgs, currentSourceArgs);
            OnRenderRegion(rois, currentDestinationArgs, currentSourceArgs);

            if (Passes == 1)
            {
                CopyRois(EnvironmentParameters.GetSelectionAsPdnRegion().GetRegionScansReadOnlyInt(),
                    dstArgs.Surface,
                    surfaceCopy);
                surfaceCopy.Dispose();
            }
            else
            {
                if (_tmr != null)
                {
                    _tmr = new System.Diagnostics.Stopwatch();
                    _tmr.Start();
                }

                _pass = 2;
                if (FullImageSelected(srcArgs.Bounds))
                {
                    currentSourceArgs = args;
                    currentDestinationArgs = dstArgs;

                    OnBeginPass(_pass, currentDestinationArgs, currentSourceArgs);
                    OnRenderRegion(rois, currentDestinationArgs, currentSourceArgs);
                    surfaceCopy.Dispose();
                }
                else
                {
                    Surface surfaceCopy2 = new Surface(dstArgs.Width, dstArgs.Height, SurfaceCreationFlags.DoNotZeroFillHint);
                    RenderArgs args2 = new RenderArgs(surfaceCopy2);

                    currentSourceArgs = args;
                    currentDestinationArgs = args2;

                    OnBeginPass(_pass, currentDestinationArgs, currentSourceArgs);
                    OnRenderRegion(rois, currentDestinationArgs, currentSourceArgs);
                    surfaceCopy.Dispose();

                    CopyRois(EnvironmentParameters.GetSelectionAsPdnRegion().GetRegionScansReadOnlyInt(),
                        dstArgs.Surface,
                        surfaceCopy2);

                    surfaceCopy2.Dispose();
                }
            }
        }

        protected override sealed void OnRender(Rectangle[] rois, int startIndex, int length)
        {
        }

        private unsafe void CopyRois(Rectangle[] rois, Surface dest, Surface source)
        {
            int COLOR_SIZE = Marshal.SizeOf(typeof(ColorBgra));

            foreach (Rectangle copyRect in rois)
            {
                int length = copyRect.Width * COLOR_SIZE;

                for (int y = copyRect.Top; y < copyRect.Bottom; y++)
                {
                    BufferUtil.Copy(dest.GetPointPointer(copyRect.Left, y), source.GetPointPointer(copyRect.Left, y), length);
                }
            }
        }
    }
}
