using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LinkHarbor;

class BatchTests
{
    static int count;
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);Console.WriteLine("PASS "+label);count++;}
    static void Main(string[] args){try{Run(args[0]).GetAwaiter().GetResult();Console.WriteLine("BATCH PASSED: "+count);}catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}}
    static async Task Run(string origin)
    {
        var split=Arguments.SplitLinks(" https://example.com/a?x=1&b=2\r\nhttps://example.com/b；https://example.com/c;https://example.com/a?x=1&b=2;\n ");
        Check(split.Count==3&&split[0].EndsWith("?x=1&b=2"),"newline, both semicolons, whitespace and duplicate links");
        bool empty=false;try{Arguments.SplitLinks(" ;；\r\n ");}catch(ArgumentException){empty=true;}Check(empty,"empty batch rejected");
        var progress=new BatchJob(1,"https://example.com");progress.ResetDownload();
        progress.ApplyLine("__STREAMS__[{\"format_id\":\"v\"},{\"format_id\":\"a\"}]");
        progress.ApplyLine("__PROGRESS__{\"filename\":\"video\",\"downloaded_bytes\":100,\"total_bytes\":100,\"status\":\"finished\"}");
        double videoDone=progress.Snapshot().Progress;
        progress.ApplyLine("__PROGRESS__{\"filename\":\"audio\",\"downloaded_bytes\":25,\"total_bytes\":100,\"status\":\"downloading\"}");
        Check(videoDone==47.5&&progress.Snapshot().Progress>videoDone&&progress.Snapshot().Progress<100,"separate audio stream advances progress without resetting or finishing early");
        progress.SetState(JobState.Failed,"fixture failure");
        Check(BatchRunner.TotalProgress(new[]{progress.Snapshot(),new BatchJob(2,"https://example.com/b").Snapshot()})<50,"failed and waiting tasks are not counted as complete");
        var engine=new Engine(Path.GetFullPath("tools"));var runner=new BatchRunner(engine);
        var jobs=new[]{new BatchJob(1,origin+"/video.mp4?alias=one"),new BatchJob(2,origin+"/video.mp4?alias=two"),new BatchJob(3,"invalid link")};
        string folder=Path.GetFullPath(Path.Combine("test-output","batch-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
        string cookie=Path.Combine(folder,"fixture-cookies.txt"),cookieText="# Netscape HTTP Cookie File\r\n127.0.0.1\tFALSE\t/\tFALSE\t0\tfixture\ttest\r\n";File.WriteAllText(cookie,cookieText);
        await runner.ParseAsync(jobs,"",cookie,CancellationToken.None);
        Check(jobs.Count(j=>j.Snapshot().State==JobState.Ready)==2&&jobs[2].Snapshot().State==JobState.Failed,"invalid link does not block valid parses");
        await runner.DownloadAsync(jobs,folder,"",cookie,"video",0,CancellationToken.None);
        var views=jobs.Select(j=>j.Snapshot()).ToArray();
        Check(views[0].State==JobState.Complete&&views[1].State==JobState.Complete&&File.Exists(views[0].File)&&File.Exists(views[1].File),"batch downloads and validates both local files");
        Check(views[0].File!=views[1].File,"same-title and same-id links have separate output files");
        Check(File.ReadAllText(cookie)==cookieText,"parallel processes preserve original Cookie file");
        Check(Math.Abs(BatchRunner.TotalProgress(views)-200.0/3)<0.01,"partial failure retains honest total progress");
        await runner.DownloadAsync(jobs,folder,"",cookie,"video",0,CancellationToken.None,"h265");
        Check(jobs.Take(2).All(j=>j.Snapshot().State==JobState.Complete&&j.Snapshot().File.EndsWith("[H265].mp4")&&File.Exists(j.Snapshot().File)),"parallel batch tasks each produce a separate HEVC MP4");
        Check(jobs[2].Snapshot().State==JobState.Failed&&Math.Abs(BatchRunner.TotalProgress(jobs.Select(j=>j.Snapshot()))-200.0/3)<0.01,"conversion completion preserves partial-failure aggregate state");
        await CheckFormats(engine, runner, origin, folder);
        var slow=Enumerable.Range(0,5).Select(i=>new BatchJob(i+1,origin+"/slow-"+(char)('a'+i)+".mp4")).ToArray();
        await runner.ParseAsync(slow,"","",CancellationToken.None);
        Check(slow.All(j=>j.Media!=null),"five slow test media parsed");
        using(var client=new WebClient())client.DownloadString(origin+"/reset-metrics");
        var running=runner.DownloadAsync(slow,folder,"","","video",0,CancellationToken.None);
        bool queued=false;int max=0;
        while(!running.IsCompleted)
        {var state=slow.Select(j=>j.Snapshot()).ToArray();max=Math.Max(max,state.Count(v=>v.State==JobState.Downloading||v.State==JobState.Processing));queued|=state.Any(v=>v.State==JobState.Queued);await Task.Delay(50);}
        await running;
        double peak;using(var client=new WebClient())peak=Json.Number(Json.Parse(client.DownloadString(origin+"/metrics")),"peak");
        Check(peak>=2&&peak<=3&&max==3&&queued,"real overlapping HTTP downloads obey three-job limit and queue extra tasks");
        Check(slow.All(j=>j.Snapshot().State==JobState.Complete)&&BatchRunner.TotalProgress(slow.Select(j=>j.Snapshot()))==100,"all five files finish with 100 percent total");
        using(var cancel=new CancellationTokenSource())
        {
            string cancelFolder=Path.Combine(folder,"cancel");
            var active=runner.DownloadAsync(slow,cancelFolder,"","","video",0,cancel.Token);
            await Task.Delay(800);cancel.Cancel();bool canceled=false;
            try{await active;}catch(OperationCanceledException){canceled=true;}
            Check(canceled&&slow.All(j=>j.Snapshot().State==JobState.Canceled),"cancel stops running and queued tasks");
            Check(!Directory.GetFiles(cancelFolder,"*.mp4").Any(),"queued tasks do not produce completed files after cancel");
        }
        // One parsed task loses its source; the other should still complete.
        var mixed=new[]{new BatchJob(1,origin+"/missing.mp4"),new BatchJob(2,origin+"/video.mp4")};
        mixed[0].Parsed(MediaInfo.FromJson("{\"title\":\"missing\",\"formats\":[{\"ext\":\"mp4\"}]}",origin+"/missing.mp4"));
        await runner.ParseAsync(new[]{mixed[1]},"","",CancellationToken.None);
        await runner.DownloadAsync(mixed,folder,"","","video",0,CancellationToken.None);
        Check(mixed[0].Snapshot().State==JobState.Failed&&mixed[1].Snapshot().State==JobState.Complete,"download failure does not cancel other tasks");
    }

    static async Task CheckFormats(Engine engine, BatchRunner runner, string origin, string folder)
    {
        var token = CancellationToken.None;
        string original = Path.GetFullPath("test-output/fixture/video.mp4");
        string av1Fixture = Path.GetFullPath("test-output/fixture/native-av1.mp4");
        string opusFixture = Path.GetFullPath("test-output/fixture/non-aac.webm");
        var av1 = await engine.RunTool("ffmpeg.exe", new List<string>{"-v","error","-y","-i",original,"-c:v","libaom-av1","-cpu-used","8","-crf","32","-b:v","0","-threads","2","-c:a","copy",av1Fixture}, token, null, false);
        Check(av1.Code==0,"native AV1 fixture created: "+av1.Error);
        var opus = await engine.RunTool("ffmpeg.exe", new List<string>{"-v","error","-y","-i",original,"-vn","-c:a","libopus",opusFixture}, token, null, false);
        Check(opus.Code==0,"non-AAC fixture created: "+opus.Error);

        var direct = new BatchJob(1, origin + "/video.mp4");
        var dash = new BatchJob(2, origin + "/manifest.mpd");
        var native = new BatchJob(3, origin + "/native-av1.mp4");
        var nonAac = new BatchJob(4, origin + "/non-aac.webm");
        await runner.ParseAsync(new[]{direct,dash,native,nonAac},"","",token);
        Check(new[]{direct,dash,native,nonAac}.All(j=>j.Media!=null),"all format fixtures parsed");

        var quickJobs = new[]{direct,dash};
        await runner.DownloadAsync(quickJobs,folder,"","","video",0,token,"mp4-copy");
        Check(quickJobs.All(j=>j.Snapshot().State==JobState.Complete),"quick MP4 accepts direct files and merges DASH tracks: "+dash.Snapshot().Message);
        Check(await MediaHash(engine, original, "v") == await MediaHash(engine, dash.Snapshot().File, "v") &&
              await MediaHash(engine, original, "a") == await MediaHash(engine, dash.Snapshot().File, "a"), "quick MP4 copies DASH H.264 and AAC packets unchanged");

        await runner.DownloadAsync(new[]{native,dash},folder,"","","video",0,token,"av1-copy");
        Check(native.Snapshot().State==JobState.Complete&&await MediaHash(engine,av1Fixture,"v")==await MediaHash(engine,native.Snapshot().File,"v"),"AV1 direct download preserves source packets");
        Check(dash.Snapshot().State==JobState.Failed&&dash.Snapshot().Message.Contains("AV1 · MP4 转码"),"missing native AV1 offers explicit conversion without starting it");
        await runner.DownloadAsync(new[]{dash},folder,"","","video",0,token,"av1");
        Check(dash.Snapshot().State==JobState.Complete&&dash.Snapshot().File.EndsWith("[AV1].mp4"),"explicit AV1 conversion produces validated MP4");

        await runner.DownloadAsync(new[]{dash,nonAac},folder,"","","m4a",0,token);
        Check(dash.Snapshot().State==JobState.Complete&&await MediaHash(engine,original,"a")==await MediaHash(engine,dash.Snapshot().File,"a"),"M4A preserves source AAC packets");
        Check(nonAac.Snapshot().State==JobState.Complete&&nonAac.Snapshot().File.EndsWith(".m4a"),"M4A converts a non-AAC source to validated AAC");
    }

    static async Task<string> MediaHash(Engine engine, string file, string stream)
    {
        var result = await engine.RunTool("ffmpeg.exe", new List<string>{"-v","error","-i",file,"-map","0:"+stream+":0","-c","copy","-f","hash","-"}, CancellationToken.None, null, true);
        if (result.Code!=0) throw new IOException(result.Error);
        return result.Output.Trim();
    }
}
