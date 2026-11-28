using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkHarbor
{
    sealed partial class MainForm
    {
        static string DescribeCookieSites(string path)
        {
            try
            {
                var sites=SiteCookieFile.ReadSites(path);
                if(sites.Count==0)return "未识别到网站";
                return String.Join("、",sites.Select(site=>site.Key+(site.Value?"":"（已过期）")));
            }
            catch(FileNotFoundException) {return "文件不存在";}
            catch(DirectoryNotFoundException) {return "文件不存在";}
            catch(UnauthorizedAccessException) {return "无读取权限";}
            catch(IOException) {return "暂时无法读取";}
        }

        Form CreateCookieManager()
        {
            var dialog=new Form{
                Text="拾链 · 管理 Cookie",ClientSize=new Size(S(680),S(416)),MinimumSize=new Size(S(460),S(310)),
                StartPosition=FormStartPosition.CenterParent,AutoScaleMode=AutoScaleMode.None,
                Font=Font,Icon=Icon,BackColor=Theme.Surface,MinimizeBox=false,ShowInTaskbar=false
            };
            LabelAt(dialog,"管理 Cookie",24,18,300,38,19,Ink,true);
            var grid=new DataGridView{
                Name="CookieBindings",AccessibleName="已绑定的 Cookie 文件和对应网站",ReadOnly=true,
                AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,
                RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells,
                EnableHeadersVisualStyles=false,BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,
                BackgroundColor=Color.White,GridColor=Theme.Border,Font=TextFont(9)
            };
            grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Theme.Surface,ForeColor=Ink,Font=TextFont(9,true)};
            grid.ColumnHeadersHeight=S(34);
            grid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.White,ForeColor=Ink,
                SelectionBackColor=Color.FromArgb(225,234,255),SelectionForeColor=Ink,Padding=new Padding(S(6)),WrapMode=DataGridViewTriState.True};
            grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="已绑定文件",FillWeight=42,SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="对应网站",FillWeight=58,SortMode=DataGridViewColumnSortMode.NotSortable});
            dialog.Controls.Add(grid);Place(grid,24,68,632,238);
            grid.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
            var empty=LabelAt(dialog,"尚未绑定 Cookie 文件",24,68,632,238,10,Muted,false);
            empty.TextAlign=ContentAlignment.MiddleCenter;empty.Anchor=grid.Anchor;
            var location=LabelAt(dialog,"",24,315,632,30,9,Muted,false);
            location.Anchor=AnchorStyles.Left|AnchorStyles.Bottom|AnchorStyles.Right;
            var remove=ButtonAt(dialog,"移除选中",24,358,110,36,false);
            var clear=ButtonAt(dialog,"清除全部",144,358,100,36,false);
            var close=ButtonAt(dialog,"关闭",550,358,106,36,true);
            remove.Anchor=clear.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
            close.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;
            close.DialogResult=DialogResult.Cancel;dialog.CancelButton=close;

            grid.SelectionChanged+=(s,e)=>{
                string path=grid.SelectedRows.Count==0?null:grid.SelectedRows[0].Tag as string;
                location.Text=path??"";
                remove.Enabled=path!=null;
            };
            var descriptions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            Action refresh=async ()=>{
                grid.Rows.Clear();
                bool hasBindings=cookieFiles.Length>0;
                grid.Visible=clear.Enabled=hasBindings;empty.Visible=!hasBindings;
                foreach(string path in cookieFiles.Reverse())
                {
                    string sites;
                    int index=grid.Rows.Add(Path.GetFileName(path),descriptions.TryGetValue(path,out sites)?sites:"正在读取…");
                    grid.Rows[index].Tag=path;
                    grid.Rows[index].Cells[0].ToolTipText=path;
                }
                grid.ClearSelection();
                remove.Enabled=false;location.Text="";
                foreach(var row in grid.Rows.Cast<DataGridViewRow>().ToArray())
                {
                    string path=(string)row.Tag;
                    if(descriptions.ContainsKey(path))continue;
                    string sites=await Task.Run(()=>DescribeCookieSites(path));
                    // A removal, clear or dialog close may have happened while the file was being read.
                    if(dialog.IsDisposed || dialog.Disposing || row.DataGridView!=grid)return;
                    descriptions[path]=sites;
                    row.Cells[1].Value=sites;
                }
            };
            Action<Action<Settings>> apply=change=>{
                bool saved=ChangeCookieBindings(change);
                refresh();
                if(!saved)MessageBox.Show(dialog,"本次修改已生效，绑定设置未能保存。","管理 Cookie");
            };
            remove.Click+=(s,e)=>{
                if(grid.SelectedRows.Count==0)return;
                string path=(string)grid.SelectedRows[0].Tag;
                apply(state=>state.RemoveCookieFile(path));
            };
            clear.Click+=(s,e)=>apply(state=>state.UpdateCookieFiles(new string[0]));
            dialog.Shown+=(s,e)=>refresh();
            return dialog;
        }
    }
}
