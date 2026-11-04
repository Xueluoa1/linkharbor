using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkHarbor
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static void Main(string[] args)
        {
            SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "拾链 · 出现问题", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm(args));
        }
    }

    sealed class ChoiceBox : Control
    {
        public readonly List<object> Items = new List<object>();
        public event EventHandler SelectedIndexChanged;
        int selected = -1;
        public int SelectedIndex { get { return selected; } set { selected = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); } }
        public object SelectedItem { get { return selected >= 0 && selected < Items.Count ? Items[selected] : null; } }
        public ChoiceBox() { DoubleBuffered = true; TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox; SetStyle(ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96f;
            Theme.Fill(e.Graphics, new RectangleF(1,1,Width-3,Height-3), 13*scale, Enabled ? Color.White : Color.FromArgb(241,244,248));
            Theme.Line(e.Graphics, new RectangleF(1,1,Width-3,Height-3), 13*scale, Focused ? Theme.Blue : Theme.Border, 1);
            var color = Enabled ? Theme.Ink : Theme.Muted;
            TextRenderer.DrawText(e.Graphics, SelectedItem == null ? "请选择" : SelectedItem.ToString(), Font, new Rectangle((int)(12*scale), 0, Width - (int)(36*scale), Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var brush = new SolidBrush(color)) e.Graphics.FillPolygon(brush, new[] { new Point(Width - 19, Height / 2 - 2), new Point(Width - 11, Height / 2 - 2), new Point(Width - 15, Height / 2 + 2) });
        }
        void ShowChoices()
        {
            if (!Enabled) return;
            Focus(); var menu = new ContextMenuStrip { Font = Font };
            for (int i = 0; i < Items.Count; i++)
            {
                int index = i; var item = new ToolStripMenuItem(Items[i].ToString()) { Checked = i == selected };
                item.Click += (s, e) => { SelectedIndex = index; }; menu.Items.Add(item);
            }
            menu.Closed += (s, e) => { if(!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => menu.Dispose())); else menu.Dispose(); };
            menu.Show(this, new Point(0, Height));
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); ShowChoices(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || (e.Alt && e.KeyCode == Keys.Down)) { ShowChoices(); e.Handled = true; }
            else if (Items.Count > 0 && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)) { SelectedIndex = Math.Max(0, Math.Min(Items.Count - 1, selected + (e.KeyCode == Keys.Down ? 1 : -1))); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    sealed partial class MainForm : Form
    {
        static readonly Color Ink = Theme.Ink, Muted = Theme.Muted, Blue = Theme.Blue;
        readonly Engine engine;
        readonly string settingsFile;
        readonly bool diagnostics;
        TextBox url, folder, proxy, log;
        Label mediaTitle, mediaMeta, status, detail, percent, cookieLabel, modeHint;
        Button parse, download, cancel, browse, cookiesButton, clearCookies;
        ChoiceBox quality, mode;
        Meter meter;
        Panel network;
        MediaInfo media;
        CancellationTokenSource operation;
        Task activeTask;
        string cookies = "", completedFile = "";
        bool busy, closePending;
        readonly List<Control> editable = new List<Control>();

        public MainForm(string[] args)
        {
            diagnostics = args.Length > 0 && (args[0] == "--screenshot" || args[0] == "--smoke-test");
            engine = new Engine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools"));
            settingsFile = AppPaths.SettingsFile;
            BuildInterface();
            LoadSettings(); ResetMedia();
            InitializeTaskUpdates();
            if(diagnostics)proxy.Text="";
            Shown += (s, e) => { try { engine.Check(); } catch (Exception ex) { SetError(ex.Message); } url.Focus(); };
            FormClosing += HandleClosing;
            if (args.Length == 2 && args[0] == "--screenshot")
            {
                Shown += (s, e) => BeginInvoke(new Action(() =>
                {
                    using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(args[1]); }
                    if(Icon==null)throw new InvalidOperationException("Application icon missing");
                    using(var bitmap=Icon.ToBitmap()){bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build","taskbar-icon-preview.png"));}
                    using(var help=CreateNetworkHelp())
                    {
                        help.Show(this);
                        using(var bitmap=new Bitmap(help.Width,help.Height)){help.DrawToBitmap(bitmap,new Rectangle(Point.Empty,help.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build","network-help-preview.png"));}
                        ((Button)help.Controls.Find("CookieHelpTab",false)[0]).PerformClick();
                        using(var bitmap=new Bitmap(help.Width,help.Height)){help.DrawToBitmap(bitmap,new Rectangle(Point.Empty,help.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build","cookie-help-preview.png"));}
                        ((Button)help.Controls.Find("BrowserHelpTab",false)[0]).PerformClick();
                        using(var bitmap=new Bitmap(help.Width,help.Height)){help.DrawToBitmap(bitmap,new Rectangle(Point.Empty,help.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build","browser-help-preview.png"));}
                        help.Close();
                    }
                    using(var help=CreateUsageHelp())
                    {
                        help.Show(this);
                        using(var bitmap=new Bitmap(help.Width,help.Height)){help.DrawToBitmap(bitmap,new Rectangle(Point.Empty,help.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build","usage-help-preview.png"));}
                        help.Close();
                    }
                    Close();
                }));
            }
            if (args.Length == 2 && args[0] == "--smoke-test")
            {
                Shown += async (s, e) =>
                {
                    string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output", "ui-smoke-result.txt");
                    try
                    {
                        folder.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-output", "ui-" + Guid.NewGuid().ToString("N"));
                        ValidateInterfaceLayout();
                        ValidateButtonBackgrounds();
                        url.Text = "invalid link"; parse.PerformClick(); await activeTask;
                        if(status.Text!="未能完成"||!parse.Enabled||cancel.Enabled)throw new Exception("Invalid link UI recovery failed");
                        url.Text = args[1]; parse.PerformClick(); await activeTask;
                        if (media == null || !download.Enabled || quality.Items.Count < 2) throw new Exception("UI parse failed: " + detail.Text);
                        mode.SelectedIndex=2;
                        if(quality.Enabled||!download.Enabled)throw new Exception("Audio mode did not update controls");
                        mode.SelectedIndex=0;
                        if(!quality.Enabled)throw new Exception("Video mode did not restore quality selection");
                        download.PerformClick(); await activeTask;
                        if (status.Text != "下载完成" || !File.Exists(completedFile) || !download.Enabled || cancel.Enabled) throw new Exception("UI download failed: " + detail.Text);
                        using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build", "download-preview.png")); }
                        ClientSize = new Size(S(900), S(680)); PerformLayout(); workspace.PerformLayout(); LayoutWorkspace();
                        ValidateInterfaceLayout();
                        if(!(mediaCard.Top<transferCard.Top&&transferCard.Top<saveCard.Top&&saveCard.Top<networkCard.Top))throw new Exception("Compact layout did not stack cards");
                        Navigate("home");
                        using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build", "compact-preview.png")); }
                        settingsNav.PerformClick();
                        if(!settingsNav.Selected || !workspace.RectangleToScreen(workspace.ClientRectangle).Contains(proxy.RectangleToScreen(proxy.ClientRectangle)))throw new Exception("Settings navigation did not reveal proxy input");
                        using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build", "settings-preview.png")); }
                        taskNav.PerformClick();
                        if(!taskNav.Selected)throw new Exception("Task navigation failed");
                        await RunBatchSmoke(new Uri(args[1]).GetLeftPart(UriPartial.Authority));
                        File.WriteAllText(report, "PASS: rounded backgrounds, single and batch parse/download, Enter inserts newline, three parallel tasks plus queue, per-task dialog, dialog close/reopen during download, mixed failure, honest aggregate progress, cancel all, clear/reset, wide/compact layout");
                    }
                    catch (Exception ex) { File.WriteAllText(report, ex.ToString()); Environment.ExitCode = 1; }
                    finally { Close(); }
                };
            }
        }
        void LoadSettings()
        {
            Settings settings = new Settings();
            try { if (File.Exists(settingsFile)) settings = Json.Serializer().Deserialize<Settings>(File.ReadAllText(settingsFile, System.Text.Encoding.UTF8)) ?? settings; } catch { }
            folder.Text = !String.IsNullOrWhiteSpace(settings.Folder) && Path.IsPathRooted(settings.Folder) ? settings.Folder : new Settings().Folder; proxy.Text = settings.Proxy ?? "";
        }
        void SaveSettings()
        {
            if (diagnostics) return;
            try
            {
                // Proxy credentials and Cookie paths are intentionally session-only.
                Uri u; string safeProxy = Uri.TryCreate(proxy.Text.Trim(), UriKind.Absolute, out u) && String.IsNullOrEmpty(u.UserInfo) ? proxy.Text.Trim() : "";
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                File.WriteAllText(settingsFile, Json.Serializer().Serialize(new Settings { Folder = folder.Text, Proxy = safeProxy }), System.Text.Encoding.UTF8);
            }
            catch (Exception ex) { AppendLog("设置未能保存：" + ex.Message); }
        }
        void ResetMedia() { mediaTitle.Text = "等待解析视频"; mediaMeta.Text = "输入一个或多个链接，点击按钮开始解析。"; quality.Items.Clear(); quality.Items.Add(new Quality { Label = "解析后选择画质" }); quality.SelectedIndex = 0; RefreshSelection(); }
        void RefreshSelection()
        {
            mode.Enabled = !busy && media != null;
            quality.Enabled = !busy && media != null && mode.SelectedIndex == 0 && jobs.Any(j=>j.Media!=null&&j.Media.HasVideo&&j.Snapshot().State!=JobState.Duplicate);
            download.Enabled = !busy && jobs.Any(j=>j.Media!=null&&j.Snapshot().State!=JobState.Duplicate&&(mode.SelectedIndex==0?j.Media.HasVideo:j.Media.HasAudio));
            modeHint.Text = mode.SelectedIndex == 0 ? "合并视频保存为 MKV；单文件保留原格式" : mode.SelectedIndex == 1 ? "保留原音频编码，避免再次压缩" : "转换为 MP3，方便在更多设备播放";
        }
        void SetStatus(string title, string message) { status.Text = title; status.ForeColor = Ink; detail.Text = message; UpdateWorkflow(title); }
        void SetError(string message) { status.Text = "未能完成"; UpdateWorkflow("未能完成"); status.ForeColor = Color.FromArgb(180, 61, 50); detail.Text = message; AppendLog(message); }
        void AppendLog(string line)
        {
            if (log.TextLength > 100000) log.Text = log.Text.Substring(log.TextLength - 60000);
            log.AppendText(line + Environment.NewLine);
        }
        void OnUi(Action action) { if (IsDisposed || !IsHandleCreated) return; try { BeginInvoke(new Action(() => { if (!IsDisposed) action(); })); } catch (InvalidOperationException) { } }
        void StartOperation(Func<CancellationToken, Task> action)
        {
            if (busy) return;
            activeTask = Perform(action);
        }
        async Task Perform(Func<CancellationToken, Task> action)
        {
            busy = true; operation = new CancellationTokenSource();
            foreach (var c in editable) c.Enabled = false;
            download.Enabled = false; cancel.Enabled = true; log.Clear(); completedFile = ""; meter.Value = 0; meter.Invalidate(); percent.Text = "—";
            try { engine.Check(); SaveSettings(); await action(operation.Token); }
            catch (OperationCanceledException) { SetStatus("任务已取消", "已完成的文件保留；未完成的任务可重新开始下载。"); }
            catch (Exception ex) { SetError(ex.Message); percent.Text = "—"; }
            finally
            {
                busy = false; operation.Dispose(); operation = null;
                foreach (var c in editable) c.Enabled = true;
                cancel.Enabled = false; RefreshSelection();
                if (closePending) Close();
            }
        }
        static string SizeText(double bytes) { return bytes >= 1073741824 ? (bytes / 1073741824).ToString("0.00") + " GB" : bytes >= 1048576 ? (bytes / 1048576).ToString("0.0") + " MB" : bytes >= 1024 ? (bytes / 1024).ToString("0.0") + " KB" : Math.Max(0, bytes).ToString("0") + " B"; }
        static string Failure(string error)
        {
            string lower = error.ToLowerInvariant();
            if (lower.Contains("sign in") || lower.Contains("login") || lower.Contains("cookies")) return "网站要求登录或验证。可在“网络与登录”选择 Cookie 文件后重试；详细原因见任务日志。";
            if (lower.Contains("timed out") || lower.Contains("unable to download") || lower.Contains("connection") || lower.Contains("proxy")) return "无法连接视频网站。请检查网络，或在“网络与登录”填写可用代理后重试。详情见任务日志。";
            if (lower.Contains("unsupported url")) return "暂不支持这个链接，请确认它是视频页面或可访问的媒体直链。";
            if (lower.Contains("requested format")) return "所选格式当前不可用，请重新解析或选择另一种下载内容。";
            if (lower.Contains("drm")) return "该内容受到 DRM 保护，无法下载。";
            var lines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string last = lines.LastOrDefault(l => l.Contains("ERROR:")) ?? lines.LastOrDefault() ?? "请查看任务日志。";
            return last.Length > 260 ? last.Substring(0, 260) + "…（完整信息见日志）" : last;
        }
        void CancelOperation() { if (operation == null) return; cancel.Enabled = false; SetStatus("正在取消…", "正在停止下载及相关处理进程。"); var source = operation; Task.Run(() => { try { source.Cancel(); } catch (ObjectDisposedException) { } }); }
        void HandleClosing(object sender, FormClosingEventArgs e)
        {
            if (!busy) { SaveSettings(); return; }
            e.Cancel = true;
            if (closePending) return;
            if (MessageBox.Show(this, "当前任务还未完成。取消任务并退出？", "退出拾链", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { closePending = true; CancelOperation(); }
        }
    }
}
