using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinkHarbor;

class CodecTests
{
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAILED: "+name);Console.WriteLine("PASS "+name);count++;}
    static void Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("CODEC PASSED: "+count);}catch(Exception e){Console.Error.WriteLine(e);Environment.Exit(1);}}
    static async Task Run()
    {
        var engine=new Engine(Path.GetFullPath("tools"));
        string dir=Path.GetFullPath(Path.Combine("test-output","codec-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(dir);
        string source=Path.Combine(dir,"中文 test.mp4");File.Copy("test-output/fixture/video.mp4",source);
        var bytes=File.ReadAllBytes(source);string h264="";
        foreach(string codec in new[]{"h264","h265"})
        {
            double maximum=0;
            string output=await VideoConversion.ConvertAsync(engine,source,codec,CancellationToken.None,p=>maximum=Math.Max(maximum,p),line=>{});
            var info=await engine.RunTool("ffprobe.exe",new List<string>{"-v","error","-show_entries","stream=codec_type,codec_name,codec_tag_string:format=duration","-of","json",output},CancellationToken.None,null,true);
            Check(info.Code==0&&info.Output.Contains(codec=="h264"?"h264":"hevc")&&info.Output.Contains("aac"),codec+" output has required video and AAC audio codecs, including MP4 source");
            Check(File.Exists(output)&&output!=source&&maximum>0,codec+" saved separately and reports conversion progress");
            if(codec=="h265")Check(info.Output.Contains("hvc1"),"HEVC MP4 uses hvc1 tag");
            if(codec=="h264")h264=output;
            var stamp=File.GetLastWriteTimeUtc(output);
            string repeated=await VideoConversion.ConvertAsync(engine,source,codec,CancellationToken.None,p=>{},line=>{});
            Check(repeated==output&&File.GetLastWriteTimeUtc(output)==stamp,codec+" existing validated output preserved");
        }
        Check(bytes.SequenceEqual(File.ReadAllBytes(source)),"original download preserved byte for byte");
        var decoded=await engine.RunTool("ffmpeg.exe",new List<string>{"-v","error","-i",h264,"-f","null","-"},CancellationToken.None,null,false);
        Check(decoded.Code==0,"converted video decodes without errors");
        string silent=Path.Combine(dir,"silent.mp4");
        var strip=await engine.RunTool("ffmpeg.exe",new List<string>{"-v","error","-i",source,"-an","-c:v","copy",silent},CancellationToken.None,null,false);
        Check(strip.Code==0&&File.Exists(await VideoConversion.ConvertAsync(engine,silent,"h265",CancellationToken.None,p=>{},line=>{})),"video without audio converts successfully");
        File.WriteAllText(h264,"invalid output");bool rejected=false;
        try{await VideoConversion.ConvertAsync(engine,source,"h264",CancellationToken.None,p=>{},line=>{});}catch(IOException){rejected=true;}
        Check(rejected&&File.ReadAllText(h264)=="invalid output","existing invalid output is rejected rather than overwritten");
        string longSource=Path.Combine(dir,"cancel.mp4");
        var generated=await engine.RunTool("ffmpeg.exe",new List<string>{"-v","error","-f","lavfi","-i","testsrc2=size=1280x720:rate=30","-t","10","-c:v","libx264","-preset","ultrafast","-threads","2",longSource},CancellationToken.None,null,false);
        Check(generated.Code==0,"cancellation test video generated");
        using(var cancel=new CancellationTokenSource())
        {
            bool canceled=false,started=false;cancel.CancelAfter(20000);
            try{await VideoConversion.ConvertAsync(engine,longSource,"h265",cancel.Token,p=>{if(p>0){started=true;cancel.Cancel();}},line=>{});}
            catch(OperationCanceledException){canceled=true;}
            Check(canceled&&started,"cancel terminates active HEVC conversion");
            Check(File.Exists(longSource)&&!File.Exists(Path.Combine(dir,"cancel [H265].mp4"))&&!Directory.GetFiles(dir,".linkharbor-*.mp4").Any(),"cancel keeps source and removes partial conversion");
        }
    }
}
