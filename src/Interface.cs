using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LinkHarbor
{
    sealed partial class MainForm
    {
        float uiScale=1;
        MeshPanel surface;
        Panel workspace, body, topbar, hero;
        Card inputCard, mediaCard, transferCard, saveCard, networkCard;
        WorkflowStrip workflow;
        Label heroTitle, heroCaption, footer;
        SoftButton homeNav, taskNav, settingsNav, helpNav;
        readonly ToolTip tips = new ToolTip { AutoPopDelay=9000, InitialDelay=450, ReshowDelay=150 };
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd,int msg,IntPtr wParam,string lParam);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,int msg,IntPtr wParam,IntPtr lParam);
        int S(float n){return (int)Math.Round(n*uiScale);}
        void Place(Control c,float x,float y,float w,float h){c.SetBounds(S(x),S(y),S(w),S(h));}
        Font TextFont(float size,bool bold=false){return new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);}
        Label LabelAt(Control p,string text,int x,int y,int w,int h,float size,Color color,bool bold)
        {
            var c=new Label{Text=text,Font=TextFont(size,bold),ForeColor=color,BackColor=Color.Transparent,AutoEllipsis=true};Place(c,x,y,w,h);p.Controls.Add(c);return c;
        }
        SoftButton ButtonAt(Control p,string text,int x,int y,int w,int h,bool primary,string icon=null)
        {
            var b=new SoftButton{Text=text,Primary=primary,Symbol=icon,Font=TextFont(9,primary),AccessibleName=String.IsNullOrEmpty(text)?icon:text};Place(b,x,y,w,h);p.Controls.Add(b);return b;
        }
        Card MakeCard(Control p){var c=new Card();p.Controls.Add(c);return c;}
        TextBox TextAt(Control p,string placeholder,bool readOnly=false,bool multiline=false)
        {
            var box=MakeCard(p);box.Radius=15;box.FillColor=Color.White;
            var t=new TextBox{Font=TextFont(10),ForeColor=Ink,BackColor=Color.White,BorderStyle=BorderStyle.None,ReadOnly=readOnly,AccessibleName=placeholder,Multiline=multiline,AcceptsReturn=multiline,ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None,MaxLength=1048576};box.Controls.Add(t);
            box.Resize+=(s,e)=>{t.SetBounds(S(15),multiline?S(8):Math.Max(S(8),(box.Height-t.PreferredHeight)/2),Math.Max(S(20),box.Width-S(30)),multiline?Math.Max(S(24),box.Height-S(16)):t.PreferredHeight);};
            if(!multiline)t.HandleCreated+=(s,e)=>SendMessage(t.Handle,0x1501,(IntPtr)1,placeholder);
            else
            {
                var hint=new Label{Text=placeholder,Font=t.Font,ForeColor=Muted,BackColor=Color.White};box.Controls.Add(hint);hint.BringToFront();
                box.Resize+=(s,e)=>hint.Bounds=new Rectangle(S(15),S(8),Math.Max(S(20),box.Width-S(48)),box.Height-S(16));
                Action updateHint=()=>hint.Visible=t.TextLength==0&&!t.Focused;
                t.TextChanged+=(s,e)=>updateHint();t.GotFocus+=(s,e)=>updateHint();t.LostFocus+=(s,e)=>updateHint();hint.Click+=(s,e)=>t.Focus();
            }
            t.GotFocus+=(s,e)=>box.Invalidate();t.LostFocus+=(s,e)=>box.Invalidate();
            return t;
        }
        ChoiceBox ComboAt(Control p){var c=new ChoiceBox{Font=TextFont(9.5f)};p.Controls.Add(c);return c;}

        void BuildInterface()
        {
            using(var g=CreateGraphics())uiScale=g.DpiX/96f;
            Text="拾链 " + AppInfo.DisplayVersion + " · 视频解析下载";Font=TextFont(10);ForeColor=Ink;BackColor=Color.FromArgb(170,188,224);
            FormBorderStyle=FormBorderStyle.None;AutoScaleMode=AutoScaleMode.None;Padding=new Padding(S(6));
            ClientSize=new Size(S(1180),S(748));MinimumSize=new Size(S(840),S(650));StartPosition=FormStartPosition.CenterScreen;
            var screen=Screen.FromControl(this).WorkingArea;Size=new Size(Math.Min(Width,screen.Width-S(30)),Math.Min(Height,screen.Height-S(30)));
            MaximizedBounds=screen;
            try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            surface=new MeshPanel{Dock=DockStyle.Fill};Controls.Add(surface);
            surface.Resize+=(s,e)=>ApplyCorners();
            Resize+=(s,e)=>{ApplyCorners();LayoutShell();};

            topbar=new Panel{BackColor=Color.Transparent};surface.Controls.Add(topbar);
            var logo=ButtonAt(topbar,"",0,0,42,42,false,"link");logo.AccessibleName="拾链首页";logo.Click+=(s,e)=>Navigate("home");
            var wordmark=LabelAt(topbar,"拾链",54,2,70,27,16,Ink,true);
            LabelAt(topbar,AppInfo.DisplayVersion + " · 正式版",55,30,120,14,7,Muted,false);
            workflow=new WorkflowStrip{Font=TextFont(8.5f)};topbar.Controls.Add(workflow);
            var minimize=ButtonAt(topbar,"",0,0,32,32,false,"min");tips.SetToolTip(minimize,"最小化");minimize.Click+=(s,e)=>WindowState=FormWindowState.Minimized;
            var maximize=ButtonAt(topbar,"",0,0,32,32,false,"max");tips.SetToolTip(maximize,"最大化 / 还原");maximize.Click+=(s,e)=>ToggleMaximize();
            var close=ButtonAt(topbar,"",0,0,32,32,false,"close");tips.SetToolTip(close,"关闭");close.Click+=(s,e)=>Close();
            topbar.Resize+=(s,e)=>{float w=topbar.Width/uiScale;Place(workflow,Math.Max(190,(w-470)/2),2,400,42);Place(minimize,w-112,6,32,32);Place(maximize,w-75,6,32,32);Place(close,w-38,6,32,32);};
            topbar.MouseDown+=DragWindow;wordmark.MouseDown+=DragWindow;topbar.DoubleClick+=(s,e)=>ToggleMaximize();

            homeNav=Rail("home","下载工作台",195);homeNav.Selected=true;homeNav.Click+=(s,e)=>Navigate("home");
            taskNav=Rail("download","当前下载任务",247);taskNav.Click+=(s,e)=>Navigate("task");
            var folderNav=Rail("folder","打开下载文件夹",299);folderNav.Click+=(s,e)=>OpenFolder();
            settingsNav=Rail("network","网络与登录设置",351);settingsNav.Click+=(s,e)=>Navigate("settings");
            var logNav=Rail("log","查看任务日志",403);logNav.Click+=(s,e)=>ShowLog();
            helpNav=Rail("help","注意 · 支持平台与使用说明",0);helpNav.Click+=(s,e)=>ShowHelp();
            surface.Resize+=(s,e)=>Place(helpNav,20,surface.Height/uiScale-67,40,40);

            workspace=new Panel{BackColor=Color.Transparent,AutoScroll=true};surface.Controls.Add(workspace);
            body=new Panel{BackColor=Color.Transparent};workspace.Controls.Add(body);
            workspace.ClientSizeChanged+=(s,e)=>LayoutWorkspace();

            hero=new Panel{BackColor=Color.Transparent};body.Controls.Add(hero);
            LabelAt(hero,"你的视频收藏，从这里开始",0,0,440,24,9,Muted,false);
            heroTitle=LabelAt(hero,"好内容，留在身边。",0,19,550,36,23,Ink,true);
            heroCaption=LabelAt(hero,"一次收藏多个视频，把喜欢的内容保存到本地。",2,57,560,22,9,Muted,false);
            var sculpture=new LinkSculpture();hero.Controls.Add(sculpture);
            hero.Resize+=(s,e)=>{float w=hero.Width/uiScale;Place(sculpture,w-250,0,250,78);sculpture.Visible=w>=850;};

            inputCard=MakeCard(body);
            var linkTitle=LabelAt(inputCard,"添加视频链接",22,12,125,25,10,Ink,true);
            var linkNote=LabelAt(inputCard,"以回车换行或者；分割链接进行多视频同时下载",150,14,600,23,8.5f,Muted,false);
            url=TextAt(inputCard,"粘贴视频链接，每行一个，也可使用 ; 或 ； 分隔",false,true);
            var clearLink=ButtonAt(inputCard,"清除",0,0,76,40,false);clearLink.Name="ClearLink";
            parse=ButtonAt(inputCard,"解析链接",0,0,132,44,true,"arrow");
            inputCard.Resize+=(s,e)=>{float w=inputCard.Width/uiScale;Place(url.Parent,20,42,w-255,62);Place(clearLink,w-223,53,75,40);Place(parse,w-140,53,120,40);Place(linkNote,150,14,w-172,23);};
            clearLink.Click+=(s,e)=>{url.Clear();ClearBatch();SetStatus("准备就绪","输入视频链接后，点击“解析链接”。");url.Focus();};
            parse.Click+=(s,e)=>StartOperation(ParseMedia);
            url.TextChanged+=(s,e)=>{if(jobs.Count>0&&url.Text!=parsedInput){ClearBatch();SetStatus("链接已更改","点击“解析链接”获取这批视频的信息。");}};

            mediaCard=MakeCard(body);
            LabelAt(mediaCard,"视频与音频",22,14,240,27,11,Ink,true);
            var mediaChip=LabelAt(mediaCard,"自由选择下载内容",0,20,150,24,8,Muted,false);mediaChip.TextAlign=ContentAlignment.MiddleRight;
            var glyph=new MediaGlyph();mediaCard.Controls.Add(glyph);Place(glyph,22,51,58,58);
            mediaTitle=LabelAt(mediaCard,"等待解析视频",106,60,400,48,13,Ink,true);
            mediaMeta=LabelAt(mediaCard,"解析后显示视频标题、时长与画质",107,105,400,22,8.5f,Muted,false);
            mode=ComboAt(mediaCard);mode.AccessibleName="下载内容";mode.Items.AddRange(new object[]{"视频 + 音频","仅音频 · 原格式","仅音频 · MP3"});mode.SelectedIndex=0;
            quality=ComboAt(mediaCard);quality.AccessibleName="最高画质";quality.Items.Add(new Quality{Label="解析后选择画质"});quality.SelectedIndex=0;
            modeHint=LabelAt(mediaCard,"",24,193,500,24,8,Muted,false);
            mediaCard.Resize+=(s,e)=>{float w=mediaCard.Width/uiScale;Place(mediaChip,w-178,16,154,24);Place(mediaTitle,96,50,w-120,37);Place(mediaMeta,97,91,w-122,24);Place(mode,22,122,(w-54)/2,38);Place(quality,32+(w-54)/2,122,(w-54)/2,38);Place(modeHint,24,171,w-48,24);};
            mode.SelectedIndexChanged+=(s,e)=>RefreshSelection();

            transferCard=MakeCard(body);
            LabelAt(transferCard,"当前任务 · 总进度",22,14,260,26,11,Ink,true);
            percent=LabelAt(transferCard,"—",0,60,110,44,26,Blue,true);percent.Font=new Font("Segoe UI",26,FontStyle.Regular);percent.TextAlign=ContentAlignment.MiddleRight;
            status=LabelAt(transferCard,"准备就绪",22,66,200,30,15,Ink,true);
            meter=new Meter();transferCard.Controls.Add(meter);
            detail=LabelAt(transferCard,"解析链接后，即可开始下载。",22,120,350,48,8.5f,Muted,false);
            download=ButtonAt(transferCard,"开始下载",22,176,220,40,true,"download");
            cancel=ButtonAt(transferCard,"取消全部",0,176,80,40,false);cancel.Enabled=false;
            transferCard.Resize+=(s,e)=>{float w=transferCard.Width/uiScale;Place(percent,w-133,38,110,48);Place(status,22,51,w-160,30);Place(meter,22,93,w-44,6);Place(detail,22,108,w-44,38);Place(download,22,151,w-134,36);Place(cancel,w-100,151,78,36);};
            download.Click+=(s,e)=>StartOperation(DownloadMedia);cancel.Click+=(s,e)=>CancelOperation();

            saveCard=MakeCard(body);
            LabelAt(saveCard,"保存位置",22,14,180,27,11,Ink,true);
            folder=TextAt(saveCard,"下载文件夹",true);
            browse=ButtonAt(saveCard,"更改位置",22,108,112,36,false);
            var open=ButtonAt(saveCard,"打开文件夹",0,120,148,36,false,"folder");
            saveCard.Resize+=(s,e)=>{float w=saveCard.Width/uiScale;Place(folder.Parent,20,51,w-40,40);Place(open,w-168,108,148,36);};
            browse.Click+=(s,e)=>{using(var picker=new FolderBrowserDialog{Description="选择视频的本地保存位置",SelectedPath=folder.Text})if(picker.ShowDialog(this)==DialogResult.OK){folder.Text=picker.SelectedPath;SaveSettings();}};
            open.Click+=(s,e)=>OpenFolder();

            networkCard=MakeCard(body);network=networkCard;
            LabelAt(networkCard,"网络与登录",22,14,200,27,11,Ink,true);
            var optional=LabelAt(networkCard,"按需设置",0,22,80,24,8,Muted,false);optional.TextAlign=ContentAlignment.MiddleRight;
            proxy=TextAt(networkCard,"代理（可选）  http://127.0.0.1:7890");
            cookiesButton=ButtonAt(networkCard,"选择 Cookie 文件",22,100,160,34,false);
            clearCookies=ButtonAt(networkCard,"清除",190,100,65,34,false);
            cookieLabel=LabelAt(networkCard,"未使用登录信息 · Netscape .txt",23,158,450,20,8,Muted,false);
            var cookieHelp=ButtonAt(networkCard,"如何获取？",0,115,106,36,false);cookieHelp.Click+=(s,e)=>ShowNetworkHelp();
            networkCard.Resize+=(s,e)=>{float w=networkCard.Width/uiScale;Place(optional,w-102,18,80,24);Place(proxy.Parent,20,51,w-40,40);Place(cookieHelp,w-126,100,106,34);Place(cookieLabel,23,140,w-46,22);};
            cookiesButton.Click+=(s,e)=>{using(var picker=new OpenFileDialog{Title="选择 Netscape 格式 Cookie 文件",Filter="Cookie 文本文件|*.txt|所有文件|*.*"})if(picker.ShowDialog(this)==DialogResult.OK){cookies=picker.FileName;cookieLabel.Text="已选择："+Path.GetFileName(cookies);tips.SetToolTip(cookieLabel,cookieLabel.Text);}};
            clearCookies.Click+=(s,e)=>{cookies="";cookieLabel.Text="未使用登录信息 · Netscape .txt";tips.SetToolTip(cookieLabel,"");};
            proxy.Leave+=(s,e)=>SaveSettings();

            footer=LabelAt(body,"本地保存  ·  原始画质  ·  自动合并音视频",2,0,600,24,8,Muted,false);
            var localBadge=LabelAt(body,"POWERED BY YT-DLP",0,0,180,24,7.5f,Muted,false);localBadge.Font=new Font("Segoe UI",7.5f);localBadge.TextAlign=ContentAlignment.MiddleRight;
            body.Resize+=(s,e)=>{Place(localBadge,body.Width/uiScale-180,body.Height/uiScale-27,180,24);};
            log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",10),BackColor=Color.White,ForeColor=Ink,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,MaxLength=0};
            editable.AddRange(new Control[]{url,clearLink,parse,mode,quality,browse,proxy,cookiesButton,clearCookies});
            LayoutShell();LayoutWorkspace();
            Shown+=(s,e)=>{ApplyCorners();LayoutShell();LayoutWorkspace();};
        }

        SoftButton Rail(string icon,string title,int y)
        {
            var b=ButtonAt(surface,"",20,y,40,40,false,icon);b.AccessibleName=title;tips.SetToolTip(b,title);return b;
        }
        void LayoutShell()
        {
            if(topbar==null||workspace==null)return;
            float w=surface.ClientSize.Width/uiScale,h=surface.ClientSize.Height/uiScale;
            Place(topbar,20,17,w-40,49);Place(workspace,96,86,w-122,h-103);if(helpNav!=null)Place(helpNav,20,h-67,40,40);
        }
        void ApplyCorners()
        {
            if(surface==null||Width<1||Height<1)return;
            using(var path=Theme.Round(new RectangleF(0,0,Width,Height),S(WindowState==FormWindowState.Maximized?0:30)))
            {var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}
            if(surface.Width>0&&surface.Height>0)using(var path=Theme.Round(new RectangleF(0,0,surface.Width,surface.Height),S(24)))
            {var old=surface.Region;surface.Region=new Region(path);if(old!=null)old.Dispose();}
        }
        bool arranging;
        void LayoutWorkspace()
        {
            if(arranging||networkCard==null)return;arranging=true;
            try
            {
                float w=workspace.ClientSize.Width/uiScale-4;bool compact=w<860;
                float contentHeight=compact?1026:628;
                body.SetBounds(0,workspace.AutoScrollPosition.Y,S(w),S(contentHeight));
                Place(hero,2,0,w-4,80);Place(inputCard,0,82,w,112);
                if(compact)
                {
                    Place(mediaCard,0,210,w,200);Place(transferCard,0,426,w,200);
                    Place(saveCard,0,642,w,168);Place(networkCard,0,826,w,168);contentHeight=1026;
                }
                else
                {
                    float left=(w-18)*.57f;Place(mediaCard,0,210,left,200);Place(transferCard,left+18,210,w-left-18,200);
                    float half=(w-18)*.48f;Place(saveCard,0,426,half,168);Place(networkCard,half+18,426,w-half-18,168);
                }
                body.Height=S(contentHeight);Place(footer,2,contentHeight-27,w-200,24);
            }
            finally{arranging=false;}
        }
        void Navigate(string target)
        {
            homeNav.Selected=target=="home";taskNav.Selected=target=="task";settingsNav.Selected=target=="settings";
            homeNav.Invalidate();taskNav.Invalidate();settingsNav.Invalidate();
            if(target=="home"){workspace.AutoScrollPosition=Point.Empty;url.Focus();}
            else if(target=="task"){ShowTasks();}
            else {workspace.ScrollControlIntoView(networkCard);proxy.Focus();}
        }
        void ValidateInterfaceLayout()
        {
            foreach(Control child in body.Controls)
                if(child is SoftButton)throw new Exception("Duplicate top navigation row is still present");
            foreach(Control panel in new Control[]{inputCard,mediaCard,transferCard,saveCard,networkCard})
            foreach(Control child in panel.Controls)
                if(child.Visible && (child.Left<0||child.Top<0||child.Right>panel.Width||child.Bottom>panel.Height))
                    throw new Exception("Control outside card: "+child.GetType().Name+" / "+child.Text);
            if(mode.Bounds.IntersectsWith(quality.Bounds)||download.Bounds.IntersectsWith(cancel.Bounds)||cookiesButton.Bounds.IntersectsWith(clearCookies.Bounds))
                throw new Exception("Interactive controls overlap");
            if(helpNav.Top<topbar.Bottom||helpNav.Bottom>surface.Height)throw new Exception("Sidebar help outside window");
            if(Region==null||Region.IsVisible(0,0))throw new Exception("Rounded window region missing");
        }
        void ValidateButtonBackgrounds()
        {
            // Start from deliberately polluted pixels, then exercise the real paint
            // methods. All four exterior corners must match the parent's rendering.
            foreach(SoftButton button in new SoftButton[]{homeNav,settingsNav,(SoftButton)parse,(SoftButton)download})
            {
                bool enabled=button.Enabled;
                foreach(bool state in new[]{true,false})
                {
                    button.Enabled=state;
                    var parent=button.Parent;
                    using(var backdrop=new Bitmap(parent.Width,parent.Height))
                    using(var rendered=new Bitmap(button.Width,button.Height))
                    {
                        button.Visible=false;
                        try{parent.DrawToBitmap(backdrop,parent.ClientRectangle);}finally{button.Visible=true;}
                        using(var graphics=Graphics.FromImage(rendered))
                        using(var paint=new PaintEventArgs(graphics,button.ClientRectangle))
                        {
                            graphics.Clear(Color.Magenta);
                            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                            typeof(SoftButton).GetMethod("OnPaintBackground",flags).Invoke(button,new object[]{paint});
                            typeof(SoftButton).GetMethod("OnPaint",flags).Invoke(button,new object[]{paint});
                        }
                        foreach(var corner in new[]{new Point(0,0),new Point(button.Width-1,0),new Point(0,button.Height-1),new Point(button.Width-1,button.Height-1)})
                        {
                            Color actual=rendered.GetPixel(corner.X,corner.Y),expected=backdrop.GetPixel(button.Left+corner.X,button.Top+corner.Y);
                            if(Math.Abs(actual.R-expected.R)>3||Math.Abs(actual.G-expected.G)>3||Math.Abs(actual.B-expected.B)>3)
                                throw new Exception("Stale button background: "+button.AccessibleName+" / "+corner+" / "+actual+" vs "+expected);
                        }
                    }
                }
                button.Enabled=enabled;
            }
        }
        void UpdateWorkflow(string title)
        {
            if(workflow==null)return;
            workflow.Step=batchPhase=="download"||title.Contains("下载")||title.Contains("合并")||title.Contains("处理")?2:batchPhase=="parse"||title.Contains("解析")?1:media==null?0:1;workflow.Invalidate();
        }
        void OpenFolder(){try{Directory.CreateDirectory(folder.Text);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",Arguments.Quote(folder.Text)){UseShellExecute=true});}catch(Exception ex){SetError(ex.Message);}}
        void ShowLog()
        {
            using(var viewer=new Form{Text="拾链 · 任务日志",Size=new Size(S(850),S(520)),StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=Theme.Surface,Padding=new Padding(S(22))})
            {
                var title=new Label{Text="任务日志",Dock=DockStyle.Top,Height=S(52),Font=TextFont(17,true),ForeColor=Ink};
                var close= new SoftButton{Text="关闭",Dock=DockStyle.Bottom,Height=S(40),Font=TextFont(10),Primary=true};close.Click+=(s,e)=>viewer.Close();
                viewer.Controls.Add(log);viewer.Controls.Add(title);viewer.Controls.Add(close);viewer.FormClosing+=(s,e)=>viewer.Controls.Remove(log);viewer.ShowDialog(this);
            }
        }
        void ShowNetworkHelp()
        {
            using(var dialog=CreateNetworkHelp())dialog.ShowDialog(this);
        }
        Form CreateNetworkHelp()
        {
            var dialog=new Form{Text="拾链 · 网络与登录帮助",ClientSize=new Size(S(650),S(480)),AutoScaleMode=AutoScaleMode.None,StartPosition=FormStartPosition.CenterParent,BackColor=Theme.Surface,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
            LabelAt(dialog,"网络与登录帮助",28,22,590,38,20,Ink,true);
            var proxyTab=ButtonAt(dialog,"代理地址与端口",28,77,174,38,true,"network");
            var cookieTab=ButtonAt(dialog,"Cookie 文件",214,77,154,38,false);
            cookieTab.Name="CookieHelpTab";
            var browserTab=ButtonAt(dialog,"浏览器插件",380,77,174,38,false);
            browserTab.Name="BrowserHelpTab";
            var page=new Panel{BackColor=Color.Transparent};Place(page,28,135,594,275);dialog.Controls.Add(page);
            var heading=LabelAt(page,"",0,0,594,28,12,Ink,true);
            var instructions=LabelAt(page,"",0,40,594,225,10,Ink,false);instructions.AutoEllipsis=false;
            var note=LabelAt(dialog,"",30,410,590,22,8.5f,Muted,false);
            var extension=ButtonAt(dialog,"浏览器插件推荐",28,434,172,34,false);
            var chromium=ButtonAt(dialog,"Chrome / Edge 插件",28,434,200,34,false);
            var firefox=ButtonAt(dialog,"Firefox 插件",240,434,160,34,false);
            chromium.Click+=(s,e)=>OpenHelpLink(dialog,"https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc");
            firefox.Click+=(s,e)=>OpenHelpLink(dialog,"https://addons.mozilla.org/en-US/firefox/addon/cookies-txt/");
            Action<int> select=tab=>
            {
                bool cookie=tab==1,browser=tab==2;
                proxyTab.Primary=tab==0;cookieTab.Primary=cookie;browserTab.Primary=browser;
                proxyTab.Invalidate();cookieTab.Invalidate();browserTab.Invalidate();
                extension.Visible=cookie;chromium.Visible=browser;firefox.Visible=browser;
                heading.Text=cookie?"导出当前网站的登录信息":"找到代理软件的本地监听地址";
                instructions.Text=cookie?
                    "1. 安装适合浏览器的 Cookie 导出插件，见“浏览器插件”页。\r\n\r\n2. 打开视频网站，登录自己的账号，进入视频页面确认可播放。\r\n\r\n3. 点击插件，导出当前网站的 Netscape 格式 .txt 文件。\r\n    Chrome / Edge 选择 Netscape → Export；无需导出全部网站。\r\n\r\n4. 在下载器点击“选择 Cookie 文件”，导入保存的 .txt，\r\n    然后重新解析链接。换网站时选用对应网站的 Cookie。":
                    "1. 打开你使用的代理软件，在设置中找“端口”或“本地监听”。\r\n    记录 HTTP 端口或混合端口（Mixed Port）。\r\n\r\n2. 若地址为 127.0.0.1、端口为 7897，在下载器填写：\r\n    http://127.0.0.1:7897（端口必须换成你实际看到的值）\r\n\r\n3. 也可查看 Windows 设置 → 网络和 Internet → 代理，\r\n    在手动代理设置中查看地址和端口；未启用时可能为空。\r\n\r\n4. 只有 SOCKS5 端口时，填写 socks5://127.0.0.1:实际端口。\r\n    下载期间保持代理软件运行；不需要代理可以留空。";
                note.Text=cookie?"Cookie 是登录凭证，请勿分享；登录失效后请重新登录并导出。":"这里需要代理监听地址，不是订阅链接、远程服务器端口或管理 API 地址。";
                if(browser)
                {
                    heading.Text="按浏览器选择 Cookie 导出插件";
                    instructions.Text="Chrome：Get cookies.txt LOCALLY（开发者 kairi003）。\r\n从 Chrome 网上应用店安装，导出时选择 Netscape。\r\n\r\nMicrosoft Edge：使用同一个 Get cookies.txt LOCALLY。\r\n在 Edge 中打开 Chrome 商店链接，按提示允许其他商店的扩展。\r\n\r\nFirefox（火狐）：cookies.txt（开发者 Lennon Hill）。\r\n从 Firefox 附加组件商店安装，导出当前站点的 Netscape 文件。\r\n\r\n其他浏览器：若支持 Chrome 扩展，可尝试上面的 Chrome 插件；\r\n兼容性以浏览器为准。请在需要安装插件的浏览器中打开商店链接。";
                    note.Text="下方入口在系统默认浏览器打开；可将商店地址复制到目标浏览器。";
                }
            };
            proxyTab.Click+=(s,e)=>select(0);cookieTab.Click+=(s,e)=>select(1);browserTab.Click+=(s,e)=>select(2);
            extension.Click+=(s,e)=>select(2);
            var done=ButtonAt(dialog,"知道了",482,434,140,34,true);done.Click+=(s,e)=>dialog.Close();
            select(0);return dialog;
        }
        void OpenHelpLink(Form owner,string address)
        {
            try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address){UseShellExecute=true});}
            catch(Exception ex){MessageBox.Show(owner,ex.Message,"无法打开链接");}
        }
        void ShowHelp(){using(var dialog=CreateUsageHelp())dialog.ShowDialog(this);}
        Form CreateUsageHelp()
        {
            var dialog=new Form{Text="拾链 " + AppInfo.DisplayVersion + " · 注意与使用说明",ClientSize=new Size(S(650),S(690)),AutoScaleMode=AutoScaleMode.None,StartPosition=FormStartPosition.CenterParent,BackColor=Theme.Surface,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
            LabelAt(dialog,"注意 · 支持平台与使用说明",28,22,594,38,20,Ink,true);
            LabelAt(dialog,"输入多个链接 → 点击解析 → 统一选择画质或音频 → 并行下载",30,72,590,28,10,Muted,false);
            LabelAt(dialog,"常见支持平台",30,116,590,28,12,Ink,true);
            var platforms=LabelAt(dialog,"国内视频：B站（哔哩哔哩）、AcFun、优酷、爱奇艺\r\n短视频与社交：抖音、小红书、微博、TikTok\r\n海外视频：YouTube（含 Shorts）、Vimeo、Dailymotion、Niconico\r\n社交视频：X / Twitter、Facebook、Instagram、Reddit\r\n回放与片段：Twitch",30,154,590,122,10,Ink,false);
            platforms.AutoEllipsis=false;
            LabelAt(dialog,"使用前请注意",30,292,590,28,12,Ink,true);
            var notes=LabelAt(dialog,"• 用回车换行、英文 ; 或中文 ； 分隔链接；回车仅换行，点击按钮解析。\r\n• 最多 3 项并行，其余自动排队；画质、音频、保存位置统一应用。\r\n• 左侧下载图标打开任务弹窗，查看进度、速度、失败原因和保存位置。\r\n• 总进度按任务平均估算，含合并与保存；失败任务不会算作完成。\r\n• 关闭任务弹窗不停止下载；“取消全部”会停止运行及排队任务。\r\n• 重复链接自动忽略；不处理合集、频道或正在直播的内容。\r\n• 不同网站均需登录时，可分批下载并切换对应的 Cookie 文件。\r\n• 平台未逐一实测；网站改版、账号权限、地区和 DRM 可能影响下载。\r\n• 默认保存到工具旁的“下载”文件夹；批量文件名附短标识以避免重名。",30,330,590,195,9.5f,Muted,false);
            notes.AutoEllipsis=false;
            var credits=LabelAt(dialog,"拾链 " + AppInfo.DisplayVersion + " · 作者：雪落a · B站 ID：雪落a77\r\n基于 yt-dlp，提供 Windows UI 适配、批量任务管理与便携封装。\r\nGPL-3.0 开源；不希望直接打包售卖，此倡议不构成额外许可限制。\r\nxueluo.online：作者使用 AI 开发的其他程序。",30,534,590,92,9,Muted,false);
            credits.AutoEllipsis=false;
            var sites=ButtonAt(dialog,"查看官方完整列表",28,640,190,34,false);
            sites.Click+=(s,e)=>OpenHelpLink(dialog,"https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md");
            var author=ButtonAt(dialog,"作者网站",232,640,150,34,false);author.Click+=(s,e)=>OpenHelpLink(dialog,"https://xueluo.online");
            var done=ButtonAt(dialog,"知道了",482,640,140,34,true);done.Click+=(s,e)=>dialog.Close();
            return dialog;
        }
        void ShowNote(string heading,string text,bool extension)
        {
            using(var dialog=new Form{Text="拾链 · "+heading,ClientSize=new Size(S(550),S(400)),StartPosition=FormStartPosition.CenterParent,BackColor=Theme.Surface,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false})
            {
                LabelAt(dialog,heading,28,24,495,40,20,Ink,true);LabelAt(dialog,text,30,83,490,245,10,Muted,false);
                var done=ButtonAt(dialog,"知道了",380,344,140,38,true);done.Click+=(s,e)=>dialog.Close();
                if(extension){var visit=ButtonAt(dialog,"打开扩展页面",28,344,180,38,false);visit.Click+=(s,e)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc"){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(dialog,ex.Message);}};}
                dialog.ShowDialog(this);
            }
        }
        void ToggleMaximize(){WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
        void DragWindow(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}}
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x84&&WindowState==FormWindowState.Normal)
            {
                long packed=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));int edge=S(8);
                bool l=p.X<edge,r=p.X>=Width-edge,t=p.Y<edge,b=p.Y>=Height-edge;
                if(l||r||t||b){m.Result=(IntPtr)(t?(l?13:r?14:12):b?(l?16:r?17:15):l?10:11);return;}
            }
            base.WndProc(ref m);
        }
    }
}
