
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

internal static class Design {
    internal static readonly Color Canvas=Color.FromArgb(243,247,252),
        Card=Color.White, Border=Color.FromArgb(224,232,243), Ink=Color.FromArgb(24,37,58),
        Muted=Color.FromArgb(83,103,129), Accent=Color.FromArgb(38,104,219),
        BlueWash=Color.FromArgb(232,240,255), Green=Color.FromArgb(14,133,103),
        GrayRing=Color.FromArgb(222,231,242);
    internal static GraphicsPath Round(RectangleF r,float radius) {
        var p=new GraphicsPath();float d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    internal static void Fill(Graphics g,Color c,RectangleF r,float radius) {
        using(var p=Round(r,radius)) using(var b=new SolidBrush(c))g.FillPath(b,p);
    }
    internal static void Line(Graphics g,Color c,RectangleF r,float radius) {
        using(var p=Round(r,radius))using(var pen=new Pen(c,1))g.DrawPath(pen,p);
    }
    internal static void Words(Graphics g,string s,RectangleF r,Font f,Color c,StringAlignment a=StringAlignment.Near) {
        using(var b=new SolidBrush(c))using(var fmt=new StringFormat(StringFormatFlags.NoWrap)){
            fmt.Alignment=a;fmt.LineAlignment=StringAlignment.Center;fmt.Trimming=StringTrimming.EllipsisCharacter;
            g.DrawString(s,f,b,r,fmt);
        }
    }
    internal static Font Font(float point,bool bold=false) {return new Font("Microsoft YaHei UI",point,bold?FontStyle.Bold:FontStyle.Regular);}
    internal static Button Button(string name,int width,int height,bool primary=false) {
        var b=new Button{Text=name,Size=new Size(width,height),Font=Font(8.5f,primary),ForeColor=primary?Color.White:Accent,
            BackColor=primary?Accent:BlueWash,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand,UseVisualStyleBackColor=false};
        b.FlatAppearance.BorderSize=0;
        b.Resize+=(s,e)=>RoundControl(b,8);
        RoundControl(b,8);
        return b;
    }
    internal static void RoundControl(Control c,int radius) {
        if(c.Width<2||c.Height<2)return;
        using(var shape=Round(new RectangleF(0,0,c.Width,c.Height),radius)){
            var old=c.Region;c.Region=new Region(shape);if(old!=null)old.Dispose();
        }
    }
    internal static string Kind(Device d) {
        if(!string.IsNullOrEmpty(d.KindHint))return d.KindHint;
        string n=(d.Name??"").ToLowerInvariant();
        if(n.Contains("mouse")||n.Contains("鼠标")||n.Contains("keris")||n.Contains("gpw"))return "mouse";
        if(n.Contains("keyboard")||n.Contains("键盘")||System.Text.RegularExpressions.Regex.IsMatch(n,@"^(k98|mchose g87)\b"))return "keyboard";
        if(n.Contains("headphone")||n.Contains("headset")||n.Contains("耳机")||n.Contains("buds")||n.Contains("airpods")||n.Contains("freeclip")||n.Contains("sonoflow")||n.Contains("speaker")||n.Contains("扬声器"))return "headphones";
        if(n.Contains("microphone")||n.Contains("麦克风"))return "mic";
        if(n.Contains("gamepad")||n.Contains("手柄")||n.Contains("xbox")||n.Contains("dualsense"))return "gamepad";
        return d.Link=="蓝牙"?"bluetooth":"generic";
    }
    internal static void Glyph(Graphics g,string key,float centerX,float centerY,Color color) {
        var state=g.Save();g.TranslateTransform(centerX-10,centerY-10);
        try {
            using(var p=new Pen(color,1.6f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}) {
                if(key=="mouse"){
                    using(var shape=Round(new RectangleF(4,2,12,16),5))g.DrawPath(p,shape);
                    g.DrawLine(p,10,5,10,8);
                }else if(key=="keyboard"){
                    using(var shape=Round(new RectangleF(1,4,18,12),2.5f))g.DrawPath(p,shape);
                    for(int y=8;y<=11;y+=3)for(int x=5;x<=15;x+=3)g.DrawLine(p,x,y,x+0.5f,y);
                    g.DrawLine(p,7,13.5f,13,13.5f);
                }else if(key=="headphones"){
                    g.DrawArc(p,2.5f,2.5f,15,14,180,180);g.DrawLine(p,2.5f,10,2.5f,14);g.DrawLine(p,17.5f,10,17.5f,14);
                    using(var a=Round(new RectangleF(3,10,4,7),1.6f))g.DrawPath(p,a);
                    using(var a=Round(new RectangleF(13,10,4,7),1.6f))g.DrawPath(p,a);
                }else if(key=="bluetooth"){
                    g.DrawLines(p,new[]{new PointF(10,2),new PointF(10,18),new PointF(15,14),new PointF(5,6)});
                    g.DrawLines(p,new[]{new PointF(10,2),new PointF(15,6),new PointF(5,14)});
                }else if(key=="gamepad"){
                    using(var a=Round(new RectangleF(2,5,16,10),3))g.DrawPath(p,a);
                    g.DrawLine(p,5,10,9,10);g.DrawLine(p,7,8,7,12);g.DrawEllipse(p,13,8,1,1);g.DrawEllipse(p,15,11,1,1);
                }else if(key=="mic"){
                    using(var a=Round(new RectangleF(7,2,6,10),3))g.DrawPath(p,a);
                    g.DrawArc(p,4.5f,6,11,9,0,180);g.DrawLine(p,10,15,10,18);g.DrawLine(p,7,18,13,18);
                }else{
                    using(var a=Round(new RectangleF(3,4,13,12),2.5f))g.DrawPath(p,a);
                    g.DrawLine(p,17.5f,8,17.5f,12);g.DrawLine(p,7,10,12,10);
                }
            }
        }finally{g.Restore(state);}
    }
}
internal sealed class DeviceTile : Control {
    readonly Device d;
    readonly Font title=Design.Font(10.25f,true), detail=Design.Font(8.2f), value=Design.Font(16,true), pct=Design.Font(8.2f,true);
    internal DeviceTile(Device model) {
        d=model;AccessibleName=model.Name;AccessibleDescription="已连接，"+(model.Battery.HasValue?model.Battery+"%":"未提供电量");
        AccessibleRole=AccessibleRole.StaticText;TabStop=false;Cursor=Cursors.Default; SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        Height=62;BackColor=Design.Canvas;
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
        var box=new RectangleF(0.5f,0.5f,Width-1f,Height-1f);
        Design.Fill(g,Design.Card,box,13);
        Design.Line(g,Design.Border,box,13);
        var ring=new RectangleF(12,12,38,38);
        Color power=!d.Battery.HasValue?Design.Muted:d.Charging?Design.Accent:d.Battery<=20?Color.FromArgb(186,65,51):
            d.Battery<=45?Color.FromArgb(172,110,13):Design.Green;
        using(var track=new Pen(Design.GrayRing,3.1f))g.DrawEllipse(track,ring);
        if(d.Battery.HasValue&&d.Battery.Value>0)using(var progress=new Pen(power,3.1f){StartCap=LineCap.Round,EndCap=LineCap.Round})
            g.DrawArc(progress,ring,-90,Math.Min(359.7f,d.Battery.Value*3.6f));
        Design.Glyph(g,Design.Kind(d),31,31,Design.Muted);
        int right=d.Battery.HasValue?70:60;
        Design.Words(g,d.Name,new RectangleF(62,10,Math.Max(15,Width-62-right),24),title,Design.Ink);
        using(var dot=new SolidBrush(Design.Green))g.FillEllipse(dot,64,43,6,6);
        var sub=(d.Link.StartsWith("2.4G")?"2.4G":d.Link)+" · 已连接";
        Design.Words(g,sub,new RectangleF(76,35,Math.Max(15,Width-76-right),20),detail,Design.Muted);
        if(d.Battery.HasValue) {
            Design.Words(g,d.Battery.Value.ToString(),new RectangleF(Width-65,10,42,41),value,Design.Ink,StringAlignment.Far);
            Design.Words(g,"%",new RectangleF(Width-21,14,15,37),pct,Design.Muted);
        }else Design.Words(g,d.BatteryNotApplicable?"—":"无电量",new RectangleF(Width-60,12,49,39),detail,Design.Muted,StringAlignment.Far);
    }
    protected override void Dispose(bool disposing){if(disposing){title.Dispose();detail.Dispose();value.Dispose();pct.Dispose();}base.Dispose(disposing);}
}
internal sealed class AudioSlider : Control {
    int level=0;internal event EventHandler Changed;
    internal int Value {get{return level;}set{int n=Math.Min(100,Math.Max(0,value));if(n==level)return;level=n;Invalidate();if(Changed!=null)Changed(this,EventArgs.Empty);}}
    internal AudioSlider(){TabStop=true;Height=26;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.Slider;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int y=Height/2,x=8+(Width-16)*level/100;
        Design.Fill(e.Graphics,Design.GrayRing,new RectangleF(7,y-2,Math.Max(1,Width-14),4),2);
        if(x>7&&Enabled)Design.Fill(e.Graphics,Design.Accent,new RectangleF(7,y-2,x-7,4),2);
        using(var b=new SolidBrush(Design.Card))e.Graphics.FillEllipse(b,x-6,y-6,12,12);
        using(var p=new Pen(Enabled?Design.Accent:Design.GrayRing,Focused?2.4f:1.5f))e.Graphics.DrawEllipse(p,x-6,y-6,12,12);
    }
    void SetFromX(int x){if(Enabled)Value=(int)Math.Round((x-8)*100.0/Math.Max(1,Width-16));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left&&Enabled){Focus();Capture=true;SetFromX(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture)SetFromX(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(Capture){SetFromX(e.X);Capture=false;}}
    protected override bool IsInputKey(Keys k){return k==Keys.Left||k==Keys.Right||k==Keys.Home||k==Keys.End||base.IsInputKey(k);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);switch(e.KeyCode){case Keys.Left:Value-=2;break;case Keys.Right:Value+=2;break;
      case Keys.Home:Value=0;break;case Keys.End:Value=100;break;default:return;}e.Handled=true;}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
}
