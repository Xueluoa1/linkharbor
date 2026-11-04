using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using LinkHarbor;

class InstallerTests
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B275D277-6BCF-4786-BD58-F9696BDD7F05}_is1";
    static string root, desktop, start;
    static int checks;
    static void Check(bool ok,string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS "+text); checks++; }
    static T Field<T>(SetupForm form,string name) { return (T)typeof(SetupForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form); }
    [STAThread] static int Main(string[] args) {
        try {
            root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"拾链.lnk");
            start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"拾链.lnk");
            using (var key = Registry.CurrentUser.OpenSubKey(Key)) Check(key == null,"no pre-existing installation (test safety guard)");
            Check(!File.Exists(desktop) && !File.Exists(start),"no existing user shortcuts (test safety guard)");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string cancelled = Path.Combine(root,"cancelled 拾链");
            Install(cancelled,false,true);
            Check(!File.Exists(Path.Combine(cancelled,"拾链下载器.exe")),"cancel rolls back application");
            using (var key = Registry.CurrentUser.OpenSubKey(Key)) Check(key == null,"cancel leaves no uninstall registration");
            Check(!File.Exists(desktop) && !File.Exists(start),"cancel leaves no shortcuts");
            string target = Path.Combine(root,"installed 拾链");
            Install(target,false,false);
            Check(File.Exists(Path.Combine(target,"installed-mode")),"installed data mode marker");
            Check(File.Exists(Path.Combine(target,"tools","ffmpeg.exe")) && File.Exists(Path.Combine(target,"tools","yt-dlp.exe")),"full offline components installed");
            Check(!File.Exists(desktop) && File.Exists(start),"desktop opt-out and start-menu shortcut");
            using (var key = Registry.CurrentUser.OpenSubKey(Key)) Check(key != null && ((string)key.GetValue("InstallLocation")).TrimEnd('\\') == target,"Windows uninstall entry and selected directory");
            string sentinel = Path.Combine(target,"my-download.txt"); File.WriteAllText(sentinel,"keep my file");
            Install(target,false,true);
            Check(File.Exists(Path.Combine(target,"拾链下载器.exe")) && File.ReadAllText(sentinel) == "keep my file","cancelled reinstall preserves installed app and user files");
            VerifyComponents(target);
            Install(target,true,false);
            VerifyComponents(target);
            Check(File.ReadAllText(sentinel) == "keep my file","reinstall preserves user files");
            Check(File.Exists(desktop),"desktop shortcut enabled");
            Directory.CreateDirectory(Path.Combine(target,"build"));
            using (var p = Process.Start(new ProcessStartInfo(Path.Combine(target,"拾链下载器.exe"),"--screenshot \""+Path.Combine(root,"installed-app.png")+"\"") { UseShellExecute = false })) {
                Check(p.WaitForExit(30000) && p.ExitCode == 0,"installed app launches");
            }
            using (var p = Process.Start(new ProcessStartInfo(Path.Combine(target,"unins000.exe"),"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = false })) { Check(p.WaitForExit(60000) && p.ExitCode == 0,"uninstaller succeeds"); }
            for (int i=0;i<100 && File.Exists(Path.Combine(target,"拾链下载器.exe"));i++) Thread.Sleep(100);
            Check(!File.Exists(Path.Combine(target,"拾链下载器.exe")) && !File.Exists(desktop) && !File.Exists(start),"uninstall removes program and shortcuts");
            Check(File.ReadAllText(sentinel) == "keep my file","uninstall preserves user files");
            using (var key = Registry.CurrentUser.OpenSubKey(Key)) Check(key == null,"uninstall removes registration");
            Console.WriteLine("ALL PASSED: "+checks); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void VerifyComponents(string target) {
        string checksums = Path.Combine(target,"tools","SHA256SUMS.txt");
        foreach (string line in File.ReadAllLines(checksums)) {
            if (String.IsNullOrWhiteSpace(line)) continue;
            string expected = line.Substring(0,64).ToLowerInvariant(), file = line.Substring(64).Trim();
            using (var hash = SHA256.Create()) using (var input = File.OpenRead(Path.Combine(target,"tools",file))) Check(BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant() == expected,"installed component hash: "+file);
        }
    }
    static void Install(string target,bool createDesktop,bool cancel) {
        using (var form = new SetupForm()) {
            Field<TextBox>(form,"folder").Text = target;
            Field<CheckBox>(form,"desktop").Checked = createDesktop;
            form.Show(); Application.DoEvents();
            if (!cancel) Capture(form,"installer-ready.png");
            var task = (Task)typeof(SetupForm).GetMethod("InstallAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
            Check(!Field<Panel>(form,"settings").Visible && Field<Panel>(form,"progress").Visible,"progress replaces path and shortcut controls");
            bool observed = false, cancelled = false; var watch = Stopwatch.StartNew();
            while (!task.IsCompleted) {
                Application.DoEvents(); Thread.Sleep(20);
                int value = Field<Meter>(form,"meter").Value;
                if (value > 0 && value < 100) { observed = true; if (!cancel) Capture(form,"installer-progress.png"); }
                if (cancel && value >= 10 && !cancelled) {
                    typeof(SetupForm).GetMethod("RequestCancel",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null); cancelled = true;
                }
                if (watch.Elapsed.TotalSeconds > 180) throw new TimeoutException("Installer exceeded 180 seconds");
            }
            task.GetAwaiter().GetResult(); Application.DoEvents();
            Check(observed,"real intermediate installation progress observed");
            Check(Field<bool>(form,"finished") != cancel,cancel ? "cancel returns retry state" : "installation completes");
            if (!cancel) Capture(form,"installer-complete.png");
            form.Close();
        }
    }
    static void Capture(Form form,string file) { using (var bitmap = new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,form.ClientRectangle); bitmap.Save(Path.Combine(root,file)); } }
}
