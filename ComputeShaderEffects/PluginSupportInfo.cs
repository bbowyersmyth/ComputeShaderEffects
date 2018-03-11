using System;
using PaintDotNet;

namespace ComputeShaderEffects
{
    public class PluginSupportInfo : IPluginSupportInfo
    {
        public string Author => "Bruce Bowyer-Smyth";

        public string Copyright => "Bruce Bowyer-Smyth";

        public string DisplayName => "Motion Blur (GPU)";

        public Version Version => GetType().Assembly.GetName().Version;

        public Uri WebsiteUri => new Uri("https://github.com/bbowyersmyth/ComputeShaderEffects");
    }
}
