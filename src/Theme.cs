using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LinkHarbor
{
    static class Theme
    {
        public static readonly Color Ink = ColorTranslator.FromHtml("#202832"), Muted = ColorTranslator.FromHtml("#74808D"), Blue = ColorTranslator.FromHtml("#2449ED"), Border = ColorTranslator.FromHtml("#E3E8ED"), Surface = ColorTranslator.FromHtml("#F7FAFC");
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public static void Fill(Graphics g, RectangleF r, float radius, Color color) { using (var p = Round(r, radius)) using (var b = new SolidBrush(color)) g.FillPath(b, p); }
        public static void Line(Graphics g, RectangleF r, float radius, Color color, float width) { using (var p = Round(r, radius)) using (var pen = new Pen(color, width)) g.DrawPath(pen, p); }
        public static void Glow(Graphics g, RectangleF rect, Color color)
        {
            using (var p = new GraphicsPath()) { p.AddEllipse(rect); using (var b = new PathGradientBrush(p)) { b.CenterColor = color; b.SurroundColors = new[] { Color.FromArgb(0, color) }; g.FillPath(b, p); } }
        }
        public static void Icon(Graphics g, string name, RectangleF rect, Color color)
        {
            var state = g.Save(); g.TranslateTransform(rect.X, rect.Y); g.ScaleTransform(rect.Width / 24, rect.Height / 24); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (name == "home") { g.DrawLines(p, new[] { new PointF(3,11),new PointF(12,3),new PointF(21,11) }); g.DrawLines(p, new[] { new PointF(6,10),new PointF(6,20),new PointF(10,20),new PointF(10,14),new PointF(14,14),new PointF(14,20),new PointF(18,20),new PointF(18,10) }); }
                else if (name == "folder") { g.DrawLines(p, new[] { new PointF(3,7),new PointF(3,19),new PointF(21,19),new PointF(21,7),new PointF(12,7),new PointF(10,4),new PointF(3,4),new PointF(3,7),new PointF(21,7) }); }
                else if (name == "download") { g.DrawLine(p,12,3,12,15); g.DrawLines(p,new[] {new PointF(7,10),new PointF(12,15),new PointF(17,10)}); g.DrawLines(p,new[] {new PointF(4,16),new PointF(4,21),new PointF(20,21),new PointF(20,16)}); }
                else if (name == "network") { g.DrawEllipse(p,3,3,18,18); g.DrawEllipse(p,8,3,8,18); g.DrawLine(p,3,12,21,12); }
                else if (name == "log") { g.DrawRectangle(p,5,3,14,18); g.DrawLine(p,9,8,15,8); g.DrawLine(p,9,12,15,12); g.DrawLine(p,9,16,13,16); }
                else if (name == "link") { g.DrawArc(p,3,8,13,9,65,280); g.DrawArc(p,9,5,13,9,245,280); g.DrawLine(p,9,14,16,10); }
                else if (name == "play") { g.DrawEllipse(p,2,2,20,20); g.DrawPolygon(p,new[] {new PointF(10,7),new PointF(17,12),new PointF(10,17)}); }
                else if (name == "check") g.DrawLines(p,new[] {new PointF(5,12),new PointF(10,17),new PointF(19,7)});
                else if (name == "arrow") { g.DrawLine(p,5,12,19,12); g.DrawLines(p,new[] {new PointF(14,7),new PointF(19,12),new PointF(14,17)}); }
                else if (name == "close") { g.DrawLine(p,7,7,17,17); g.DrawLine(p,17,7,7,17); }
                else if (name == "min") g.DrawLine(p,6,15,18,15);
                else if (name == "max") g.DrawRectangle(p,6,6,12,12);
                else if (name == "help") { g.DrawEllipse(p,3,3,18,18); g.DrawArc(p,9,7,6,6,180,240); g.DrawLine(p,12,12,12,14); g.DrawEllipse(p,11.5f,17,1,1); }
            }
            g.Restore(state);
        }
    }

    sealed class MeshPanel : Panel
    {
        public MeshPanel() { DoubleBuffered = true; BackColor = Color.FromArgb(246,248,251); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(246,247,252), Color.FromArgb(244,249,247), 45f)) e.Graphics.FillRectangle(b, ClientRectangle);
            Theme.Glow(e.Graphics,new RectangleF(Width * .48f,Height * .54f,Width * .9f,Height * .9f),Color.FromArgb(122,249,239,186));
            Theme.Glow(e.Graphics,new RectangleF(-Width * .1f,Height * .4f,Width * .9f,Height),Color.FromArgb(90,204,234,248));
            Theme.Glow(e.Graphics,new RectangleF(Width * .5f,-Height * .35f,Width * .8f,Height * .85f),Color.FromArgb(90,220,224,252));
        }
    }
    sealed class Card : Panel
    {
        public int Radius = 25;
        public Color FillColor = Color.FromArgb(244,248,250);
        public Card() { DoubleBuffered = true; BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float scale = e.Graphics.DpiX / 96f; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1,1,Width-3,Height-4);
            Theme.Fill(e.Graphics,new RectangleF(1,3,Width-3,Height-4),Radius*scale,Color.FromArgb(12,119,138,161));
            Theme.Fill(e.Graphics,r,Radius*scale,FillColor); Theme.Line(e.Graphics,r,Radius*scale,Color.FromArgb(215,255,255,255),1);
        }
    }
    sealed class SoftButton : Button
    {
        public bool Primary, Selected;
        public string Symbol;
        bool hover, pressed;
        public SoftButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Opaque, false);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false; BackColor = Color.Transparent; Cursor = Cursors.Hand;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // ButtonBase's themed transparent background can leave its rectangular
            // backing buffer untouched. Repaint the actual parent beneath the entire
            // capsule so resizing, hovering and enabling never expose stale pixels.
            if(Parent == null) { e.Graphics.Clear(Theme.Surface); return; }
            var state = e.Graphics.Save();
            try
            {
                e.Graphics.TranslateTransform(-Left, -Top);
                using(var parentPaint = new PaintEventArgs(e.Graphics, Bounds))
                {
                    InvokePaintBackground(Parent, parentPaint);
                    InvokePaint(Parent, parentPaint);
                }
            }
            finally { e.Graphics.Restore(state); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; float scale = e.Graphics.DpiX/96f;
            bool active = Primary || Selected;
            Color fill = active ? (Enabled ? (pressed ? Color.FromArgb(30,55,195) : hover ? Color.FromArgb(54,82,246) : Theme.Blue) : Color.FromArgb(222,229,247)) : (hover && Enabled ? Color.FromArgb(233,239,248) : Color.FromArgb(246,249,251));
            var rect = new RectangleF(1,1,Width-3,Height-3);
            Theme.Fill(e.Graphics,rect,Height/2f,fill); Theme.Line(e.Graphics,rect,Height/2f,active ? Color.FromArgb(20,Theme.Blue) : Theme.Border,scale);
            Color foreground = !Enabled ? Color.FromArgb(137,150,174) : active ? Color.White : Theme.Ink;
            int icon = (int)(20*scale), gap = (int)(8*scale);
            if (!String.IsNullOrEmpty(Symbol) && String.IsNullOrEmpty(Text)) Theme.Icon(e.Graphics,Symbol,new RectangleF((Width-icon)/2f,(Height-icon)/2f,icon,icon),foreground);
            else
            {
                int tw = TextRenderer.MeasureText(Text,Font).Width, whole = tw + (String.IsNullOrEmpty(Symbol) ? 0 : icon + gap), x = (Width-whole)/2;
                if (!String.IsNullOrEmpty(Symbol)) { Theme.Icon(e.Graphics,Symbol,new RectangleF(x,(Height-icon)/2f,icon,icon),foreground); x += icon+gap; }
                TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(x,0,tw,Height),foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.SingleLine);
            }
            if (Focused && ShowFocusCues) Theme.Line(e.Graphics,new RectangleF(4,4,Width-9,Height-9),Height/2f,active ? Color.White : Theme.Blue,1);
        }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; pressed=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    }
    sealed class Meter : Control
    {
        public int Value;
        public Meter() { DoubleBuffered=true; SetStyle(ControlStyles.SupportsTransparentBackColor,true); BackColor=Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Theme.Fill(e.Graphics,new RectangleF(0,0,Width,Height),Height/2f,Color.FromArgb(228,234,241));
            if (Value>0) using(var p=Theme.Round(new RectangleF(0,0,Math.Max(Height,Width*Math.Min(Value,100)/100f),Height),Height/2f)) using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(81,119,255),Theme.Blue,0f)) e.Graphics.FillPath(b,p);
        }
    }
    sealed class WorkflowStrip : Control
    {
        public int Step;
        public WorkflowStrip() { DoubleBuffered=true; SetStyle(ControlStyles.SupportsTransparentBackColor,true); BackColor=Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; float s=g.DpiX/96f;
            using(var p=Theme.Round(new RectangleF(1,1,Width-2,Height-2),Height/2f)) using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(230,240,250),Color.FromArgb(245,244,211),0f)) g.FillPath(b,p);
            string[] labels={"粘贴链接","解析内容","保存到本地"};
            for(int i=0;i<3;i++)
            {
                float x=i*Width/3f+14*s; bool active=i==Step;
                if(active) Theme.Fill(g,new RectangleF(i*Width/3f+5*s,5*s,Width/3f-10*s,Height-10*s),(Height-10*s)/2,Color.FromArgb(230,255,255,255));
                var circle=new RectangleF(x,(Height-20*s)/2,20*s,20*s);
                using(var b=new SolidBrush(i<=Step ? Theme.Blue : Color.FromArgb(222,228,230))) g.FillEllipse(b,circle);
                using(var f=new Font("Segoe UI",8,FontStyle.Bold)) TextRenderer.DrawText(g,(i+1).ToString(),f,Rectangle.Round(circle),i<=Step ? Color.White : Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g,labels[i],Font,new Rectangle((int)(x+27*s),0,(int)(Width/3f-48*s),Height),active?Theme.Ink:Theme.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            }
        }
    }
    sealed class LinkSculpture : Control
    {
        public LinkSculpture() { DoubleBuffered=true; SetStyle(ControlStyles.SupportsTransparentBackColor,true); BackColor=Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; var state=g.Save(); g.ScaleTransform(Width/300f,Height/150f);
            Theme.Glow(g,new RectangleF(15,65,275,90),Color.FromArgb(80,83,111,220));
            DrawLink(g,75,76,-32); DrawLink(g,157,79,-32);
            using(var p=new Pen(Color.FromArgb(155,255,255,255),2)) {g.DrawLine(p,32,32,32,42);g.DrawLine(p,27,37,37,37);g.DrawLine(p,263,85,263,93);g.DrawLine(p,259,89,267,89);}
            g.Restore(state);
        }
        static void DrawLink(Graphics g,float x,float y,float angle)
        {
            var saved=g.Save();g.TranslateTransform(x,y);g.RotateTransform(angle);
            using(var p=Theme.Round(new RectangleF(-43,-22,121,60),30))
            {
                using(var shadow=new Pen(Color.FromArgb(60,63,85,171),20)) {g.TranslateTransform(1,6);g.DrawPath(shadow,p);g.TranslateTransform(-1,-6);}
                using(var b=new LinearGradientBrush(new Rectangle(-60,-48,160,110),Color.FromArgb(178,202,255),Color.FromArgb(39,78,230),75f)) using(var pen=new Pen(b,20))g.DrawPath(pen,p);
                using(var pen=new Pen(Color.FromArgb(120,228,238,255),1.4f)){g.TranslateTransform(-1,-7);g.DrawPath(pen,p);}
            }
            g.Restore(saved);
        }
    }
    sealed class MediaGlyph : Control
    {
        public MediaGlyph(){ DoubleBuffered=true;SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var p=Theme.Round(new RectangleF(0,0,Width-1,Height-1),20*g.DpiX/96f)) using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(222,233,255),Color.FromArgb(237,243,253),45f)) g.FillPath(b,p);
            Theme.Icon(g,"play",new RectangleF(Width*.27f,Height*.27f,Width*.46f,Height*.46f),Theme.Blue);
        }
    }
}
