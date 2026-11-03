using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace LinkHarbor
{
    public sealed class Settings
    {
        public string Folder { get; set; }
        public string Proxy { get; set; }
        public Settings() { Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "下载"); Proxy = ""; }
    }

    public sealed class MediaInfo
    {
        public string Url, Title, Author, Identity;
        public double Duration;
        public bool HasAudio, HasVideo, Live;
        public List<int> Heights = new List<int>();
        public int FormatCount;
        public static MediaInfo FromJson(string json, string url)
        {
            var d = Json.Parse(json);
            if (Json.Text(d, "_type") == "playlist" || d.ContainsKey("entries"))
                throw new InvalidOperationException("此链接是播放列表或频道，请改用其中各个视频的页面链接。");
            var info = new MediaInfo { Url = url, Title = Json.Text(d, "title"), Author = Json.Text(d, "uploader"), Duration = Json.Number(d, "duration"), Live = Json.Text(d, "live_status") == "is_live" };
            string extractor=Json.Text(d,"extractor_key"),id=Json.Text(d,"id");
            info.Identity=String.IsNullOrEmpty(extractor)||extractor.Equals("Generic",StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(id)?url:extractor+":"+id;
            if (info.Live) throw new InvalidOperationException("这是正在直播的内容，请在直播结束后下载回放。");
            if (String.IsNullOrWhiteSpace(info.Title)) info.Title = Json.Text(d, "id");
            if (String.IsNullOrWhiteSpace(info.Title)) info.Title = "未命名媒体";
            object formats;
            if (d.TryGetValue("formats", out formats) && formats is IEnumerable)
            {
                foreach (var obj in (IEnumerable)formats)
                {
                    var f = obj as Dictionary<string, object>;
                    if (f == null || Json.Text(f, "has_drm") == "True") continue;
                    var audio = Json.Text(f, "acodec");
                    var video = Json.Text(f, "vcodec");
                    if (video == "none" && audio == "none") continue;
                    info.FormatCount++;
                    string extension = Json.Text(f, "ext");
                    bool audioOnly = new[] { "mp3", "m4a", "aac", "opus", "ogg", "flac", "wav" }.Contains(extension);
                    info.HasAudio |= audio != "none";
                    info.HasVideo |= video != "none" && !audioOnly;
                    int h = (int)Json.Number(f, "height");
                    if (h > 0 && video != "none" && !info.Heights.Contains(h)) info.Heights.Add(h);
                }
            }
            else
            {
                info.FormatCount = 1;
                info.HasAudio = Json.Text(d, "acodec") != "none";
                info.HasVideo = Json.Text(d, "vcodec") != "none";
            }
            info.Heights.Sort((a, b) => b.CompareTo(a));
            return info;
        }
    }

    public static class Json
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 200 }; }
        public static Dictionary<string, object> Parse(string value) { return Serializer().Deserialize<Dictionary<string, object>>(value); }
        public static string Text(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
        public static double Number(Dictionary<string, object> d, string key) { double n; return Double.TryParse(Text(d, key), NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n : 0; }
    }

    public sealed class Quality
    {
        public string Label;
        public int Height;
        public override string ToString() { return Label; }
    }

    public static class Arguments
    {
        // Windows CommandLineToArgvW quoting, including trailing slashes and embedded quotes.
        public static string Quote(string value)
        {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); slashes = 0; continue; }
                b.Append('\\', slashes); slashes = 0; b.Append(c);
            }
            b.Append('\\', slashes * 2); b.Append('"'); return b.ToString();
        }
        public static string ValidateUrl(string value)
        {
            Uri uri;
            value = value.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || String.IsNullOrWhiteSpace(uri.Host) || value.Any(Char.IsControl))
                throw new ArgumentException("请输入完整的 http:// 或 https:// 视频链接。");
            return value;
        }
        public static List<string> SplitLinks(string value)
        {
            var links=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(string part in value.Split(new[]{'\r','\n',';','；'},StringSplitOptions.RemoveEmptyEntries))
            {string link=part.Trim();if(link.Length>0&&seen.Add(link))links.Add(link);}
            if(links.Count==0)throw new ArgumentException("请输入视频链接；多个链接可用回车换行或分号分隔。");
            return links;
        }
        public static List<string> Common(string tools, string proxy, string cookies)
        {
            var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--encoding", "utf-8", "--no-colors", "--socket-timeout", "25", "--retries", "3", "--extractor-retries", "2", "--no-js-runtimes", "--js-runtimes", "node:" + Path.Combine(tools, "node.exe"), "--ffmpeg-location", tools };
            if (!String.IsNullOrWhiteSpace(proxy))
            {
                Uri uri;
                if (!Uri.TryCreate(proxy.Trim(), UriKind.Absolute, out uri) || !new[] { "http", "https", "socks5", "socks5h", "socks4" }.Contains(uri.Scheme) || String.IsNullOrEmpty(uri.Host))
                    throw new ArgumentException("代理格式不正确，例如 http://127.0.0.1:7890；不用代理时留空。");
                args.AddRange(new[] { "--proxy", proxy.Trim() });
            }
            if (!String.IsNullOrWhiteSpace(cookies))
            {
                if (!File.Exists(cookies)) throw new FileNotFoundException("选择的 Cookie 文件已不存在，请重新选择。");
                args.AddRange(new[] { "--cookies", cookies });
            }
            return args;
        }
        public static List<string> Parse(string tools, string url, string proxy, string cookies)
        {
            var a = Common(tools, proxy, cookies);
            a.AddRange(new[] { "--dump-single-json", "--skip-download", "--playlist-items", "1", "--", ValidateUrl(url) }); return a;
        }
        public static List<string> Download(string tools, MediaInfo info, string folder, string proxy, string cookies, string mode, int height)
        {
            if (String.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) throw new ArgumentException("请选择完整的本地保存路径。");
            var a = Common(tools, proxy, cookies);
            a.AddRange(new[] { "--newline", "--progress", "--progress-delta", "0.3", "--no-simulate", "--no-overwrites", "--continue", "--windows-filenames", "--trim-filenames", "160", "-P", folder, "-o", "%(title)s [%(id)s].%(ext)s", "--progress-template", "download:__PROGRESS__%(progress)j", "--print", "after_move:__FILE__%(filepath)j" });
            a.AddRange(new[]{"--print","before_dl:__STREAMS__%(requested_formats)j"});
            if (mode == "mp3") a.AddRange(new[] { "-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0" });
            else if (mode == "audio") a.AddRange(new[] { "-f", "ba/b", "-x" });
            else
            {
                string filter = height > 0 ? "[height<=" + height.ToString(CultureInfo.InvariantCulture) + "]" : "";
                // Prefer best resolution; MKV can contain all common YouTube video/audio codecs.
                a.AddRange(new[] { "-f", "bv*" + filter + "+ba/b" + filter + "/bv" + filter, "--merge-output-format", "mkv" });
            }
            a.AddRange(new[] { "--", info.Url }); return a;
        }
    }

    public sealed class RunResult
    {
        public int Code;
        public string Output;
        public string Error;
    }

    public sealed class Engine
    {
        public string ToolsPath { get; private set; }
        public Engine(string toolsPath) { ToolsPath = toolsPath; }
        public void Check()
        {
            foreach (string file in new[] { "yt-dlp.exe", "node.exe", "ffmpeg.exe", "ffprobe.exe" })
                if (!File.Exists(Path.Combine(ToolsPath, file))) throw new FileNotFoundException("缺少组件 tools\\" + file + "。请保留整个工具文件夹，不要单独移动启动程序。");
        }
        public Task<RunResult> Run(List<string> args, CancellationToken token, Action<string> onLine, bool capture)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var stdout = new StringBuilder(); var errors = new Queue<string>(); var gate = new object();
                using (var job = new ProcessJob())
                using (var p = new Process())
                {
                    p.StartInfo = new ProcessStartInfo(Path.Combine(ToolsPath, "yt-dlp.exe"), String.Join(" ", args.Select(Arguments.Quote)))
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                    };
                    p.OutputDataReceived += (s, e) => { if (e.Data == null) return; if (capture) lock (gate) stdout.AppendLine(e.Data); if (onLine != null) onLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data == null) return; lock (gate) { errors.Enqueue(e.Data); while (errors.Count > 70) errors.Dequeue(); } if (onLine != null) onLine(e.Data); };
                    p.Start();
                    try { job.Add(p); } catch { try { p.Kill(); } catch { } throw; }
                    using (token.Register(() => job.Kill()))
                    {
                        p.BeginOutputReadLine(); p.BeginErrorReadLine(); p.WaitForExit();
                    }
                    token.ThrowIfCancellationRequested();
                    lock (gate) return new RunResult { Code = p.ExitCode, Output = stdout.ToString(), Error = String.Join(Environment.NewLine, errors) };
                }
            });
        }
    }

    // A kernel job owns the whole download tree, including FFmpeg and JavaScript workers.
    // Closing the application or canceling cannot leave descendants running in the background.
    sealed class ProcessJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits
        {
            public long PerProcessTime, PerJobTime;
            public uint Flags;
            public UIntPtr MinWorkingSet, MaxWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits limits, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateJobObject(SafeFileHandle job, uint code);
        readonly SafeFileHandle handle;
        public ProcessJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
            if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits)))) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new System.ComponentModel.Win32Exception(error); }
        }
        public void Add(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        public void Kill() { TerminateJobObject(handle, 1); }
        public void Dispose() { handle.Dispose(); }
    }
}
