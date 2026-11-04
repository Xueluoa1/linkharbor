using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace LinkHarbor
{
    // The bundle contains the complete offline portable package, including licenses.
    internal static class SingleFileLauncher
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool extractOnly = args.Length == 1 && args[0] == "--extract-only";
            string destination = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "拾链-v" + AppInfo.Version);
            try
            {
                if (extractOnly) Prepare(destination);
                else
                {
                    Application.EnableVisualStyles();
                    Exception failure = null;
                    using (var window = new Form())
                    {
                        window.Text = "拾链 " + AppInfo.DisplayVersion;
                        window.ClientSize = new Size(450, 110);
                        window.StartPosition = FormStartPosition.CenterScreen;
                        window.FormBorderStyle = FormBorderStyle.FixedDialog;
                        window.ControlBox = false;
                        window.Controls.Add(new Label { Text = "正在准备运行组件，首次启动需要片刻……", AutoSize = true, Left = 20, Top = 22 });
                        window.Controls.Add(new ProgressBar { Style = ProgressBarStyle.Marquee, Left = 20, Top = 58, Width = 410 });
                        window.Shown += delegate {
                            ThreadPool.QueueUserWorkItem(delegate {
                                try { Prepare(destination); }
                                catch (Exception error) { failure = error; }
                                window.BeginInvoke((MethodInvoker)delegate { window.Close(); });
                            });
                        };
                        Application.Run(window);
                    }
                    if (failure != null) throw failure;
                    Process.Start(new ProcessStartInfo(Path.Combine(destination, "拾链下载器.exe")) {
                        WorkingDirectory = destination, UseShellExecute = false
                    });
                }
                return 0;
            }
            catch (Exception error)
            {
                if (!extractOnly) MessageBox.Show("启动失败：" + error.Message + "\r\n\r\n请将完整 EXE 放在可写目录，并检查磁盘空间。\r\n程序目录：" + destination,
                    "拾链", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void Prepare(string destination)
        {
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string mutexId;
            using (var hash = SHA256.Create()) mutexId = Hex(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
            using (var mutex = new Mutex(false, "Local\\LinkHarbor-Bundle-" + mutexId))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromMinutes(5)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("另一个启动进程仍在准备组件，请稍后重试。");
                    RejectReparsePoints(root);
                    Directory.CreateDirectory(root);
                    var assembly = Assembly.GetExecutingAssembly();
                    using (var payload = assembly.GetManifestResourceStream("LinkHarbor.Payload.zip"))
                    using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                    using (var reader = new StreamReader(assembly.GetManifestResourceStream("LinkHarbor.Payload.sha256")))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string expected = line.Substring(0, 64);
                            string relative = line.Substring(66);
                            string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("无效的组件路径。");
                            RejectReparsePoints(target);
                            if (File.Exists(target) && FileHash(target) == expected) continue;
                            var entry = archive.GetEntry(relative);
                            if (entry == null) throw new IOException("完整包缺少组件：" + relative);
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try
                            {
                                using (var input = entry.Open())
                                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
                                if (FileHash(temporary) != expected) throw new IOException("组件校验失败：" + relative);
                                if (File.Exists(target)) File.Replace(temporary, target, null);
                                else File.Move(temporary, target);
                            }
                            finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        }
                    }
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }

        private static void RejectReparsePoints(string path)
        {
            for (string current = path; !String.IsNullOrEmpty(current); current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar)))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("程序目录不能使用符号链接或目录联接：" + current);
            }
        }

        private static string FileHash(string path)
        {
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(path)) return Hex(hash.ComputeHash(input));
        }

        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
