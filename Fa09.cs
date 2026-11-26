using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

internal static class Fa09 {
    internal static bool IsReceiver(ushort vid,ushort pid){return vid==0x3554&&pid==0xfa09;}
    internal static byte[] Request(byte command) {
        if(command!=5&&command!=0x4a) throw new ArgumentException("Only documented reads allowed");
        byte[] frame=new byte[20];frame[0]=0x13;frame[1]=command;frame[2]=1;frame[19]=(byte)(0x14+command);return frame;
    }
    internal static bool Reply(byte[] b,int read,byte command) {
        return read==20&&b.Length==20&&b[0]==0x13&&b[1]==command&&b[2]==1&&b[3]==0&&!b.SequenceEqual(Request(command));
    }
    static bool Wait(SafeFileHandle h,Task t,int ms) {
        try{if(t.Wait(ms))return true;}catch(AggregateException){return false;}
        NativeDevices.CancelIoEx(h,IntPtr.Zero);
        try {if(!t.Wait(150))throw new IOException("Timed out cancelling receiver read");}catch(AggregateException){}
        return false;
    }
    static byte[] Exchange(SafeFileHandle h,FileStream s,byte command) {
        var frame=Request(command);var b=new byte[20];
        if(!Wait(h,s.WriteAsync(frame,0,20),230))return null;
        var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<430) {
            var t=s.ReadAsync(b,0,20);
            if(!Wait(h,t,Math.Max(1,430-(int)watch.ElapsedMilliseconds))||t.Result==0)return null;
            if(Reply(b,t.Result,command))return b;
        }
        return null;
    }
    static string KeyboardName(byte[] reply) {
        // Only name a model when its reported identity matches a documented ID.
        switch(BitConverter.ToString(reply,5,6)) {
            case "03-00-00-00-01-15": return "MCHOSE G75 Pro";
            case "03-00-00-00-03-35": return "MCHOSE G75 V2";
            case "03-00-00-00-01-32": return "MCHOSE G98";
            case "03-00-00-00-02-1C": return "MCHOSE G87";
            default:return "FA09 2.4G 无线键盘";
        }
    }
    internal static Device Read(Device row) {
        ushort page;
        if(!IsReceiver(row.Vendor,row.Product)||!NativeDevices.Fa09Collection(row.Path,out page))return row;
        try {using(var h=NativeDevices.CreateFile(row.Path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
            if(h.IsInvalid)return row;
            using(var s=new FileStream(h,FileAccess.ReadWrite,1,true)) {
                var identity=Exchange(h,s,5);
                if(identity==null)return row;
                row.Name=KeyboardName(identity);
                var batt=Exchange(h,s,0x4a);
                if(batt==null||batt[5]==0||batt[5]>100){row.Note="已识别协议；本轮未取得可信电量";return row;}
                row.Battery=batt[5];row.State="已连接";row.Note="键盘响应电量查询；充电状态未确认";return row;
            }
        }} catch(Exception e){row.Note="未取得键盘读数："+e.Message;}
        return row;
    }
}