using System;
using System.IO;
using System.Linq;
using System.Threading;
using LinkHarbor;

// Opt-in live test. Uses the supplied public work links and output folder.
class DouyinLive
{
    static int Main(string[] args)
    {
        var token = CancellationToken.None;
        var engine = new Engine(Path.GetFullPath("tools"));
        var runner = new BatchRunner(engine);
        string proxy = args[0];
        var jobs = args.Skip(2).Select((url, index) => new BatchJob(index+1, url)).ToArray();
        runner.ParseAsync(jobs, proxy, "", token).GetAwaiter().GetResult();
        foreach (var job in jobs)
        {
            var view = job.Snapshot();
            Console.WriteLine("{0}: {1} {2}", view.Number, view.State, view.Message);
        }
        if (jobs[0].Snapshot().State != JobState.Ready ||
            jobs.Skip(1).Any(job => job.Snapshot().State != JobState.Duplicate)) return 1;
        runner.DownloadAsync(jobs, Path.GetFullPath(args[1]), proxy, "", "video", 0, token).GetAwaiter().GetResult();
        var output = jobs[0].Snapshot();
        if (output.State != JobState.Complete)
        {
            Console.WriteLine(System.Text.RegularExpressions.Regex.Replace(output.Message, @"https?://\S+", "[URL]"));
            return 2;
        }
        Console.WriteLine(output.File);
        return engine.RunTool("ffprobe.exe", new System.Collections.Generic.List<string>{
            "-v","error","-show_entries","format=duration,size:stream=codec_type,codec_name,width,height",
            "-of","json",output.File},token,Console.WriteLine,false).GetAwaiter().GetResult().Code;
    }
}
