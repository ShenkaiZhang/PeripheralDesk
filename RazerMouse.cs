using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

// Original implementation using the published Razer packet layout; no vendor binaries.
internal sealed class RazerMouse : IDisposable {
    sealed class BatteryProfile {
        internal byte Txn;
        internal int Interface;
        internal bool Wireless, HasFeatureReport;
    }
    static BatteryProfile Find(ushort vid,ushort pid){
        if(vid!=0x1532)return null;
        switch(pid){
          case 0x00b6: case 0x00b7:return new BatteryProfile{Txn=0x1f,Interface=0,Wireless=pid==0x00b7,HasFeatureReport=true};
          case 0x00c0: case 0x00c1:return new BatteryProfile{Txn=0x1f,Interface=0,Wireless=pid==0x00c1,HasFeatureReport=true};
          case 0x007a: case 0x007b:return new BatteryProfile{Txn=0x3f,Interface=2,Wireless=pid==0x007b};
          default:return null;
        }
    }
    internal static bool Supports(ushort vid,ushort pid){return Find(vid,pid)!=null;}
    internal static bool IsWireless(ushort vid,ushort pid){var m=Find(vid,pid);return m!=null&&m.Wireless;}
    internal static bool AllowsCollection(ushort vid,ushort pid,int iface,ushort page,ushort usage,ushort input,ushort output) {
        var m=Find(vid,pid);
        return m!=null&&iface==m.Interface&&page==1&&usage==2&&input==91&&output==91;
    }
    internal static bool AllowsFeature(ushort vid,ushort pid,int iface,ushort page,ushort usage,ushort feature) {
        var m=Find(vid,pid);
        return m!=null&&m.HasFeatureReport&&iface==m.Interface&&page==1&&usage==2&&feature==91;
    }
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_SetFeature(SafeFileHandle h,byte[] report,uint length);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_GetFeature(SafeFileHandle h,[In,Out] byte[] report,uint length);
    internal static byte Xor(byte[] b){
        byte x=0;for(int i=3;i<=88;i++)x^=b[i];return x;
    }
    internal static byte[] Frame(byte txn,byte group,byte id,byte[] args){
        if(args==null||args.Length>80)throw new ArgumentException("Invalid Razer payload");
        var b=new byte[91];b[2]=txn;b[6]=(byte)args.Length;b[7]=group;b[8]=id;
        Array.Copy(args,0,b,9,args.Length);b[89]=Xor(b);return b;
    }
    internal static bool ValidReply(byte[] b,int len,byte txn,byte group,byte id,int min){
        return b!=null&&len==91&&b.Length>=91&&b[0]==0&&b[1]==2&&b[2]==txn&&b[3]==0&&b[4]==0&&b[5]==0&&
            b[6]>=min&&b[6]<=80&&b[7]==group&&b[8]==id&&b[89]==Xor(b);
    }
    internal static int Percent(byte value){return (value*100+127)/255;}
    readonly SafeFileHandle handle;
    readonly FileStream io;
    readonly BatteryProfile model;
    RazerMouse(Device d){
        model=Find(d.Vendor,d.Product);
        int mode=NativeDevices.RazerMode(d.Path,d.Vendor,d.Product);
        if(model==null||mode==0)throw new IOException("控制接口未通过核对");
        handle=NativeDevices.CreateFile(d.Path,mode==2?0u:0xc0000000u,3,IntPtr.Zero,3,mode==2?0u:0x40000000u,IntPtr.Zero);
        if(handle.IsInvalid){int err=System.Runtime.InteropServices.Marshal.GetLastWin32Error();handle.Dispose();throw new System.ComponentModel.Win32Exception(err);}
        if(mode==2)return;
        try {io=new FileStream(handle,FileAccess.ReadWrite,1,true);}catch{handle.Dispose();throw;}
    }
    public void Dispose(){try{if(io!=null)io.Dispose();}finally{handle.Dispose();}}
    bool Complete(Task t,int ms){
        try{if(t.Wait(ms))return true;}catch(AggregateException){return false;}
        NativeDevices.CancelIoEx(handle,IntPtr.Zero);
        try{if(!t.Wait(150))throw new IOException("雷蛇接口未完成取消，停止本次查询");}catch(AggregateException){}
        return false;
    }
    byte[] Exchange(byte group,byte cmd,int min,params byte[] args){
        if(group!=7||(cmd!=0x80&&cmd!=0x84))throw new ArgumentException("Only battery and charge status queries are allowed.");
        var w=Frame(model.Txn,group,cmd,args);var timer=Stopwatch.StartNew();
        if(io==null){
            if(!HidD_SetFeature(handle,w,91))return null;
            for(int i=0;i<3&&timer.ElapsedMilliseconds<380;i++){
                Thread.Sleep(25);
                var r=new byte[91];
                if(HidD_GetFeature(handle,r,91)&&ValidReply(r,91,model.Txn,group,cmd,min))return r;
            }
            return null;
        }
        if(!Complete(io.WriteAsync(w,0,w.Length),220))return null;
        while(timer.ElapsedMilliseconds<380){
            var b=new byte[91];var read=io.ReadAsync(b,0,b.Length);
            if(!Complete(read,Math.Max(1,380-(int)timer.ElapsedMilliseconds))||read.Result==0)return null;
            if(ValidReply(b,read.Result,model.Txn,group,cmd,min))return b;
        }
        return null;
    }
    internal static Device ReadDevice(Device row) {
        try {using(var t=new RazerMouse(row)){
            var battery=t.Exchange(7,0x80,2,0,0);
            if(battery!=null&&battery[9]==0)row.Battery=Percent(battery[10]);
            if(row.Battery.HasValue){
                var c=t.Exchange(7,0x84,2,0,0);
                row.Charging=c!=null&&c[9]==0&&c[10]==1;
            }
            if(row.Battery.HasValue){
                row.State="已连接";
                row.Note="已确认实时电量";
            }
            else row.Note="接口已打开，但没有收到可核对的电量回复";
        }}catch(IOException e){row.Note="雷蛇读取未完成："+e.Message;}
        catch(System.ComponentModel.Win32Exception e){row.Note="雷蛇控制接口无法打开（Windows 错误 "+e.NativeErrorCode+"）";}
        return row;
    }
}
