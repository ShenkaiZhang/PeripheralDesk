using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

internal static class CompxMouse {
    // This allowlist describes where a battery query is permitted, not product names.
    internal static string TransportKind(ushort vid,ushort pid){
        if(vid==0x3710){
            if(pid==0x3414||pid==0x3527)return "usb";
            return pid==0x5406?"receiver":null;
        }
        if(vid==0x3554){
            switch(pid){
                case 0xf58f:case 0xfb43:return "usb";
                case 0xf58a:case 0xf505:case 0xf508:case 0xf510:case 0xfb44:return "receiver";
                case 0xf58c:case 0xf506:case 0xf507:case 0xf50f:return "unknown";
            }
        }
        if(vid==0x373b&&(pid==0x1040||pid==0x1085))return "receiver";
        if(vid==0x3770)return pid==0x0100?"usb":pid==0x0200?"receiver":null;
        return null;
    }
    internal static bool IsProfile(ushort vid,ushort pid){return TransportKind(vid,pid)!=null;}
    internal static bool NeedsInterfaceOne(ushort vid,ushort pid){return vid==0x3710&&IsProfile(vid,pid);}
    internal static byte Sum(byte[] data){int result=0;foreach(var b in data)result+=b;return (byte)(result&255);}
    internal static byte[] Request(){var b=new byte[17];b[0]=8;b[1]=4;b[16]=0x49;return b;}
    internal static int? Parse(byte[] b,int length){
        if(b==null||length!=17||b.Length!=17||b[0]!=8||b[1]!=4||b[2]!=0||b[6]>100||b[7]>1||Sum(b)!=0x55)return null;
        int mv=Protocol.BE(b,8);
        return mv>=2500&&mv<=5000?(int?)b[6]:null;
    }
    static bool Wait(SafeFileHandle h,Task task,int ms){
        try{if(task.Wait(ms))return true;}catch(AggregateException){return false;}
        NativeDevices.CancelIoEx(h,IntPtr.Zero);
        try{if(!task.Wait(150))throw new IOException("读取取消超时");}catch(AggregateException){}
        return false;
    }
    sealed class Transport : IDisposable {
        readonly SafeFileHandle h;
        readonly FileStream io;
        internal Transport(Device d) {
            if(!IsProfile(d.Vendor,d.Product)||!NativeDevices.CompxControl(d.Path,d.Vendor,d.Product))
                throw new IOException("控制接口未通过核对");
            h=NativeDevices.CreateFile(d.Path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
            if(h.IsInvalid){int e=Marshal.GetLastWin32Error();h.Dispose();throw new IOException("无法打开控制接口（Windows 错误 "+e+"）");}
            try{io=new FileStream(h,FileAccess.ReadWrite,1,true);}catch{h.Dispose();throw;}
        }
        public void Dispose(){try{io.Dispose();}finally{h.Dispose();}}
        internal byte[] QueryBattery() {
            var request=Request();
            if(!Wait(h,io.WriteAsync(request,0,17),220))return null;
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<450){
                var b=new byte[17];var read=io.ReadAsync(b,0,17);
                if(!Wait(h,read,Math.Max(1,450-(int)clock.ElapsedMilliseconds))||read.Result==0)return null;
                if(Parse(b,read.Result).HasValue)return b;
            }
            return null;
        }
    }
    internal static Device Read(Device d){
        try{using(var t=new Transport(d)){
            var b=t.QueryBattery();
            var charge=Parse(b,b==null?0:b.Length);
            if(charge.HasValue){d.Battery=charge;d.Charging=b[7]==1;d.State="已连接";}
            d.Note=charge.HasValue?"已验证实时电量":"未收到有效电量回复";
        }}catch(IOException e){d.Note="读取未完成："+e.Message;}
        return d;
    }
}
