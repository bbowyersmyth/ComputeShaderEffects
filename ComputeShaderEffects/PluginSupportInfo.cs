using PaintDotNet;
using System;

namespace ComputeShaderEffects
{
    public class PluginSupportInfo : IPluginSupportInfo
    {
        public string Author
        {
            get
            {
                return "Bruce Bowyer-Smyth";
            }
        }

        public string Copyright
        {
            get
            {
                return "Bruce Bowyer-Smyth";
            }
        }

        public string DisplayName
        {
            get
            {
                return "Motion Blur (GPU)";
            }
        }

        public Version Version
        {
            get
            {
                return base.GetType().Assembly.GetName().Version;
            }
        }

        public Uri WebsiteUri
        {
            get
            {
                return new Uri("http://www.wmf2wpf.com");
            }
        }
    }
}
