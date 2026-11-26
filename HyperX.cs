using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
internal static class HyperX {
    internal static bool IsReceiver(ushort vid,ushort pid) {return vid==0x03f0&&(pid==0x05b7||pid==0x0c9d);}
    internal static int ParseLevel(byte[] frame,int len) {
        return len==62&&frame[0]==0x66&&(frame[1]==0x89||frame[1]==0x0d)
            &&(frame[2]!=0||frame[3]!=0)&&frame[4]<=100?frame[4]:-1;
    }
    static bool Complete(SafeFileHandle h,Task task,int limit) {
        try {if(task.Wait(limit))return true;}catch(AggregateException){return false;}
        NativeDevices.CancelIoEx(h,IntPtr.Zero);
        try {task.Wait(150);}catch(AggregateException){}
        return false;
    }
    internal static Device Read(Device row) {
        if(!IsReceiver(row.Vendor,row.Product)||!NativeDevices.MatchesCollection(row.Path,0xff13,1,62,62))return row;
        try { using(var h=NativeDevices.CreateFile(row.Path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
            if(h.IsInvalid)return row;
            using(var io=new FileStream(h,FileAccess.ReadWrite,1,true)) {
                byte[] request=new byte[62], reply=new byte[62];request[0]=0x66;request[1]=0x89;
                if(!Complete(h,io.WriteAsync(request,0,62),240))return row;
                var timer=Stopwatch.StartNew();
                while(timer.ElapsedMilliseconds<450) {
                    var read=io.ReadAsync(reply,0,62);
                    if(!Complete(h,read,Math.Max(1,450-(int)timer.ElapsedMilliseconds))||read.Result==0)break;
                    int level=ParseLevel(reply,read.Result);if(level<0)continue;
                    row.Battery=level;row.State="已连接";row.Note="已收到耳机电量回复";return row;
                }
            }
        }}catch(Exception e){row.Note="未取得耳机读数："+e.Message;}
        return row;
    }
}
