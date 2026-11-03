using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
class TestHost
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--sleep")
        {
            var p = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--child") { UseShellExecute = false, CreateNoWindow = true });
            File.WriteAllText(args[1] + ".tmp", p.Id.ToString());
            File.Move(args[1] + ".tmp", args[1]);
            Thread.Sleep(60000); return;
        }
        if (args.Length > 0 && args[0] == "--child") { Thread.Sleep(60000); return; }
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine(new JavaScriptSerializer().Serialize(args));
    }
}
