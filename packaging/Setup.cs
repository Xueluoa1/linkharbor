using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LinkHarbor
{
    internal static class SetupProgram
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        internal static string Preview, SilentDirectory;
        internal static bool Desktop = true;
        [STAThread] static void Main(string[] args)
        {
            foreach (string arg in args) {
                if (arg.StartsWith("/SCREENSHOT=", StringComparison.OrdinalIgnoreCase)) Preview = arg.Substring(12);
                if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase)) SilentDirectory = arg.Substring(5);
                if (arg.Equals("/NODESKTOP", StringComparison.OrdinalIgnoreCase)) Desktop = false;
            }
            bool owned;
            using (var mutex = new Mutex(true, "Local\\LinkHarborSetup", out owned)) {
                if (!owned) { MessageBox.Show("拾链安装程序已打开。", "拾链"); Environment.ExitCode = 1; return; }
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm());
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        readonly MeshPanel surface = new MeshPanel();
        readonly Panel settings = new Panel(), progress = new Panel();
        readonly TextBox folder = new TextBox();
        readonly CheckBox desktop = new CheckBox();
        readonly SoftButton install = new SoftButton(), cancel = new SoftButton();
        readonly Label status = new Label(), percent = new Label();
        readonly Meter meter = new Meter();
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        bool busy, finished, cancelling;
        string work, destination;
        float scale;
        public SetupForm()
        {
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(S(480), S(390)); StartPosition = FormStartPosition.CenterScreen;
            Text = "拾链安装 · " + AppInfo.DisplayVersion;
            Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            using (var round = Theme.Round(new RectangleF(0,0,Width,Height), S(24))) Region = new Region(round);
            surface.Dock = DockStyle.Fill; Controls.Add(surface);
            surface.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
            surface.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.Line(e.Graphics, new RectangleF(0.5f,0.5f,Width-1,Height-1), S(24), Color.White, 1);
                using (var p = new Pen(Color.FromArgb(200,255,255,255))) e.Graphics.DrawLine(p,0,S(342),Width,S(342));
            };
            var logo = new LinkSculpture(); Put(surface,logo,165,31,76,47);
            TextAt(surface,"拾链",249,28,90,37,21,Theme.Ink,FontStyle.Bold);
            TextAt(surface,AppInfo.DisplayVersion,250,66,85,20,9,Theme.Muted,FontStyle.Regular);
            Put(surface,settings,30,129,420,123); settings.BackColor = Color.Transparent;
            TextAt(settings,"安装位置",0,0,200,22,10,Theme.Ink,FontStyle.Regular);
            var box = new Card { Radius = 12, FillColor = Color.White }; Put(settings,box,0,28,344,40);
            folder.BorderStyle = BorderStyle.None; folder.Font = new Font("Microsoft YaHei UI",9);
            Put(box,folder,13,11,318,24); folder.AccessibleName = "安装位置";
            folder.Text = PreviousDirectory();
            var browse = new SoftButton { Text = "浏览…", Font = new Font("Microsoft YaHei UI",9) }; Put(settings,browse,352,28,68,40);
            browse.Click += delegate { using (var d = new FolderBrowserDialog { Description = "选择拾链安装文件夹", SelectedPath = folder.Text, ShowNewFolderButton = true }) { if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath; } };
            desktop.Text = "创建桌面快捷方式"; desktop.Checked = SetupProgram.Desktop; desktop.ForeColor = Theme.Muted;
            desktop.Font = new Font("Microsoft YaHei UI",9); desktop.BackColor = Color.Transparent;
            Put(settings,desktop,0,78,240,25);
            Put(surface,progress,30,129,420,123); progress.BackColor = Color.Transparent; progress.Visible = false;
            Put(progress,status,0,26,350,24); status.ForeColor = Theme.Ink; status.Font = new Font("Microsoft YaHei UI",10); status.BackColor = Color.Transparent;
            Put(progress,percent,355,26,65,24); percent.TextAlign = ContentAlignment.MiddleRight; percent.Font = new Font("Segoe UI",10); percent.ForeColor = Theme.Blue;
            Put(progress,meter,0,65,420,9);
            install.Text = "安装"; install.Primary = true; install.Font = new Font("Microsoft YaHei UI",10);
            cancel.Text = "取消"; cancel.Font = install.Font;
            Put(surface,install,112,280,144,40); Put(surface,cancel,268,280,100,40);
            install.Click += async delegate { if (finished) { try { Process.Start(new ProcessStartInfo(Path.Combine(destination,"拾链下载器.exe")) { WorkingDirectory = destination }); Close(); } catch (Exception ex) { MessageBox.Show(this,ex.Message,"无法启动"); } } else await InstallAsync(); };
            cancel.Click += delegate { if (busy) RequestCancel(); else Close(); };
            AcceptButton = install; CancelButton = cancel;
            TextAt(surface,"作者  雪落a",164,353,90,23,9,Theme.Muted,FontStyle.Regular);
            var website = new LinkLabel { Text = "xueluo.online ↗", LinkColor = Theme.Muted, ActiveLinkColor = Theme.Blue, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent, Font = new Font("Segoe UI",9) };
            Put(surface,website,257,355,120,22);
            website.LinkClicked += delegate { try { Process.Start("https://xueluo.online"); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开网站"); } };
            poll.Interval = 100; poll.Tick += delegate { ReadProgress(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; RequestCancel(); } };
            Shown += async delegate {
                if (SetupProgram.Preview != null) {
                    if (SetupProgram.Preview.Contains("progress")) { settings.Visible = false; progress.Visible = true; status.Text = "正在安装…"; percent.Text = "40%"; meter.Value = 40; install.Enabled = false; install.Text = "安装中…"; }
                    using (var bitmap = new Bitmap(Width,Height)) { DrawToBitmap(bitmap,ClientRectangle); bitmap.Save(SetupProgram.Preview); } Close();
                } else if (SetupProgram.SilentDirectory != null) { folder.Text = SetupProgram.SilentDirectory; await InstallAsync(); }
            };
        }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        int S(int n) { return (int)Math.Round(n*scale); }
        void Put(Control parent, Control child, int x, int y, int w, int h) { child.SetBounds(S(x),S(y),S(w),S(h)); parent.Controls.Add(child); }
        Label TextAt(Control parent,string text,int x,int y,int w,int h,float size,Color color,FontStyle style) { var l = new Label { Text = text, Font = new Font("Microsoft YaHei UI",size,style), ForeColor = color, BackColor = Color.Transparent }; Put(parent,l,x,y,w,h); return l; }
        static string PreviousDirectory() {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B275D277-6BCF-4786-BD58-F9696BDD7F05}_is1")) {
                var path = k == null ? null : k.GetValue("InstallLocation") as string;
                if (!String.IsNullOrWhiteSpace(path)) return path.TrimEnd('\\');
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","LinkHarbor");
        }
        void ValidateDestination() {
            destination = Path.GetFullPath(folder.Text.Trim()).TrimEnd('\\');
            if (!Path.IsPathRooted(folder.Text.Trim()) || destination.TrimEnd('\\') == Path.GetPathRoot(destination).TrimEnd('\\') || destination.StartsWith(@"\\") || destination.Contains("\"")) throw new IOException("请选择本机磁盘中的独立文件夹。 ");
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B275D277-6BCF-4786-BD58-F9696BDD7F05}_is1")) {
                string previous = key == null ? null : key.GetValue("InstallLocation") as string;
                if (!String.IsNullOrEmpty(previous) && !String.Equals(Path.GetFullPath(previous).TrimEnd('\\'),destination,StringComparison.OrdinalIgnoreCase)) throw new IOException("已安装拾链。升级请使用原目录；如需更换位置，请先在 Windows 设置中卸载原安装。");
            }
            if (Directory.Exists(destination) && Directory.GetFileSystemEntries(destination).Length > 0 && !File.Exists(Path.Combine(destination,"installed-mode"))) throw new IOException("该文件夹已有其他内容，请新建一个拾链文件夹后安装。");
            // Probe the existing ancestor, leaving the target itself for the installer's rollback.
            string parent = destination;
            while (!Directory.Exists(parent)) parent = Path.GetDirectoryName(parent);
            string probe = Path.Combine(parent,".linkharbor-write-"+Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) {} File.Delete(probe);
            var drive = new DriveInfo(Path.GetPathRoot(destination));
            if (drive.AvailableFreeSpace < 700L*1024*1024) throw new IOException("安装磁盘至少需要 700 MB 可用空间。");
        }
        void RequestCancel() {
            if (cancelling) return;
            cancelling = true; cancel.Enabled = false; status.Text = "正在取消并回滚…";
            if (work != null) File.WriteAllText(Path.Combine(work,"cancel"),"cancel");
        }
        void ReadProgress() {
            if (work == null || cancelling) return;
            try {
                var parts = File.ReadAllText(Path.Combine(work,"progress")).Split('/');
                int done, total;
                if (parts.Length == 2 && Int32.TryParse(parts[0],out done) && Int32.TryParse(parts[1],out total) && total > 0) {
                    int value = Math.Min(99,(int)(100L*done/total)); meter.Value = value; meter.Invalidate(); percent.Text = value+"%"; status.Text = "正在安装…";
                }
            } catch (IOException) {} catch (UnauthorizedAccessException) {}
        }
        async Task InstallAsync() {
            try { ValidateDestination(); } catch (Exception ex) { if (SetupProgram.SilentDirectory == null) MessageBox.Show(this,ex.Message,"安装位置不可用",MessageBoxButtons.OK,MessageBoxIcon.Information); else { Environment.ExitCode = 2; Close(); } return; }
            busy = true; cancelling = false; settings.Visible = false; progress.Visible = true;
            install.Enabled = false; install.Text = "安装中…"; status.Text = "正在准备安装…"; percent.Text = "0%"; meter.Value = 0;
            work = Path.Combine(Path.GetTempPath(),"LinkHarborSetup-"+Guid.NewGuid().ToString("N"));
            int result = -1; string error = null; bool createDesktop = desktop.Checked;
            try {
                Directory.CreateDirectory(work); poll.Start();
                result = await Task.Run(delegate {
                    string backend = Path.Combine(work,"install.exe");
                    using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("backend")) using (var output = File.Create(backend)) input.CopyTo(output);
                    if (File.Exists(Path.Combine(work,"cancel"))) return 5;
                    string arguments = "/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /DIR="+Quote(destination)+" /TASKS="+Quote(createDesktop ? "desktopicon" : "")+" /PROGRESSFILE="+Quote(Path.Combine(work,"progress"))+" /CANCELFILE="+Quote(Path.Combine(work,"cancel"))+" /LOG="+Quote(Path.Combine(work,"install.log"));
                    using (var process = Process.Start(new ProcessStartInfo(backend,arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work })) { process.WaitForExit(); return process.ExitCode; }
                });
            } catch (Exception ex) { error = ex.Message; }
            finally { poll.Stop(); busy = false; }
            if (result == 0) {
                finished = true; status.Text = "安装完成"; meter.Value = 100; meter.Invalidate(); percent.Text = "100%"; install.Text = "启动拾链"; install.Enabled = true; cancel.Text = "关闭"; cancel.Enabled = true;
            } else if (cancelling && (result == 2 || result == 5)) {
                status.Text = "已取消安装"; percent.Text = ""; install.Text = "重新安装"; install.Enabled = true; cancel.Enabled = true;
                settings.Visible = true; progress.Visible = false;
            } else {
                status.Text = "安装未完成"; install.Text = "重试安装"; install.Enabled = true; cancel.Enabled = true;
                string savedLog = Path.Combine(Path.GetTempPath(),"LinkHarbor-install-error-"+DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".log");
                try { File.Copy(Path.Combine(work,"install.log"),savedLog,true); } catch (IOException) { savedLog = "未生成日志"; }
                if (SetupProgram.SilentDirectory == null) MessageBox.Show(this,(error ?? "安装返回代码："+result)+"\r\n请关闭正在运行的拾链，再重试。\r\n日志："+savedLog,"安装未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                settings.Visible = true; progress.Visible = false;
            }
            try { if (work != null && Directory.Exists(work)) Directory.Delete(work,true); } catch (IOException) {} catch (UnauthorizedAccessException) {}
            work = null;
            if (SetupProgram.SilentDirectory != null) { Environment.ExitCode = result == 0 ? 0 : 1; Close(); }
        }
        static string Quote(string text) { return "\""+text.Replace("\"", "")+"\""; }
    }
}
