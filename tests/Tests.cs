using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinkHarbor;

class Tests
{
    static int count;
    static string root = AppDomain.CurrentDomain.BaseDirectory;
    static void Check(bool ok, string label) { if (!ok) throw new Exception("FAILED: " + label); count++; Console.WriteLine("PASS " + label); }
    static void Reject(Action action, string label) { bool failed = false; try { action(); } catch (ArgumentException) { failed = true; } Check(failed, label); }
    static void Main(string[] args) { try { Run(args).GetAwaiter().GetResult(); Console.WriteLine("ALL PASSED: " + count); } catch (Exception e) { Console.Error.WriteLine(e); Environment.Exit(1); } }
    static async Task Run(string[] args)
    {
        Check(!AppPaths.Installed && AppPaths.SettingsFile == Path.Combine(root,"settings.json"), "portable settings stay beside program");
        Check(new Settings().Folder == Path.Combine(root,"下载"), "portable default downloads preserved");
        string marker = Path.Combine(root,"installed-mode");
        try {
            File.WriteAllText(marker,"test");
            Check(AppPaths.Installed && AppPaths.SettingsFile == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LinkHarbor","settings.json"), "installed settings outside application directory");
            Check(new Settings().Folder == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"拾链"), "installed downloads in user videos directory");
        } finally { File.Delete(marker); }
        Check(Arguments.ValidateUrl(" https://example.com/watch?v=1&list=2 ") == "https://example.com/watch?v=1&list=2", "trim URL without losing query");
        Reject(() => Arguments.ValidateUrl("file:///C:/Windows/win.ini"), "reject local file URLs");
        Reject(() => Arguments.ValidateUrl("--exec calc.exe"), "reject option injection");
        Reject(() => Arguments.ValidateUrl("https://example.com/\nhello"), "reject URL control characters");
        Reject(() => Arguments.Common("tools", "not-a-proxy", ""), "validate proxy");
        var fixture = "{\"title\":\"测试视频\",\"duration\":12,\"formats\":[{\"vcodec\":\"avc1\",\"acodec\":\"none\",\"height\":1080},{\"vcodec\":\"avc1\",\"acodec\":\"none\",\"height\":720},{\"vcodec\":\"none\",\"acodec\":\"aac\"},{\"vcodec\":\"avc1\",\"height\":2160,\"has_drm\":true}]}";
        var info = MediaInfo.FromJson(fixture, "https://example.com/watch");
        Check(info.HasAudio && info.HasVideo && info.Heights.SequenceEqual(new[] { 1080, 720 }), "parse real format list, exclude DRM");
        Check(info.FormatCount == 3, "format count excludes DRM");
        Check(MediaInfo.FromJson("{\"formats\":[{\"ext\":\"mp4\"}]}", "https://example.com").HasVideo, "generic video without codec metadata");
        bool rejectPlaylist = false; try { MediaInfo.FromJson("{\"_type\":\"playlist\",\"entries\":[]}", "https://example.com"); } catch (InvalidOperationException) { rejectPlaylist = true; }
        Check(rejectPlaylist, "reject playlist");
        bool rejectLive = false; try { MediaInfo.FromJson("{\"live_status\":\"is_live\"}", "https://example.com"); } catch (InvalidOperationException) { rejectLive = true; }
        Check(rejectLive, "reject ongoing livestream");
        var dl = Arguments.Download("tools", info, root, "", "", "video", 720);
        Check(dl.Contains("bv*[height<=720]+ba/b[height<=720]/bv[height<=720]") && dl[dl.Count - 2] == "--", "bounded resolution and URL separator");
        Check(Arguments.Download("tools", info, root, "", "", "mp3", 0).Contains("--audio-format"), "MP3 conversion arguments");

        var fake = new Engine(Path.Combine(root, "fake-tools"));
        var sample = new[] { "", "path with spaces", "中文路径", "ends\\", "quote\"inside", "slashes\\\\\"quote", "https://example.com/?a=1&b=%2F", "$(calc) & echo nope" };
        var echo = await fake.Run(sample.ToList(), CancellationToken.None, null, true);
        var received = Json.Serializer().Deserialize<string[]>(echo.Output.TrimStart('\uFEFF'));
        Check(sample.SequenceEqual(received), "Windows argv round trip including shell metacharacters");
        string pidPath = Path.Combine(root, "child-pid.txt");
        using (var cancel = new CancellationTokenSource())
        {
            var running = fake.Run(new List<string> { "--sleep", pidPath }, cancel.Token, null, false);
            for (int i = 0; i < 100 && !File.Exists(pidPath); i++) await Task.Delay(50);
            Check(File.Exists(pidPath), "cancellation fixture started child");
            int pid = Int32.Parse(File.ReadAllText(pidPath)); var timer = Stopwatch.StartNew(); cancel.Cancel();
            bool canceled = false; try { await running; } catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "cancellation returns canceled state");
            Check(timer.Elapsed.TotalSeconds < 5, "cancellation finishes within 5 seconds");
            bool gone = false; try { gone = Process.GetProcessById(pid).HasExited; } catch (ArgumentException) { gone = true; }
            Check(gone, "cancellation terminates child process tree");
        }
        if (args.Length == 0) return;
        string tools = Path.GetFullPath(Path.Combine(root, "../tools"));
        var engine = new Engine(tools); engine.Check();
        string baseUrl = args[0];
        var parsed = await engine.Run(Arguments.Parse(tools, baseUrl + "/video.mp4", "", ""), CancellationToken.None, null, true);
        Check(parsed.Code == 0, "actual yt-dlp parses local HTTP video: " + parsed.Error);
        var direct = MediaInfo.FromJson(parsed.Output, baseUrl + "/video.mp4");
        Check(direct.HasVideo && direct.HasAudio, "generic HTTP video enables download");
        var manifest = await engine.Run(Arguments.Parse(tools, baseUrl + "/manifest.mpd", "", ""), CancellationToken.None, null, true);
        Check(manifest.Code == 0, "actual yt-dlp parses DASH manifest: " + manifest.Error);
        var dash = MediaInfo.FromJson(manifest.Output, baseUrl + "/manifest.mpd");
        Check(dash.HasVideo && dash.HasAudio && dash.Heights.Contains(180), "DASH video/audio and quality detection");
        string outDir = Path.Combine(root, "下载 空格", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outDir);
        string file = ""; int events = 0;
        Action<string> progress = line => { if (line.StartsWith("__FILE__")) file = Json.Serializer().Deserialize<string>(line.Substring(8)); if (line.StartsWith("__PROGRESS__")) { var p = Json.Parse(line.Substring(12)); events++; } };
        var video = await engine.Run(Arguments.Download(tools, dash, outDir, "", "", "video", 180), CancellationToken.None, progress, false);
        Check(video.Code == 0 && File.Exists(file) && Path.GetExtension(file) == ".mkv", "actual download and merge MKV: " + video.Error);
        Check(events > 0, "structured progress events received");
        Check(Probe(tools, file).Contains("video") && Probe(tools, file).Contains("audio"), "merged file has video AND audio streams");
        var modified = File.GetLastWriteTimeUtc(file);
        var repeated = await engine.Run(Arguments.Download(tools, dash, outDir, "", "", "video", 180), CancellationToken.None, progress, false);
        Check(repeated.Code == 0 && File.GetLastWriteTimeUtc(file) == modified, "repeat download preserves completed file");
        file = "";
        var mp3 = await engine.Run(Arguments.Download(tools, direct, outDir, "", "", "mp3", 0), CancellationToken.None, progress, false);
        Check(mp3.Code == 0 && File.Exists(file) && Path.GetExtension(file) == ".mp3", "actual MP3 conversion: " + mp3.Error);
        Check(Probe(tools, file).Contains("mp3"), "MP3 file contains MP3 audio codec");
        file = "";
        var audio = await engine.Run(Arguments.Download(tools, dash, outDir, "", "", "audio", 0), CancellationToken.None, progress, false);
        Check(audio.Code == 0 && File.Exists(file) && !Probe(tools, file).Contains("video"), "original audio extraction");
        var failed = await engine.Run(Arguments.Parse(tools, baseUrl + "/missing", "", ""), CancellationToken.None, null, true);
        Check(failed.Code != 0 && failed.Error.Length > 0, "HTTP error propagated");
    }
    static string Probe(string tools, string file)
    {
        using (var p = Process.Start(new ProcessStartInfo(Path.Combine(tools, "ffprobe.exe"), "-v error -show_entries stream=codec_type,codec_name -of json " + Arguments.Quote(file)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
        { string result = p.StandardOutput.ReadToEnd(); p.WaitForExit(); if (p.ExitCode != 0) throw new Exception("ffprobe failed"); return result; }
    }
}
