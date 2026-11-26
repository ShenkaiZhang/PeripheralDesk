using System;

internal static class Protocol {
    internal static bool IsLogitechReceiver(ushort vendor,ushort pid) {
        if(vendor!=0x046d) return false;
        switch(pid) {
        case 0xc52b: case 0xc532: case 0xc52f: case 0xc548:
        case 0xc539: case 0xc53a: case 0xc53d: case 0xc53f:
        case 0xc541: case 0xc545: case 0xc547: case 0xc54d: return true;
        default:return false;
        }
    }
    internal static bool IsLogitechWired(ushort vid,ushort pid) {
        return vid==0x046d&&(pid==0xc09b||pid==0xc094||pid==0xc08b);
    }
    internal static bool IsLogitechEndpoint(ushort vid,ushort pid) {
        return IsLogitechReceiver(vid,pid)||IsLogitechWired(vid,pid);
    }
    internal static byte[] ShortFrame(byte slot,byte feature,byte operation,byte[] data) {
        if(data.Length>3)throw new ArgumentException("Short HID++ payload");
        var b=new byte[7];b[0]=0x10;b[1]=slot;b[2]=feature;b[3]=operation;
        Array.Copy(data,0,b,4,data.Length);return b;
    }
    internal static bool ReceiverInfoReply(byte[] b,int length,byte selector=3){
        return b!=null&&b.Length>=length&&length==20&&b[0]==0x11&&b[1]==255&&b[2]==0x83&&b[3]==0xb5&&b[4]==selector;
    }
    internal static string PairedModel(byte[] b){
        if(b==null||b.Length!=20||(b[4]!=0x20&&b[4]!=0x21)||(b[11]&15)!=2)return null;
        int wpid=BE(b,7);
        return wpid==0||wpid==65535?null:wpid.ToString("X4");
    }
    internal static string PairedName(byte[] b){
        if(b==null||b.Length!=20||(b[4]!=0x40&&b[4]!=0x41)||b[5]==0||b[5]>14)return "";
        for(int i=6;i<6+b[5];i++)if(b[i]<32||b[i]>126)return "";
        return System.Text.Encoding.ASCII.GetString(b,6,b[5]).Trim();
    }
    internal static int? WindowsBattery(uint kind,byte[] bytes) {
        uint v;
        if(kind==3&&bytes.Length==1) v=bytes[0];
        else if(kind==7&&bytes.Length==4) v=BitConverter.ToUInt32(bytes,0);
        else return null;
        return v<=100?(int?)v:null;
    }
    internal static byte[] Frame(byte slot,byte feature,byte operation,byte[] data) {
        if(data.Length>16) throw new ArgumentException("Report payload");
        var w=new byte[20];w[0]=0x11;w[1]=slot;w[2]=feature;w[3]=operation;
        Array.Copy(data,0,w,4,data.Length);return w;
    }
    internal static int Response(byte[] w,int n,byte slot,byte feature,byte operation) {
        if(n<7||(w[0]!=0x10&&w[0]!=0x11)||w[1]!=slot)return 0;
        if(w[2]==feature&&w[3]==operation)return 1;
        if((w[2]==0xff||w[2]==0x8f)&&w[3]==feature&&w[4]==operation)return -1;
        return 0;
    }
    internal static int BE(byte[] b,int i) { return b[i]*256+b[i+1]; }
    internal static int? Battery(byte[] data,bool unified) {
        if(data==null||data.Length<(unified?3:2)||data[0]>100)return null;
        // Unified HID++ reports a zero state-of-charge on some devices with only level flags.
        if(unified && data[0]==0 && data[1]>1)return null;
        return data[0];
    }
}
