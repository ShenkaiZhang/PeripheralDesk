
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class PeripheralDeskIcon {
    static GraphicsPath Rounded(float x, float y, float w, float h, float r) {
        var p=new GraphicsPath();float d=2*r;
        p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);
        p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);
        p.CloseFigure();return p;
    }
    static Bitmap Render(int size) {
        var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap)) {
            g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
            g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            g.ScaleTransform(size/32f,size/32f);
            var frame=new RectangleF(1.0f,1.0f,30f,30f);
            using(var p=Rounded(frame.X,frame.Y,frame.Width,frame.Height,8))
            using(var fill=new LinearGradientBrush(frame,Color.FromArgb(76,73,185),Color.FromArgb(30,42,101),65f))
                g.FillPath(fill,p);
            using(var p=Rounded(1.6f,1.6f,28.8f,28.8f,7.5f))
            using(var edge=new Pen(Color.FromArgb(204,146,151,242),1f))g.DrawPath(edge,p);
            using(var lane=new Pen(Color.FromArgb(216,219,237,255),2.45f)){
                lane.StartCap=lane.EndCap=LineCap.Round;
                g.DrawLine(lane,7.7f,11,24.3f,11);
                g.DrawLine(lane,7.7f,21,24.3f,21);
            }
            using(var accent=new SolidBrush(Color.FromArgb(132,250,225))){
                g.FillEllipse(accent,10f,7.1f,7.8f,7.8f);
                g.FillEllipse(Brushes.White,17.3f,17.1f,7.8f,7.8f);
            }
        }
        return bitmap;
    }
    static byte[] Entry(int size) {
        using(var b=Render(size))
        using(var stream=new MemoryStream())
        using(var w=new BinaryWriter(stream)){
            int maskStride=((size+31)/32)*4;
            w.Write(40);w.Write(size);w.Write(size*2);w.Write((ushort)1);w.Write((ushort)32);
            w.Write(0);w.Write(size*size*4+maskStride*size);for(int i=0;i<4;i++)w.Write(0);
            for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){
                var c=b.GetPixel(x,y);w.Write(c.B);w.Write(c.G);w.Write(c.R);w.Write(c.A);
            }
            for(int y=size-1;y>=0;y--){
                var mask=new byte[maskStride];
                for(int x=0;x<size;x++)if(b.GetPixel(x,y).A==0)mask[x/8]|=(byte)(128>>(x%8));
                w.Write(mask);
            }
            w.Flush();return stream.ToArray();
        }
    }
    public static void Write(string path) {
        int[] sizes={16,20,24,32,40,48,64,128,256};
        var entries=Array.ConvertAll(sizes,Entry);
        using(var w=new BinaryWriter(File.Create(path))){
            w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){
                w.Write((byte)(sizes[i]%256));w.Write((byte)(sizes[i]%256));
                w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)32);
                w.Write(entries[i].Length);w.Write(offset);offset+=entries[i].Length;
            }
            foreach(var e in entries)w.Write(e);
        }
    }
}
'@
[PeripheralDeskIcon]::Write((Join-Path $PSScriptRoot 'PeripheralDesk.ico'))

