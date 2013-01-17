using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using PaintDotNet;
using System.Runtime.InteropServices;

namespace ComputeShaderEffects
{
    public abstract class DualPassComputeShaderBase : TiledComputeShaderBase 
    {
        private int pass;

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
                boundingTile = this.EnvironmentParameters.GetSelection(dstArgs.Bounds).GetBoundsInt();
                boundingTile.Inflate(base.ApronSize, base.ApronSize);
                boundingTile.Intersect(dstArgs.Bounds);
            }

            rois = base.SliceRectangles(new Rectangle[] { boundingTile });

            surfaceCopy = new Surface(dstArgs.Width, dstArgs.Height, SurfaceCreationFlags.DoNotZeroFillHint); //dstArgs.Surface.Clone();
            args = new RenderArgs(surfaceCopy);

            currentSourceArgs = srcArgs;
            currentDestinationArgs = args;

            this.pass = 1;
            OnBeginPass(this.pass, currentDestinationArgs, currentSourceArgs);
            OnRenderRegion(rois, currentDestinationArgs, currentSourceArgs);

            if (Passes == 1)
            {
                CopyRois(this.EnvironmentParameters.GetSelection(dstArgs.Bounds).GetRegionScansInt(),
                    dstArgs.Surface,
                    surfaceCopy);
                surfaceCopy.Dispose();
            }
            else
            {
                this.pass = 2;
                if (FullImageSelected(srcArgs.Bounds))
                {
                    currentSourceArgs = args;
                    currentDestinationArgs = dstArgs;

                    OnBeginPass(this.pass, currentDestinationArgs, currentSourceArgs);
                    OnRenderRegion(rois, currentDestinationArgs, currentSourceArgs);
                    surfaceCopy.Dispose();
                }
                else
                {
                    Surface surfaceCopy2;
                    RenderArgs args2;
                    surfaceCopy2 = new Surface(dstArgs.Width, dstArgs.Height, SurfaceCreationFlags.DoNotZeroFillHint);
                    args2 = new RenderArgs(surfaceCopy2);

                    currentSourceArgs = args;
                    currentDestinationArgs = args2;

                    OnBeginPass(this.pass, currentDestinationArgs, currentSourceArgs);
                    OnRenderRegion(rois, currentDestinationArgs, currentSourceArgs);
                    surfaceCopy.Dispose();

                    CopyRois(this.EnvironmentParameters.GetSelection(dstArgs.Bounds).GetRegionScansInt(),
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
                for (int y = copyRect.Top; y < copyRect.Bottom; y++)
                {
                    //ColorBgra* pSrcPixels = source.GetPointAddressUnchecked(copyRect.Left, y);
                    //ColorBgra* pDstPixels = dest.GetPointAddressUnchecked(copyRect.Left, y);

                    //PaintDotNet.SystemLayer.Memory.Copy(pDstPixels, pSrcPixels, (ulong)(copyRect.Width * COLOR_SIZE));
                    CustomCopy(dest.GetPointAddressUnchecked(copyRect.Left, y), source.GetPointAddressUnchecked(copyRect.Left, y), copyRect.Width * COLOR_SIZE);
                }
            }
        }
    }
}
