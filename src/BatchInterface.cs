using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkHarbor
{
    sealed partial class MainForm
    {
        readonly List<BatchJob> jobs=new List<BatchJob>();
        string parsedInput="",batchPhase="";
        System.Windows.Forms.Timer batchTimer;
        Form taskDialog;
        DataGridView taskGrid;
        Label taskSummary;
        TextBox taskDetails;
        SoftButton taskCancel,taskOpen;

        void InitializeTaskUpdates()
        {
            batchTimer=new System.Windows.Forms.Timer{Interval=250};
            batchTimer.Tick+=(s,e)=>{if(busy||taskDialog!=null)RefreshBatchView();};
            batchTimer.Start();
            FormClosed+=(s,e)=>{batchTimer.Stop();batchTimer.Dispose();if(taskDialog!=null)taskDialog.Close();};
        }
        void ClearBatch()
        {
            jobs.Clear();parsedInput="";media=null;completedFile="";ResetMedia();
            meter.Value=0;meter.Invalidate();percent.Text="—";RefreshTaskDialog();
        }
        async Task ParseMedia(CancellationToken token)
        {
            ClearBatch();
            var links=Arguments.SplitLinks(url.Text);parsedInput=url.Text;
            for(int i=0;i<links.Count;i++)jobs.Add(new BatchJob(i+1,links[i]));
            batchPhase="parse";RefreshBatchView();
            try{await new BatchRunner(engine).ParseAsync(jobs,proxy.Text,cookies,token);}
            finally{batchPhase="";RefreshMediaSummary();RefreshTaskDialog();AppendBatchLogs();}
            int ready=jobs.Count(j=>j.Media!=null&&j.Snapshot().State!=JobState.Duplicate);
            int failed=jobs.Count(j=>j.Snapshot().State==JobState.Failed);
            if(ready==0){SetError("没有可下载的视频。点击左侧下载图标查看每条链接的原因。");return;}
            SetStatus("解析完成",String.Format("可下载 {0} 项{1}；选择内容后点击“开始下载”。",ready,failed>0?"，失败 "+failed+" 项":""));
        }
        void RefreshMediaSummary()
        {
            var ready=jobs.Where(j=>j.Media!=null&&j.Snapshot().State!=JobState.Duplicate).Select(j=>j.Media).ToList();
            media=ready.FirstOrDefault();
            if(media==null){ResetMedia();return;}
            mediaTitle.Text=jobs.Count==1?media.Title:"已解析 "+ready.Count+" 个视频";
            mediaMeta.Text=jobs.Count==1?media.FormatCount+" 种可用格式 · "+(media.Duration>0?TimeSpan.FromSeconds(media.Duration).ToString(@"hh\:mm\:ss"):"时长未知"):"统一选择下载内容与画质；左侧下载图标查看详情";
            quality.Items.Clear();quality.Items.Add(new Quality{Label="最佳可用画质",Height=0});
            foreach(int h in ready.SelectMany(m=>m.Heights).Distinct().OrderByDescending(h=>h))quality.Items.Add(new Quality{Label="最高 "+h+"p",Height=h});
            quality.SelectedIndex=0;mode.SelectedIndex=ready.Any(m=>m.HasVideo)?0:1;
        }
        async Task DownloadMedia(CancellationToken token)
        {
            if(media==null||parsedInput!=url.Text)throw new InvalidOperationException("请先解析当前链接。");
            string modeName=mode.SelectedIndex==0?"video":mode.SelectedIndex==1?"audio":"mp3";
            var q=quality.SelectedItem as Quality;
            batchPhase="download";
            try{await new BatchRunner(engine).DownloadAsync(jobs,folder.Text,proxy.Text,cookies,modeName,q==null?0:q.Height,token);}
            finally{RefreshBatchView();batchPhase="";AppendBatchLogs();RefreshTaskDialog();}
            var views=jobs.Select(j=>j.Snapshot()).ToList();
            int done=views.Count(j=>j.State==JobState.Complete),failed=views.Count(j=>j.State==JobState.Failed),canceled=views.Count(j=>j.State==JobState.Canceled);
            completedFile=views.Where(j=>j.State==JobState.Complete).Select(j=>j.File).FirstOrDefault()??"";
            bool allComplete=views.All(j=>j.State==JobState.Complete||j.State==JobState.Duplicate);
            SetStatus(allComplete?"下载完成":done>0?"部分下载完成":"下载未完成",String.Format("已完成 {0} 项 · 失败 {1} 项{2}；左侧下载图标查看详情。",done,failed,canceled>0?" · 取消 "+canceled+" 项":""));
        }
        void AppendBatchLogs()
        {
            foreach(var job in jobs){var v=job.Snapshot();AppendLog("[任务 "+v.Number+"] "+v.Title+"\r\n"+v.Log);}
        }
        void RefreshBatchView()
        {
            var views=jobs.Select(j=>j.Snapshot()).ToList();
            if(batchPhase=="parse")
            {
                int finished=views.Count(j=>j.State!=JobState.Waiting&&j.State!=JobState.Parsing);
                SetStatus("正在解析 "+finished+"/"+views.Count,"最多同时解析 3 项；每条链接的结果可在任务窗口查看。");
            }
            else if(batchPhase=="download")
            {
                meter.Value=(int)Math.Floor(BatchRunner.TotalProgress(views));meter.Invalidate();percent.Text=meter.Value+"%";
                int done=views.Count(j=>j.State==JobState.Complete),active=views.Count(j=>j.State==JobState.Downloading||j.State==JobState.Processing);
                int failed=views.Count(j=>j.State==JobState.Failed),total=views.Count(j=>j.State!=JobState.Duplicate);
                SetStatus("已完成 "+done+" / "+total,"运行 "+active+" 项 · 失败 "+failed+" 项 · "+SizeText(views.Sum(j=>j.Speed))+"/s\r\n总进度按任务平均估算，包含合并与保存。");
            }
            if(busy&&operation!=null&&operation.IsCancellationRequested)SetStatus("正在取消…","正在停止运行中及排队中的任务。");
            RefreshTaskDialog();
        }
        static string JobStateText(JobState state)
        {
            switch(state)
            {
                case JobState.Waiting:return "等待解析";case JobState.Parsing:return "正在解析";case JobState.Ready:return "等待下载";
                case JobState.Queued:return "排队中";case JobState.Downloading:return "正在下载";case JobState.Processing:return "合并 / 处理";
                case JobState.Complete:return "已完成";case JobState.Failed:return "失败";case JobState.Canceled:return "已取消";default:return "重复已跳过";
            }
        }
        void ShowTasks()
        {
            if(taskDialog!=null){taskDialog.Activate();return;}
            taskDialog=new Form{Text="拾链 · 下载任务",ClientSize=new Size(S(920),S(550)),MinimumSize=new Size(S(760),S(460)),AutoScaleMode=AutoScaleMode.None,StartPosition=FormStartPosition.CenterParent,BackColor=Theme.Surface,Font=Font,Icon=Icon};
            var title=LabelAt(taskDialog,"下载任务",24,20,400,38,20,Ink,true);
            taskSummary=LabelAt(taskDialog,"",26,65,850,28,9,Muted,false);
            taskGrid=new DataGridView{ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoGenerateColumns=false,BackgroundColor=Theme.Surface,BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,GridColor=Theme.Border,EnableHeadersVisualStyles=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,Font=TextFont(9)};
            taskGrid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.FromArgb(233,239,248),ForeColor=Ink,Font=TextFont(9,true),SelectionBackColor=Color.FromArgb(233,239,248)};
            taskGrid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.White,ForeColor=Ink,SelectionBackColor=Color.FromArgb(225,234,255),SelectionForeColor=Ink,Padding=new Padding(S(5))};
            taskGrid.RowTemplate.Height=S(38);taskGrid.ColumnHeadersHeight=S(38);
            string[] headers={"序号","视频 / 链接","状态","进度","速度","当前媒体流","剩余时间"};
            int[] weights={42,235,100,65,90,140,90};
            for(int i=0;i<headers.Length;i++)taskGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=headers[i],FillWeight=weights[i],SortMode=DataGridViewColumnSortMode.NotSortable});
            taskDialog.Controls.Add(taskGrid);taskGrid.SelectionChanged+=(s,e)=>RefreshTaskDetails();
            taskDetails=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Theme.Surface,ForeColor=Muted,Font=TextFont(9)};taskDialog.Controls.Add(taskDetails);
            taskCancel=ButtonAt(taskDialog,"取消全部",24,500,120,36,false);taskCancel.Click+=(s,e)=>CancelOperation();
            taskOpen=ButtonAt(taskDialog,"打开所选文件夹",156,500,170,36,false,"folder");
            taskOpen.Click+=(s,e)=>
            {
                var job=SelectedJob();if(job==null)return;string file=job.Snapshot().File;
                if(File.Exists(file))try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,"+Arguments.Quote(file)){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(taskDialog,ex.Message);}
            };
            var close=ButtonAt(taskDialog,"关闭",756,500,140,36,true);close.Click+=(s,e)=>taskDialog.Close();
            taskDialog.Resize+=(s,e)=>
            {
                float w=taskDialog.ClientSize.Width/uiScale,h=taskDialog.ClientSize.Height/uiScale;
                Place(taskSummary,26,65,w-52,28);Place(taskGrid,24,104,w-48,h-295);Place(taskDetails,26,h-177,w-52,113);
                Place(taskCancel,24,h-50,120,36);Place(taskOpen,156,h-50,170,36);Place(close,w-164,h-50,140,36);
            };
            taskDialog.FormClosed+=(s,e)=>{taskDialog=null;taskGrid=null;taskDetails=null;};
            var area=Screen.FromControl(this).WorkingArea;
            taskDialog.Size=new Size(Math.Min(taskDialog.Width,area.Width-S(30)),Math.Min(taskDialog.Height,area.Height-S(30)));
            // Trigger initial placement even when the requested size already fits the screen.
            Place(taskGrid,24,104,taskDialog.ClientSize.Width/uiScale-48,taskDialog.ClientSize.Height/uiScale-295);
            Place(taskDetails,26,taskDialog.ClientSize.Height/uiScale-177,taskDialog.ClientSize.Width/uiScale-52,113);
            RefreshTaskDialog();taskDialog.Show(this);
        }
        BatchJob SelectedJob(){return taskGrid==null||taskGrid.CurrentRow==null?null:taskGrid.CurrentRow.Tag as BatchJob;}
        void RefreshTaskDialog()
        {
            if(taskDialog==null||taskGrid==null||taskDialog.IsDisposed)return;
            if(taskGrid.Rows.Count!=jobs.Count||(jobs.Count>0&&!Object.ReferenceEquals(taskGrid.Rows[0].Tag,jobs[0])))
            {taskGrid.Rows.Clear();foreach(var job in jobs){int row=taskGrid.Rows.Add();taskGrid.Rows[row].Tag=job;}}
            for(int i=0;i<jobs.Count;i++)
            {
                var v=jobs[i].Snapshot();var row=taskGrid.Rows[i];
                object[] values={v.Number,v.Title,JobStateText(v.State),Math.Floor(v.Progress)+"%",v.Speed>0?SizeText(v.Speed)+"/s":"—",v.Bytes>0?SizeText(v.Bytes)+(v.Total>0?" / "+SizeText(v.Total):""):"—",v.Eta>0?TimeSpan.FromSeconds(v.Eta).ToString(@"hh\:mm\:ss"):"—"};
                for(int c=0;c<values.Length;c++)if(!Object.Equals(row.Cells[c].Value,values[c]))row.Cells[c].Value=values[c];
                row.DefaultCellStyle.ForeColor=v.State==JobState.Failed?Color.FromArgb(180,61,50):Ink;
            }
            var views=jobs.Select(j=>j.Snapshot()).ToList();
            taskSummary.Text=jobs.Count==0?"尚无任务。先在主窗口输入链接，再点击“解析链接”。":"共 "+jobs.Count+" 项 · 已完成 "+views.Count(v=>v.State==JobState.Complete)+" 项 · 失败 "+views.Count(v=>v.State==JobState.Failed)+" 项 · 最多 3 项并行；关闭窗口不影响下载";
            taskCancel.Enabled=busy&&operation!=null&&!operation.IsCancellationRequested;
            RefreshTaskDetails();
        }
        void RefreshTaskDetails()
        {
            if(taskDetails==null)return;
            var job=SelectedJob();
            if(job==null){taskDetails.Text="选择一条任务，查看链接、保存位置、错误原因和详细日志。";if(taskOpen!=null)taskOpen.Enabled=false;return;}
            var v=job.Snapshot();
            string text=v.Title+"\r\n链接："+v.Url+"\r\n"+JobStateText(v.State)+"："+v.Message+(String.IsNullOrEmpty(v.File)?"":"\r\n保存位置："+v.File)+"\r\n\r\n"+v.Log;
            if(taskDetails.Text!=text)taskDetails.Text=text;
            if(taskOpen!=null)taskOpen.Enabled=v.State==JobState.Complete&&File.Exists(v.File);
        }
        async Task RunBatchSmoke(string origin)
        {
            if(taskDialog==null||taskGrid.Rows.Count!=1)throw new Exception("Task dialog did not open with single task");
            taskDialog.Close();
            ClientSize=new Size(S(1180),S(748));Navigate("home");
            url.Text=origin+"/video.mp4";url.Focus();url.SelectionStart=url.TextLength;
            var previous=activeTask;SendMessage(url.Handle,0x102,(IntPtr)13,IntPtr.Zero);
            if(!url.Text.Contains("\n")||busy||activeTask!=previous)throw new Exception("Enter must insert newline without parsing");
            url.Text=origin+"/slow-a.mp4\r\n"+origin+"/slow-b.mp4；"+origin+"/slow-c.mp4;"+origin+"/slow-d.mp4\r\ninvalid link";
            parse.PerformClick();await activeTask;
            if(jobs.Count!=5||jobs.Count(j=>j.Media!=null)!=4||!download.Enabled)throw new Exception("Batch parse failed");
            taskNav.PerformClick();
            if(taskDialog==null||taskGrid.Rows.Count!=5)throw new Exception("Batch tasks missing from dialog");
            download.PerformClick();
            bool overlap=false;
            for(int i=0;i<80&&busy;i++)
            {
                var views=jobs.Select(j=>j.Snapshot()).ToList();
                overlap|=views.Count(j=>j.State==JobState.Downloading)>=2&&views.Any(j=>j.State==JobState.Queued);
                if(overlap&&views.Any(j=>j.Progress>5))break;
                await Task.Delay(100);
            }
            if(!overlap||!busy)throw new Exception("Batch GUI did not run concurrent queued downloads");
            taskDialog.Close();if(!busy)throw new Exception("Closing task dialog stopped download");
            taskNav.PerformClick();RefreshBatchView();
            using(var bitmap=new Bitmap(taskDialog.Width,taskDialog.Height)){taskDialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,taskDialog.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build","batch-tasks-preview.png"));}
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build","batch-running-preview.png"));}
            await activeTask;
            if(jobs.Count(j=>j.Snapshot().State==JobState.Complete)!=4||status.Text!="部分下载完成"||meter.Value!=80)throw new Exception("Batch aggregate completion or failure state incorrect");
            if(taskGrid.Rows[4].Cells[2].Value.ToString()!="失败")throw new Exception("Task error missing from dialog");
            taskDialog.Close();
            folder.Text=Path.Combine(folder.Text,"canceled");download.PerformClick();await Task.Delay(700);
            cancel.PerformClick();await activeTask;
            if(busy||cancel.Enabled||jobs.Count(j=>j.Snapshot().State==JobState.Canceled)!=4)throw new Exception("GUI cancel did not stop all jobs");
            ((Button)inputCard.Controls.Find("ClearLink",false)[0]).PerformClick();
            if(url.TextLength!=0||jobs.Count!=0||download.Enabled||meter.Value!=0)throw new Exception("Clear left stale batch state");
            ValidateInterfaceLayout();
        }
    }
}
