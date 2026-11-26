using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

internal sealed class Logitech : IDisposable {
    readonly SafeFileHandle handle;
    readonly FileStream stream;
    readonly FileStream shortStream;
    readonly SafeFileHandle shortHandle;
    string lastIo;
    int sequence=0;
    Logitech(Device receiver) {
        if(!Protocol.IsLogitechEndpoint(receiver.Vendor,receiver.Product)||!NativeDevices.CorrectCollection(receiver.Path))
            throw new IOException("接收器接口未通过匹配");
        handle=NativeDevices.CreateFile(receiver.Path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        if(handle.IsInvalid){handle.Dispose();throw new Win32Exception();}
        try {stream=new FileStream(handle,FileAccess.ReadWrite,1,true);} catch {handle.Dispose();throw;}
        if(receiver.ShortPath!=null&&NativeDevices.InterfaceKey(receiver.Path).Equals(NativeDevices.InterfaceKey(receiver.ShortPath),StringComparison.OrdinalIgnoreCase)
            &&NativeDevices.LogitechShort(receiver.ShortPath)){
            shortHandle=NativeDevices.CreateFile(receiver.ShortPath,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
            if(!shortHandle.IsInvalid)try{shortStream=new FileStream(shortHandle,FileAccess.ReadWrite,1,true);}catch(IOException){shortHandle.Dispose();}
        }
    }
    public void Dispose() {if(shortStream!=null)shortStream.Dispose();if(shortHandle!=null)shortHandle.Dispose();stream.Dispose();handle.Dispose();}
    byte[] Query(byte slot,byte feature,byte fn,params byte[] args) {
        if(shortStream!=null&&args.Length<=3){
            var answer=Exchange(slot,feature,fn,true,args);
            if(answer!=null)return answer;
            // Only use this fallback for feature discovery, never for settings writes.
            if(feature!=0||fn!=0||lastIo==null||!lastIo.StartsWith("未收到"))return null;
        }
        return Exchange(slot,feature,fn,false,args);
    }
    byte[] Exchange(byte slot,byte feature,byte fn,bool shortRequest,params byte[] args) {
        byte op=(byte)(fn*16+8+(sequence++%7));
        byte[] req=shortRequest?Protocol.ShortFrame(slot,feature,op,args):Protocol.Frame(slot,feature,op,args);
        lastIo=null;
        var writer=shortRequest?shortStream:stream;
        if(writer==null)return null;
        Task write=writer.WriteAsync(req,0,req.Length);
        if(!Wait(shortRequest?shortHandle:handle,write,220)){lastIo="发送失败";return null;}
        byte[] result=ReadReply(stream,handle,20,slot,feature,op,shortStream==null?280:150);
        if(result!=null||lastIo!=null)return result;
        if(shortStream!=null) {
            result=ReadReply(shortStream,shortHandle,7,slot,feature,op,130);
            if(result!=null||lastIo!=null)return result;
        }
        lastIo="未收到回复";return null;
    }
    byte[] ReadReply(FileStream reader,SafeFileHandle h,int size,byte slot,byte feature,byte op,int ms){
        var clock=Stopwatch.StartNew();var b=new byte[size];
        while(clock.ElapsedMilliseconds<ms) {
            Task<int> read=reader.ReadAsync(b,0,b.Length);
            if(!Wait(h,read,Math.Max(1,ms-(int)clock.ElapsedMilliseconds)))return null;
            int len=read.Result;if(len<=0)break;
            int match=Protocol.Response(b,len,slot,feature,op);
            if(match<0){lastIo="设备拒绝，代码 "+b[5].ToString("X2");return null;}
            if(match==1)return b.Skip(4).Take(len-4).ToArray();
        }
        return null;
    }
    byte[] ReadReceiverInfo(byte selector){
        if(shortStream==null)return null;
        var request=Protocol.ShortFrame(255,0x83,0xb5,new[]{selector});
        if(!Wait(shortHandle,shortStream.WriteAsync(request,0,request.Length),220))return null;
        var timer=Stopwatch.StartNew();
        var b=new byte[20];
        while(timer.ElapsedMilliseconds<350){
            var read=stream.ReadAsync(b,0,b.Length);
            if(!Wait(handle,read,Math.Max(1,350-(int)timer.ElapsedMilliseconds))||read.Result==0)return null;
            if(Protocol.ReceiverInfoReply(b,read.Result,selector))return b;
        }
        return null;
    }
    string PairedSummary(){
        var results=new List<string>();
        for(byte i=0;i<2;i++){
            var model=Protocol.PairedModel(ReadReceiverInfo((byte)(0x20+i)));
            if(model==null)continue;
            string name=Protocol.PairedName(ReadReceiverInfo((byte)(0x40+i)));
            results.Add((string.IsNullOrEmpty(name)?"鼠标":name)+" / WPID "+model);
        }
        return string.Join("、",results);
    }
    bool Ping(byte slot){
        if(shortStream==null)return false;
        const byte marker=0x63;
        var p=Exchange(slot,0,1,true,0,0,marker);
        return p!=null&&p.Length>=3&&p[0]>=2&&p[2]==marker;
    }
    bool Wait(SafeFileHandle h,Task pending,int limit) {
        try { if(pending.Wait(limit))return true; }
        catch(AggregateException) { return false; }
        NativeDevices.CancelIoEx(h,IntPtr.Zero);
        try { if(!pending.Wait(150)) throw new IOException("接收器未完成取消，已停止本次查询"); }
        catch(AggregateException) {}
        return false;
    }
    byte Feature(byte slot,int feature) {
        var p=Query(slot,0,0,(byte)(feature>>8),(byte)feature);
        return p!=null&&p.Length>0?p[0]:(byte)0;
    }
    string DeviceName(byte slot,byte f) {
        var length=Query(slot,f,0); if(length==null||length.Length==0||length[0]==0||length[0]>64)return "";
        var bytes=new List<byte>(); int total=length[0];
        while(bytes.Count<total) {
            var p=Query(slot,f,1,(byte)bytes.Count);
            if(p==null||p.Length==0)return "";
            bytes.AddRange(p.Take(total-bytes.Count));
        }
        return Encoding.UTF8.GetString(bytes.ToArray()).Trim('\0',' ');
    }
    internal static List<Device> Read(Device receiver) {
        var output=new List<Device>();
        try {using(var transport=new Logitech(receiver)) {
            var wired=Protocol.IsLogitechWired(receiver.Vendor,receiver.Product);
            var slots=wired?new byte[]{255}:receiver.Product==0xc54d?new byte[]{1,2,255}:new byte[]{1,2,3,4,5,6};
            bool ping=false;
            foreach(byte slot in slots) {
                if(slot==255&&output.Count>0)break;
                if(receiver.Product==0xc54d&&!ping)ping=transport.Ping(slot);
                byte f=transport.Feature(slot,5);
                if(f==0)continue;
                string name=transport.DeviceName(slot,f);
                if(string.IsNullOrWhiteSpace(name))continue;
                var d=new Device { Id=receiver.Id+":"+slot,Vendor=receiver.Vendor,Product=receiver.Product,
                    Name=name,KindHint=wired||receiver.Product==0xc54d?"mouse":null,Link=wired?"USB · Logitech":"2.4G · Logitech",State="已连接" };
                byte battery=transport.Feature(slot,0x1004);
                bool unified=battery!=0;
                if(!unified)battery=transport.Feature(slot,0x1000);
                if(battery!=0) {
                    var p=transport.Query(slot,battery,unified?(byte)1:(byte)0);
                    d.Battery=Protocol.Battery(p,unified);
                    if(d.Battery.HasValue) {
                        byte status=unified?p[2]:p[2<p.Length?2:1];
                        d.Charging=unified?(status==1||status==2):(status==1||status==2||status==3);
                    }
                }
                d.Note=d.Battery.HasValue?"已收到实时电量回复":"已连接；未取得可读的百分比";
                output.Add(d);
            }
            if(output.Count==0){
                bool receiverAnswered=receiver.Product==0xc54d&&transport.ReadReceiverInfo(3)!=null;
                string paired=receiverAnswered?transport.PairedSummary():"";
                receiver.Note="控制接口已打开；"+(receiverAnswered?"接收器自身已回复；":"")+
                    (transport.shortStream!=null?(ping?"鼠标短报文有回复；":"没有收到鼠标短报文回复；"):"未找到可用的短报文接口；")+
                    "没有取得鼠标实时回复"+(transport.lastIo==null?"":"（"+transport.lastIo+"）")+
                    (paired.Length==0?"":"；接收器配对记录（不代表在线）："+paired);
            }
        }} catch(Exception e) {
            receiver.Note="接收器无法读取："+e.Message;
        }
        if(output.Count==0) output.Add(receiver);
        return output;
    }
}
