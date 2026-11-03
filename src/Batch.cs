using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        public MediaInfo Media {get;private set;}
        public BatchJob(int number,string url){view=new JobView{Number=number,Url=url,Title=url,Message="等待解析",Log="",File="",State=JobState.Waiting};}
        public JobView Snapshot()
        {
            lock(sync)return new JobView{Number=view.Number,Url=view.Url,Title=view.Title,Message=view.Message,File=view.File,Log=view.Log,State=view.State,Progress=view.Progress,Speed=view.Speed,Bytes=view.Bytes,Total=view.Total,Eta=view.Eta};
        }
        public void SetState(JobState state,string message)
        {lock(sync){view.State=state;view.Message=message;if(state!=JobState.Downloading){view.Speed=0;view.Eta=0;}}}
        public void Parsed(MediaInfo media){lock(sync){Media=media;view.Title=media.Title;view.State=JobState.Ready;view.Message="解析完成，等待下载";}}
        public void ResetDownload()
        {lock(sync){streams.Clear();streamCount=1;view.Progress=0;view.Speed=0;view.Bytes=0;view.Total=0;view.Eta=0;view.File="";view.State=JobState.Queued;view.Message="等待下载名额";}}
        public void AppendLog(string line)
        {lock(sync){if(view.Log.Length>60000)view.Log=view.Log.Substring(view.Log.Length-40000);view.Log+=line+Environment.NewLine;}}
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
                        view.Progress=Math.Max(view.Progress,Math.Min(95,95*streams.Values.Sum()/Math.Max(streamCount,streams.Count)));
                        view.State=finished?JobState.Processing:JobState.Downloading;
                        view.Message=finished?"媒体流已下载，等待后续处理":"正在下载媒体流";
                    }
                    catch{AppendLog("暂未取得有效进度，等待下一次更新。");}
                    return;
                }
                AppendLog(line);
                if(line.StartsWith("[Merger]")||line.StartsWith("[ExtractAudio]")||line.StartsWith("[Fixup"))
                {view.State=JobState.Processing;view.Progress=Math.Max(view.Progress,95);view.Speed=0;view.Eta=0;view.Message="正在合并 / 转换";}
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
        public async Task ParseAsync(IList<BatchJob> jobs,string proxy,string cookies,CancellationToken token)
        {
            await RunLimited(jobs,async job=>
            {
                job.SetState(JobState.Parsing,"正在获取视频信息");
                string link=Arguments.ValidateUrl(job.Snapshot().Url);
                using(var copy=new CookieCopy(cookies))
                {
                    var result=await engine.Run(Arguments.Parse(engine.ToolsPath,link,proxy,copy.Path),token,line=>{if(!line.StartsWith("{"))job.AppendLog(line);},true);
                    if(result.Code!=0)throw new InvalidOperationException(result.Error);
                    job.Parsed(MediaInfo.FromJson(result.Output,link));
                }
            },token);
            var identities=new HashSet<string>(StringComparer.Ordinal);
            foreach(var job in jobs.Where(j=>j.Media!=null))
                if(!identities.Add(job.Media.Identity))job.SetState(JobState.Duplicate,"与本批次另一条链接指向同一视频，已跳过");
        }
        public async Task DownloadAsync(IList<BatchJob> jobs,string folder,string proxy,string cookies,string mode,int height,CancellationToken token)
        {
            Directory.CreateDirectory(folder);
            var selected=jobs.Where(j=>j.Media!=null&&j.Snapshot().State!=JobState.Duplicate).ToList();
            foreach(var job in selected)job.ResetDownload();
            await RunLimited(selected,async job=>
            {
                if(mode=="video"?!job.Media.HasVideo:!job.Media.HasAudio)throw new InvalidOperationException("此视频没有所选的媒体类型，请更改下载内容后重试。");
                job.SetState(JobState.Downloading,"正在连接下载源");
                using(var copy=new CookieCopy(cookies))
                {
                    var args=Arguments.Download(engine.ToolsPath,job.Media,folder,proxy,copy.Path,mode,height);
                    if(jobs.Count>1)
                    {
                        // Distinct links can have the same title/id. Keep parallel output paths isolated.
                        string key;
                        using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(job.Media.Url))).Replace("-","").Substring(0,12);
                        args[args.IndexOf("-o")+1]="["+key+"] %(title)s [%(id)s].%(ext)s";
                    }
                    var result=await engine.Run(args,token,job.ApplyLine,false);
                    if(result.Code!=0)throw new InvalidOperationException(result.Error);
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
        sealed class CookieCopy:IDisposable
        {
            public string Path {get;private set;}
            public CookieCopy(string source)
            {
                Path="";if(String.IsNullOrWhiteSpace(source))return;
                string target=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"linkharbor-"+Guid.NewGuid().ToString("N")+".txt");
                try{File.Copy(source,target);Path=target;}
                catch{if(File.Exists(target))File.Delete(target);throw;}
            }
            public void Dispose(){if(Path.Length>0)try{File.Delete(Path);}catch{}}
        }
    }
}
