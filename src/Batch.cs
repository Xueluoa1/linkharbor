using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace LinkHarbor
{
    public enum JobState { Waiting, Parsing, Ready, Queued, Downloading, Processing, Complete, Failed, Canceled, Duplicate }

    public sealed class JobView
    {
        public int Number;
        public string Url,Title,Message,File,Log;
        public JobState State;
        public double Progress,Speed,Bytes,Total,Eta;
    }

    public sealed class BatchJob
    {
        readonly object sync=new object();
        readonly Dictionary<string,double> streams=new Dictionary<string,double>();
        readonly JobView view;
        int streamCount=1;
        bool converting;
        public MediaInfo Media {get;private set;}
        public BatchJob(int number,string url){view=new JobView{Number=number,Url=url,Title=url,Message="等待解析",Log="",File="",State=JobState.Waiting};}
        public JobView Snapshot()
        {
            lock(sync)return new JobView{Number=view.Number,Url=view.Url,Title=view.Title,Message=view.Message,File=view.File,Log=view.Log,State=view.State,Progress=view.Progress,Speed=view.Speed,Bytes=view.Bytes,Total=view.Total,Eta=view.Eta};
        }
        public void SetState(JobState state,string message)
        {lock(sync){view.State=state;view.Message=message;if(state!=JobState.Downloading){view.Speed=0;view.Eta=0;}}}
        public void Parsed(MediaInfo media){lock(sync){Media=media;view.Title=media.Title;view.State=JobState.Ready;view.Message="解析完成，等待下载";}}
        public void ResetDownload(bool convert=false)
        {lock(sync){converting=convert;streams.Clear();streamCount=1;view.Progress=0;view.Speed=0;view.Bytes=0;view.Total=0;view.Eta=0;view.File="";view.State=JobState.Queued;view.Message="等待下载名额";}}
        public void ConversionProgress(double fraction,string codec)
        {lock(sync){view.State=JobState.Processing;view.Progress=Math.Max(view.Progress,80+19*Math.Max(0,Math.Min(1,fraction)));view.Speed=0;view.Eta=0;view.Message="正在转为 "+codec+" · "+Math.Floor(fraction*100)+"%（原始下载文件保留）";}}
        public void ConvertedFile(string path){lock(sync)view.File=path;}
        public void AppendLog(string message)
        {
            if(String.IsNullOrWhiteSpace(message))return;
            string separator=Environment.NewLine+Environment.NewLine;
            var lines=message.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            string entry=String.Join(separator,lines.Where(line=>!String.IsNullOrWhiteSpace(line)));
            lock(sync)
            {
                if(view.Log.Length>60000)view.Log=view.Log.Substring(view.Log.Length-40000);
                view.Log+=view.Log.Length==0?entry:separator+entry;
            }
        }
        public void ApplyLine(string line)
        {
            lock(sync)
            {
                if(line.StartsWith("__STREAMS__"))
                {
                    try {var items=Json.Serializer().DeserializeObject(line.Substring(11)) as ICollection;streamCount=items==null?1:Math.Max(1,items.Count);}catch{streamCount=1;}
                    return;
                }
                if(line.StartsWith("__FILE__"))
                {try{view.File=Json.Serializer().Deserialize<string>(line.Substring(8));}catch{}return;}
                if(line.StartsWith("__PROGRESS__"))
                {
                    try
                    {
                        var p=Json.Parse(line.Substring(12));
                        view.Bytes=Json.Number(p,"downloaded_bytes");view.Total=Json.Number(p,"total_bytes");
                        if(view.Total<=0)view.Total=Json.Number(p,"total_bytes_estimate");
                        bool finished=Json.Text(p,"status")=="finished";
                        view.Speed=finished?0:Json.Number(p,"speed");view.Eta=finished?0:Json.Number(p,"eta");
                        string key=Json.Text(p,"filename");if(String.IsNullOrEmpty(key))key="media";
                        double fraction=finished?1:view.Total>0?Math.Min(1,view.Bytes/view.Total):0;
                        double previous;streams.TryGetValue(key,out previous);streams[key]=Math.Max(previous,fraction);
                        // Keep room for merging and final file validation; audio starts a new stream.
                        double budget=converting?80:95;
                        view.Progress=Math.Max(view.Progress,Math.Min(budget,budget*streams.Values.Sum()/Math.Max(streamCount,streams.Count)));
                        view.State=finished?JobState.Processing:JobState.Downloading;
                        view.Message=finished?"媒体流已下载，等待后续处理":"正在下载媒体流";
                    }
                    catch{AppendLog("暂未取得有效进度，等待下一次更新。");}
                    return;
                }
                AppendLog(line);
                if(line.StartsWith("[Merger]")||line.StartsWith("[ExtractAudio]")||line.StartsWith("[VideoRemuxer]")||line.StartsWith("[Fixup"))
                {view.State=JobState.Processing;view.Progress=Math.Max(view.Progress,converting?80:95);view.Speed=0;view.Eta=0;view.Message="正在合并 / 转换";}
            }
        }
        public void Complete()
        {
            lock(sync)
            {
                if(String.IsNullOrEmpty(view.File)||!File.Exists(view.File))throw new IOException("下载结束，但未找到完成的文件。请查看此任务日志。");
                view.State=JobState.Complete;view.Progress=100;view.Speed=0;view.Eta=0;view.Message="已保存："+Path.GetFileName(view.File);
            }
        }
    }

    public sealed class BatchRunner
    {
        public const int ParallelLimit=3;
        readonly Engine engine;
        public BatchRunner(Engine engine){this.engine=engine;}
        static readonly SemaphoreSlim BrowserLock=new SemaphoreSlim(1,1);
        public async Task ParseAsync(IList<BatchJob> jobs,string proxy,string cookies,CancellationToken token,bool bypassDomestic=true)
        {
            await RunLimited(jobs,async job=>
            {
                job.SetState(JobState.Parsing,"正在获取视频信息");
                string link=Arguments.ValidateUrl(job.Snapshot().Url);
                link=Regex.Replace(link,@"^(https?://)m\.bilibili\.com(?=/video/)","$1www.bilibili.com",RegexOptions.IgnoreCase);
                string effective=Arguments.EffectiveProxy(link,proxy,bypassDomestic);
                string host=new Uri(link).Host;
                bool douyin=Arguments.HostMatches(host,"douyin.com","iesdouyin.com");
                bool xhs=Arguments.HostMatches(host,"xiaohongshu.com","xhslink.com");
                using(var copy=new CookieCopy(link,cookies))
                {
                    if(douyin||xhs)
                    {
                        job.SetState(JobState.Parsing,"正在通过网页解析作品");
                        try
                        {
                            job.Parsed(await ResolvePlatform(douyin?"douyin.cjs":"xiaohongshu.cjs",link,effective,copy.Path,token));
                            return;
                        }
                        catch(InvalidOperationException ex)
                        {
                            if(!xhs)throw;
                            job.AppendLog("小红书网页解析："+ex.Message+"；正在尝试 yt-dlp。");
                        }
                    }
                    var args=Arguments.Parse(engine.ToolsPath,link,effective,"");
                    SetConnection(args,link,bypassDomestic,copy.Path);
                    var result=await engine.Run(args,token,null,true);
                    if(result.Code!=0)throw new InvalidOperationException(result.Error);
                    if(result.Error.Length>0)job.AppendLog(result.Error);
                    job.Parsed(MediaInfo.FromJson(result.Output,link));
                }
            },token);
            var identities=new HashSet<string>(StringComparer.Ordinal);
            foreach(var job in jobs.Where(j=>j.Media!=null))
                if(!identities.Add(job.Media.Identity))job.SetState(JobState.Duplicate,"与本批次另一条链接指向同一视频，已跳过");
        }

        internal static void SetConnection(List<string> args,string link,bool bypassDomestic,string cookieFile)
        {
            if(bypassDomestic && Arguments.IsDomestic(link))args.InsertRange(0,new[]{"--proxy",""});
            if(!String.IsNullOrEmpty(cookieFile))args.InsertRange(0,new[]{"--cookies",cookieFile});
        }

        async Task<MediaInfo> ResolvePlatform(string script,string url,string proxy,string cookiePath,CancellationToken token)
        {
            string effective=proxy.Trim();
            if(effective.Length>0)
            {
                Uri uri;
                if(!Uri.TryCreate(effective,UriKind.Absolute,out uri)||uri.UserInfo.Length>0)
                    throw new InvalidOperationException("网页解析需要有效且不含用户名密码的代理地址。");
                if(uri.Scheme=="socks5h")effective="socks5"+effective.Substring(7);
            }
            await BrowserLock.WaitAsync(token).ConfigureAwait(false);
            string importRoot=null;
            try
            {
                using(AppPaths.LockBrowserProfile())
                {
                    if(!String.IsNullOrEmpty(cookiePath))
                        importRoot=Path.Combine(Path.GetTempPath(),"linkharbor-import-"+Guid.NewGuid().ToString("N"));
                    var args=new List<string>{Path.Combine(engine.ToolsPath,script),url,importRoot??AppPaths.BrowserProfilePath,effective,cookiePath};
                    var result=await engine.RunTool("node.exe",args,token,null,true).ConfigureAwait(false);
                    if(result.Code!=0)throw new InvalidOperationException(result.Error.Trim());
                    var parsed=Json.Parse(result.Output);
                    string jar=Json.Text(parsed,"_linkharbor_cookie_jar");
                    parsed.Remove("_linkharbor_cookie_jar");
                    string json=Json.Serializer().Serialize(parsed);
                    var media=MediaInfo.FromJson(json,url);
                    media.PreResolvedInfoJson=json;
                    media.BrowserCookieJar=jar;
                    return media;
                }
            }
            finally
            {
                BrowserLock.Release();
                if(importRoot!=null && Directory.Exists(importRoot))
                {
                    for(int i=0;i<5;i++)
                    {
                        try{Directory.Delete(importRoot,true);break;}
                        catch(IOException){Thread.Sleep(200);}
                        catch(UnauthorizedAccessException){Thread.Sleep(200);}
                    }
                }
            }
        }
        public async Task DownloadAsync(IList<BatchJob> jobs,string folder,string proxy,string cookies,string mode,int height,CancellationToken token,string codec="original",bool bypassDomestic=true)
        {
            bool video = mode == "video";
            bool convert = video && (codec == "h264" || codec == "h265" || codec == "av1");
            bool direct = video && (codec == "mp4-copy" || codec == "av1-copy");
            if (video && !convert && !direct && codec != "original") throw new ArgumentException("不支持的视频编码");
            Directory.CreateDirectory(folder);
            var selected=jobs.Where(j=>j.Media!=null&&j.Snapshot().State!=JobState.Duplicate).ToList();
            foreach(var job in selected)job.ResetDownload(convert);
            await RunLimited(selected,async job=>
            {
                if(mode=="video"?!job.Media.HasVideo:!job.Media.HasAudio)throw new InvalidOperationException("此视频没有所选的媒体类型，请更改下载内容后重试。");
                job.SetState(JobState.Downloading,"正在连接下载源");
                using(var copy=new CookieCopy(job.Media.Url,cookies,job.Media.BrowserCookieJar))
                {
                    string effective=Arguments.EffectiveProxy(job.Media.Url,proxy,bypassDomestic);
                    var args=Arguments.Download(engine.ToolsPath,job.Media,folder,effective,"",mode,height,codec);
                    SetConnection(args,job.Media.Url,bypassDomestic,copy.Path);
                    if(jobs.Count>1)
                    {
                        // Distinct links can have the same title/id. Keep parallel output paths isolated.
                        string key;
                        using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(job.Media.Url))).Replace("-","").Substring(0,12);
                        int templateIndex = args.IndexOf("-o") + 1;
                        args[templateIndex] = "[" + key + "] " + args[templateIndex];
                    }
                    RunResult result;
                    if(job.Media.PreResolvedInfoJson==null)result=await engine.Run(args,token,job.ApplyLine,false);
                    else
                    {
                        string infoFile=Path.Combine(Path.GetTempPath(),"linkharbor-"+Guid.NewGuid().ToString("N")+".info.json");
                        try
                        {
                            File.WriteAllText(infoFile,job.Media.PreResolvedInfoJson,new UTF8Encoding(false));
                            int end=args.IndexOf("--");
                            args.RemoveRange(end,args.Count-end);
                            args.AddRange(new[]{"--load-info-json",infoFile});
                            result=await engine.Run(args,token,job.ApplyLine,false);
                        }
                        finally{if(File.Exists(infoFile))File.Delete(infoFile);}
                    }
                    if (result.Code != 0)
                    {
                        if (direct && result.Error.IndexOf("requested format is not available", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            job.AppendLog(result.Error);
                            throw new InvalidOperationException(codec == "av1-copy"
                                ? "当前画质没有可用的原生 AV1 组合。可更换画质或手动选择“AV1 · MP4 转码”。"
                                : "当前画质没有可用的 H.264 / AAC 组合。可更换画质或选择“H.264 · MP4”转码。");
                        }
                        throw new InvalidOperationException(result.Error);
                    }
                    if (direct || mode == "m4a")
                        await VideoConversion.ValidateDirectOutput(engine, job.Snapshot().File, mode == "m4a" ? "m4a" : codec, token);
                    if(convert)
                    {
                        string source=job.Snapshot().File;
                        string label = codec == "av1" ? "AV1" : codec == "h264" ? "H.264" : "H.265";
                        string converted=await VideoConversion.ConvertAsync(engine,source,codec,token,fraction=>job.ConversionProgress(fraction,label),job.AppendLog);
                        job.ConvertedFile(converted);
                    }
                    job.Complete();
                }
            },token);
        }
        static async Task RunLimited(IList<BatchJob> jobs,Func<BatchJob,Task> action,CancellationToken token)
        {
            using(var slots=new SemaphoreSlim(ParallelLimit))
            {
                await Task.WhenAll(jobs.Select(async job=>
                {
                    bool entered=false;
                    try {await slots.WaitAsync(token);entered=true;token.ThrowIfCancellationRequested();await action(job);}
                    catch(OperationCanceledException){job.SetState(JobState.Canceled,"已取消，可重新解析或下载");}
                    catch(Exception ex){job.AppendLog(ex.Message);job.SetState(JobState.Failed,ex.Message);}
                    finally{if(entered)slots.Release();}
                }));
            }
            token.ThrowIfCancellationRequested();
        }
        public static double TotalProgress(IEnumerable<JobView> jobs)
        {
            var included=jobs.Where(j=>j.State!=JobState.Duplicate).ToList();
            return included.Count==0?0:included.Average(j=>j.Progress);
        }
        internal sealed class CookieCopy:IDisposable
        {
            public string Path {get;private set;}
            public CookieCopy(string link,string source,string extra=null)
            {
                Path="";
                using(var input=OpenSource(link,source))
                {
                    if(input==null && String.IsNullOrEmpty(extra))return;
                    string target=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"linkharbor-"+Guid.NewGuid().ToString("N")+".txt");
                    try
                    {
                        if(input!=null)
                        {
                            using(var output=File.Open(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
                        }
                        else File.WriteAllText(target,"# Netscape HTTP Cookie File\n",new UTF8Encoding(false));
                        if(!String.IsNullOrEmpty(extra))File.AppendAllText(target,"\n"+extra+"\n",new UTF8Encoding(false));
                        Path=target;
                    }
                    catch{if(File.Exists(target))File.Delete(target);throw;}
                }
            }
            static FileStream OpenSource(string link,string external)
            {
                if(!String.IsNullOrWhiteSpace(external))return File.OpenRead(external);
                if(!Arguments.HostMatches(new Uri(link).Host,"bilibili.com","b23.tv"))return null;
                // An authorization window can atomically replace the saved jar while a task copies it.
                try{return File.Open(AppPaths.BilibiliCookies,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);}
                catch(FileNotFoundException){return null;}
                catch(DirectoryNotFoundException){return null;}
            }
            public void Dispose(){if(Path.Length>0)try{File.Delete(Path);}catch{}}
        }
    }

    public static class VideoConversion
    {
        public static async Task<string> ConvertAsync(Engine engine,string source,string codec,CancellationToken token,Action<double> progress,Action<string> log)
        {
            if(codec!="h264"&&codec!="h265"&&codec!="av1")throw new ArgumentException("请选择 H.264、H.265 或 AV1");
            if(String.IsNullOrEmpty(source)||!File.Exists(source))throw new IOException("找不到待转换的下载文件");
            var input=await Probe(engine,source,token);
            var streams=((IEnumerable)input["streams"]).Cast<object>().OfType<Dictionary<string,object>>().ToList();
            var video=streams.FirstOrDefault(s=>Json.Text(s,"codec_type")=="video");
            if(video==null)throw new InvalidOperationException("文件不包含视频流，无法转换视频编码");
            if(new[]{"smpte2084","arib-std-b67"}.Contains(Json.Text(video,"color_transfer")))
                throw new InvalidOperationException("此视频为 HDR。为避免颜色失真，请选择“原始编码”；已下载的原文件保留。");
            double duration=0;object format;
            if(input.TryGetValue("format",out format))duration=Json.Number((Dictionary<string,object>)format,"duration");
            string target=Path.Combine(Path.GetDirectoryName(source),Path.GetFileNameWithoutExtension(source)+" ["+codec.ToUpperInvariant()+"].mp4");
            string expected = codec == "h265" ? "hevc" : codec;
            if(File.Exists(target))
            {
                await Validate(engine,target,expected,streams.Any(s=>Json.Text(s,"codec_type")=="audio"),token);
                log("已有对应编码文件，跳过转换："+target);return target;
            }
            string temporary=Path.Combine(Path.GetDirectoryName(source),".linkharbor-"+Guid.NewGuid().ToString("N")+".mp4");
            try
            {
                progress(0);log("保留原文件："+source);
                var args = new List<string>{"-hide_banner","-nostdin","-n","-loglevel","error","-i",source,"-map","0:v:0","-map","0:a:0?","-vf","scale=trunc(iw/2)*2:trunc(ih/2)*2","-pix_fmt","yuv420p","-threads","2","-c:a","aac","-b:a","192k","-movflags","+faststart","-progress","pipe:1","-nostats"};
                if (codec == "av1") args.AddRange(new[]{"-c:v","libaom-av1","-cpu-used","6","-crf","30","-b:v","0","-row-mt","1"});
                else args.AddRange(new[]{"-c:v",codec=="h264"?"libx264":"libx265","-preset","medium","-crf",codec=="h264"?"20":"24"});
                if(codec=="h265")args.AddRange(new[]{"-tag:v","hvc1","-x265-params","pools=2:frame-threads=2:log-level=error"});
                args.Add(temporary);
                var result=await engine.RunTool("ffmpeg.exe",args,token,line=>
                {
                    if(line.StartsWith("out_time_us="))
                    {double elapsed;if(duration>0&&Double.TryParse(line.Substring(12),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out elapsed))progress(Math.Min(1,elapsed/1000000/duration));}
                    else if(!line.Contains("="))log(line);
                },false);
                if(result.Code!=0)throw new InvalidOperationException("视频转码失败，原文件保留。\r\n"+result.Error);
                await Validate(engine,temporary,expected,streams.Any(s=>Json.Text(s,"codec_type")=="audio"),token);
                token.ThrowIfCancellationRequested();File.Move(temporary,target);return target;
            }
            finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch{}}
        }
        public static async Task ValidateDirectOutput(Engine engine, string file, string format, CancellationToken token)
        {
            if (String.IsNullOrEmpty(file) || !File.Exists(file)) throw new IOException("找不到下载后的文件");
            var data = await Probe(engine, file, token);
            var streams = ((IEnumerable)data["streams"]).Cast<object>().OfType<Dictionary<string,object>>().ToList();
            var video = streams.FirstOrDefault(s => Json.Text(s,"codec_type") == "video");
            var audio = streams.FirstOrDefault(s => Json.Text(s,"codec_type") == "audio");
            string audioCodec = audio == null ? "" : Json.Text(audio,"codec_name");
            var container = (Dictionary<string,object>)data["format"];
            bool mp4Family = Json.Text(container, "format_name").Split(',').Contains("mp4");
            if (format == "m4a")
            {
                if (mp4Family && video == null && audioCodec == "aac" && file.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)) return;
                throw new IOException("下载文件未通过 AAC / M4A 校验，请检查任务详情。");
            }
            bool av1 = format == "av1-copy";
            string expectedVideo = av1 ? "av1" : "h264";
            bool audioMatches = audio == null || audioCodec == "aac" || (av1 && audioCodec == "opus");
            if (mp4Family && file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && video != null && Json.Text(video,"codec_name") == expectedVideo && audioMatches) return;
            throw new IOException(av1 ? "源文件不符合 AV1 直下要求，可尝试“AV1 · MP4 转码”。" : "源文件不符合 H.264 / AAC 快速封装要求，可尝试“H.264 · MP4”转码。");
        }
        static async Task<Dictionary<string,object>> Probe(Engine engine,string file,CancellationToken token)
        {
            var result=await engine.RunTool("ffprobe.exe",new List<string>{"-v","error","-show_entries","stream=codec_type,codec_name,color_transfer:format=duration,format_name","-of","json",file},token,null,true);
            if(result.Code!=0)throw new IOException("无法检查视频文件："+result.Error);
            return Json.Parse(result.Output);
        }
        static async Task Validate(Engine engine,string file,string codec,bool audio,CancellationToken token)
        {
            var data=await Probe(engine,file,token);
            var streams=((IEnumerable)data["streams"]).Cast<object>().OfType<Dictionary<string,object>>().ToList();
            if(!streams.Any(s=>Json.Text(s,"codec_type")=="video"&&Json.Text(s,"codec_name")==codec)||audio&&!streams.Any(s=>Json.Text(s,"codec_type")=="audio"&&Json.Text(s,"codec_name")=="aac"))
                throw new IOException("目标文件编码校验不通过。原文件保留；若同名转换文件已存在，请先移走它再重试。");
        }
    }
}
