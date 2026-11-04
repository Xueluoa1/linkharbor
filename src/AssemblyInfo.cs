using System.Reflection;

[assembly: AssemblyTitle("拾链")]
[assembly: AssemblyDescription("基于 yt-dlp 的 Windows 视频解析与批量下载工具")]
[assembly: AssemblyCompany("雪落a")]
[assembly: AssemblyProduct("拾链")]
[assembly: AssemblyCopyright("Copyright (C) 2026 雪落a")]
[assembly: AssemblyVersion(LinkHarbor.AppInfo.Version)]
[assembly: AssemblyFileVersion(LinkHarbor.AppInfo.Version)]
[assembly: AssemblyInformationalVersion(LinkHarbor.AppInfo.Version)]

namespace LinkHarbor
{
    internal static class AppInfo
    {
        public const string Version = "1.0.1";
        public const string DisplayVersion = "v" + Version;
    }
}
