using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

internal sealed class Device {
    public string Id, Name, Link, State, Note, Path, ShortPath, InterfaceKey;
    public int? Battery; public string KindHint; public bool BatteryNotApplicable;
    public bool Charging;
    public ushort Vendor, Product;
    public override string ToString() { return Name; }
}
internal static class NativeDevices {
    const uint Present=2, AllClasses=4, Interfaces=16;
    static readonly IntPtr Invalid=new IntPtr(-1);
    [StructLayout(LayoutKind.Sequential)] internal struct Info {
        public uint Size; public Guid Class; public uint Instance; public UIntPtr Reserved;
        public static Info New() { return new Info { Size=(uint)Marshal.SizeOf(typeof(Info)) }; }
    }
    [StructLayout(LayoutKind.Sequential)] struct Iface {
        public uint Size; public Guid Class; public uint Flags; public UIntPtr Reserved;
        public static Iface New() { return new Iface { Size=(uint)Marshal.SizeOf(typeof(Iface)) }; }
    }
    [StructLayout(LayoutKind.Sequential)] struct Key {
        public Guid Fmt; public uint Pid;
        public Key(string guid,uint pid) { Fmt=new Guid(guid); Pid=pid; }
    }
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort Usage, Page, Input, Output, Feature;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
        public ushort LinkNodes, InButtons, InValues, InIndices, OutButtons, OutValues, OutIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [DllImport("setupapi.dll", EntryPoint="SetupDiGetClassDevsW", SetLastError=true)] static extern IntPtr ClassDevs(IntPtr guid, IntPtr en, IntPtr window, uint flags);
    [DllImport("setupapi.dll", EntryPoint="SetupDiGetClassDevsW", SetLastError=true)] static extern IntPtr ClassDevs(ref Guid guid, IntPtr en, IntPtr window, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInfo(IntPtr h,uint i,ref Info d);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInstanceId(IntPtr h,ref Info d,StringBuilder id,uint cap,out uint need);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceProperty(IntPtr h,ref Info d,ref Key key,out uint type,byte[] buffer,uint capacity,out uint needed,uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr h,IntPtr dev,ref Guid guid,uint i,ref Iface iface);
    [DllImport("setupapi.dll", EntryPoint="SetupDiGetDeviceInterfaceDetailW", SetLastError=true)] static extern bool Detail(IntPtr h,ref Iface iface,IntPtr detail,uint len,out uint need,ref Info d);
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle h,out IntPtr data);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,out Caps caps);
        [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_GetProductString(SafeFileHandle handle,byte[] data,uint len);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type,IntPtr caps,ref ushort len,IntPtr data);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    internal static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr sa,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool CancelIoEx(SafeFileHandle handle,IntPtr overlapped);

    static byte[] Prop(IntPtr h,ref Info d,Key k,out uint kind) {
        byte[] b=new byte[1024]; uint n;
        if(!SetupDiGetDeviceProperty(h,ref d,ref k,out kind,b,(uint)b.Length,out n,0) || n>b.Length) return new byte[0];
        Array.Resize(ref b,(int)n); return b;
    }
    static string PropString(IntPtr h,ref Info d,Key k) { uint t; var b=Prop(h,ref d,k,out t); return t==18?Encoding.Unicode.GetString(b).TrimEnd('\0'):""; }
    static string Instance(IntPtr h,ref Info d) { uint n; var text=new StringBuilder(512); return SetupDiGetDeviceInstanceId(h,ref d,text,(uint)text.Capacity,out n)?text.ToString():""; }
    static readonly Key BatteryKey=new Key("104EA319-6EE2-4701-BD47-8DDBF425BBE5",2);
    static readonly Key ConnectedKey=new Key("78C34FC8-104A-4ACA-9EA4-524D52996E57",55);
    static readonly Key HardwareIds=new Key("A45C254E-DF1C-4EFD-8020-67D146A850E0",3);
    static readonly Key Friendly=new Key("A45C254E-DF1C-4EFD-8020-67D146A850E0",14);
    static readonly Key Description=new Key("A45C254E-DF1C-4EFD-8020-67D146A850E0",2);
    static readonly Key Reported=new Key("540B947E-8B40-45BC-A8A2-6A0B894CBDA2",4);
    internal static List<Device> Bluetooth() {
        var live=BluetoothStatus.Read();
        List<Audio.Endpoint> outputs=null;try{outputs=Audio.Outputs();}catch(Exception){}
        var found=new Dictionary<string,Device>(StringComparer.OrdinalIgnoreCase);
        var h=ClassDevs(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,AllClasses);
        if(h==Invalid) throw new System.ComponentModel.Win32Exception();
        try { for(uint i=0;;i++) {
            var d=Info.New(); if(!SetupDiEnumDeviceInfo(h,i,ref d)) break;
            string source=Instance(h,ref d);
            var m=System.Text.RegularExpressions.Regex.Match(source,@"^(?:BTHENUM|BTHLE)\\DEV_([0-9A-F]{12})\\",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if(!m.Success) continue;
            string id="bt:"+m.Groups[1].Value; uint type;
            var c=Prop(h,ref d,ConnectedKey,out type);
            bool? connected=type==17&&c.Length==1?(bool?)(c[0]!=0):null;
            BluetoothStatus.Entry endpoint; if(live.TryGetValue(m.Groups[1].Value,out endpoint))connected=endpoint.Connected;
            var b=Prop(h,ref d,BatteryKey,out type);
            int? level=connected==true ? Protocol.WindowsBattery(type,b) : null;
            string name=PropString(h,ref d,Friendly);
            if(name.Length==0) name=PropString(h,ref d,Reported);
            if(name.Length==0) name=PropString(h,ref d,Description);
            if(endpoint!=null&&!string.IsNullOrWhiteSpace(endpoint.Name))name=endpoint.Name;
            if(name.Length==0) name="蓝牙外设";
            var entry=new Device { Id=id,Name=name,Link="蓝牙", State=connected==true?"已连接":connected==false?"未连接":"未取得在线状态", Battery=level,
                Note=connected==true?(level.HasValue?"Windows 提供电量":"已连接，Windows 未提供电量"):"未将历史电量当作当前电量" };
            Device old;
            if(!found.TryGetValue(id,out old) || Rank(entry)>Rank(old)) found[id]=entry;
        }} finally { SetupDiDestroyDeviceInfoList(h); }
        foreach(var pair in live) {
            var id="bt:"+pair.Key;
            if(found.ContainsKey(id))continue;
            var e=pair.Value;
            found.Add(id,new Device{Id=id,Name=string.IsNullOrWhiteSpace(e.Name)?"蓝牙外设":e.Name,Link="蓝牙",
                State=e.Connected?"已连接":"未连接",Note=e.Connected?"Windows 已确认连接，未提供电量":"未将历史电量当作当前电量"});
        }
        foreach(var row in found.Values) {
            if(row.State!="已连接")continue;
            var audioState=Audio.ConfirmedPlayback(row.Name,outputs);
            if(audioState==false){row.State="未连接";row.Battery=null;row.Note="蓝牙记录存在，Windows 未报告耳机音频已接通";}
            else if(audioState==null&&Audio.IsFreeClip2(row.Name)){row.State="未确认音频连接";row.Battery=null;row.Note="耳机播放状态尚未确认";}
        }
        return found.Values.ToList();
    }
    static int Rank(Device d) { return d.Battery.HasValue?3:d.State=="已连接"?2:d.State=="未连接"?1:0; }
    internal static List<Device> Receivers() {
        var rows=new List<Device>();
        var shorts=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        Guid guid; HidD_GetHidGuid(out guid);
        var h=ClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,Present|Interfaces);
        if(h==Invalid) throw new System.ComponentModel.Win32Exception();
        try { for(uint i=0;;i++) {
            var iface=Iface.New(); if(!SetupDiEnumDeviceInterfaces(h,IntPtr.Zero,ref guid,i,ref iface)) break;
            var d=Info.New(); uint size; Detail(h,ref iface,IntPtr.Zero,0,out size,ref d);
            if(size<8||size>65536) continue;
            var p=Marshal.AllocHGlobal((int)size);
            try {
                Marshal.WriteInt32(p,8); d=Info.New();
                if(!Detail(h,ref iface,p,size,out size,ref d)) continue;
                string path=Marshal.PtrToStringUni(IntPtr.Add(p,4));
                if(string.IsNullOrEmpty(path)) continue;
                var m=System.Text.RegularExpressions.Regex.Match(path,@"^\\\\\?\\hid#vid_([0-9a-f]{4})&pid_([0-9a-f]{4})",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if(!m.Success) continue;
                ushort vid=Convert.ToUInt16(m.Groups[1].Value,16), pid=Convert.ToUInt16(m.Groups[2].Value,16);
                string name=PropString(h,ref d,Reported);
                if(string.IsNullOrEmpty(name)) name=PropString(h,ref d,Friendly);
                if(string.IsNullOrEmpty(name)) name=PropString(h,ref d,Description);
                var root=System.Text.RegularExpressions.Regex.Replace(path,@"&col[0-9a-f]+","",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var kind=UsbCollectionKind(h,ref d,path);
                if(Fa09.IsReceiver(vid,pid)) {
                    ushort page;
                    bool proven=Fa09Collection(path,out page);
                    rows.Add(new Device {Id="fa09:"+PhysicalUsbId(d.Instance,vid,pid,root),Path=proven?path:null,Vendor=vid,Product=pid,
                        Name="FA09 2.4G 无线接收器",Link="2.4G",State="接收器已连接",
                        Note=proven?"接口已匹配，等待设备回复":"外设状态及电量未知；接口无法打开或未通过验证"});
                } else if(Protocol.IsLogitechEndpoint(vid,pid)) {
                    string ifaceKey=InterfaceKey(path);
                    if(LogitechShort(path)) {
                        if(shorts.ContainsKey(ifaceKey))shorts[ifaceKey]=null;
                        else shorts.Add(ifaceKey,path);
                    }
                    bool wired=Protocol.IsLogitechWired(vid,pid);
                    string why; bool usable=LogitechCollection(path,out why);
                    rows.Add(new Device{Id=PhysicalUsbId(d.Instance,vid,pid,root),Path=usable?path:null,Vendor=vid,Product=pid,
                        Name=wired?UsbDeviceLabel(d.Instance,vid,pid,path,name,"Logitech USB 鼠标"):"Logitech 接收器 "+pid.ToString("X4"),Link=wired?"USB":"2.4G",KindHint="mouse",BatteryNotApplicable=wired,State=wired?(kind=="mouse"?"已连接":"未确认设备状态"):"接收器已连接",InterfaceKey=ifaceKey,
                        Note=usable?"外设状态尚未确认":"HID++ 电量接口未验证："+why});
                } else if(RazerMouse.Supports(vid,pid)) {
                    bool ready=RazerCollection(path,vid,pid), wireless=RazerMouse.IsWireless(vid,pid);
                    rows.Add(new Device{Id="razer:"+PhysicalUsbId(d.Instance,vid,pid,root), Path=ready?path:null, Vendor=vid, Product=pid,
                        Name=UsbDeviceLabel(d.Instance,vid,pid,path,name,wireless?"Razer 接收器":"Razer USB 鼠标"),KindHint="mouse",Link=wireless?"2.4G · Razer":"USB · Razer", State=wireless?"接收器已连接":kind=="mouse"?"已连接":"未确认设备状态",
                        Note=ready?"接口已匹配，等待设备回复":"未找到能确认的雷蛇控制接口；未发送命令"});
                } else if(HyperX.IsReceiver(vid,pid)) {
                    if(!MatchesCollection(path,0xff13,1,62,62))continue;
                    rows.Add(new Device{Id=root,Path=path,Vendor=vid,Product=pid,Name="HyperX Cloud III Wireless",Link="2.4G · HyperX",State="接收器已连接",Note="耳机状态尚未确认"});
                } else if(CompxMouse.IsProfile(vid,pid)) {
                    bool ready=CompxControl(path,vid,pid);
                    var link=CompxMouse.TransportKind(vid,pid);
                    var label=UsbDeviceLabel(d.Instance,vid,pid,path,name,"CompX 鼠标或接收器");
                    if(link=="unknown"&&ReceiverName(label))link="receiver";
                    rows.Add(new Device{Id="compx:"+PhysicalUsbId(d.Instance,vid,pid,root),Path=ready?path:null,Vendor=vid,Product=pid,Name=label,
                        Link=link=="usb"?"USB":link=="receiver"?"2.4G":"USB / 无线",KindHint="mouse",
                        State=link=="receiver"?"接收器已连接":link=="usb"&&kind=="mouse"?"已连接":"未确认设备状态",
                        Note=ready?"接口已匹配，等待电量回复":"未找到匹配的 CompX 电量接口；未发送命令"});
                } else if(ReceiverOnly(vid,pid)||ReceiverName(name)||ReceiverName(UsbProductName(path))) {
                    name=UsbDeviceLabel(d.Instance,vid,pid,path,name,"USB 接收器");
                    string group=vid.ToString("X4")+":"+pid.ToString("X4")+":"+root.Split('#').Skip(2).FirstOrDefault();
                    rows.Add(new Device{Id="unknown:"+group,Vendor=vid,Product=pid,Name=name,Link="USB 接收器",State="接收器已连接",Note="未适配协议；外设状态和电量未知"});
                } else if(kind!=null) {
                    string parentName;string physical=PhysicalUsbId(d.Instance,vid,pid,null,true,out parentName);
                    if(physical==null)continue;
                    string product=UsbProductName(path);
                    string label=!string.IsNullOrWhiteSpace(parentName)?parentName:!string.IsNullOrWhiteSpace(product)?product:name;
                    if(string.IsNullOrWhiteSpace(label))label=kind=="mouse"?"USB 鼠标":kind=="keyboard"?"USB 键盘":"USB 手柄";
                    if(ReceiverName(label)){
                        rows.Add(new Device{Id="usb-hid:"+physical,Vendor=vid,Product=pid,Name=label,Link="USB 接收器",State="接收器已连接",
                            Note="仅确认接收器，未确认无线外设在线"});
                        continue;
                    }
                    rows.Add(new Device{Id="usb-hid:"+physical,Vendor=vid,Product=pid,Name=label,
                        Link="USB",State="已连接",KindHint=kind,Note="Windows 识别到活动 USB 接口；电量与厂商功能未验证"});
                }
            } finally { Marshal.FreeHGlobal(p); }
        }} finally { SetupDiDestroyDeviceInfoList(h); }
        return rows.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).Select(g=> {
            if(g.Key.StartsWith("usb-hid:",StringComparison.OrdinalIgnoreCase)){
                var blocked=g.FirstOrDefault(d=>d.State=="接收器已连接");if(blocked!=null)return blocked;
                var all=g.ToList();var label=all.FirstOrDefault(d=>!string.IsNullOrWhiteSpace(d.Name)&&!d.Name.StartsWith("HID",StringComparison.OrdinalIgnoreCase));
                if(label!=null){
                    var exact=all.FirstOrDefault(d=>d.KindHint==Design.Kind(new Device{Name=label.Name}));if(exact!=null)return exact;
                }
                if(all.Select(d=>d.KindHint).Distinct().Count()==1)return all[0];
                var maybe=all.FirstOrDefault(d=>d.KindHint=="keyboard");
                if(maybe!=null){maybe.Note="同一物理 USB 设备包含多个 HID 集合，按键盘归并";return maybe;}
                return all[0];
            }
            var verified=g.Where(x=>x.Path!=null).ToList();
            if(g.Key.StartsWith("fa09:",StringComparison.OrdinalIgnoreCase)&&verified.Count>1) {
                var row=g.First();row.Path=null;row.Note="同一接收器有多个匹配接口；未发送查询";return row;
            }
            if((g.Key.StartsWith("razer:",StringComparison.OrdinalIgnoreCase)||g.Key.StartsWith("compx:",StringComparison.OrdinalIgnoreCase))&&verified.Count>1){
                var row=g.First();row.Path=null;row.Note="多个厂商控制接口符合，未发送查询";return row;
            }
            if(verified.Count>0) {
                var entry=verified[0];
                if(g.Any(x=>x.State=="已连接"))entry.State="已连接";
                if(Protocol.IsLogitechEndpoint(entry.Vendor,entry.Product)) {
                    if(verified.Count!=1){entry.Path=null;entry.Note="多个控制接口符合，未发送查询";return entry;}
                    string s;if(shorts.TryGetValue(entry.InterfaceKey,out s))entry.ShortPath=s;
                }
                return entry;
            }
            var first=g.OrderByDescending(x=>x.State=="已连接").First();
            if(Protocol.IsLogitechEndpoint(first.Vendor,first.Product)) {
                var checks=g.GroupBy(x=>x.Note,StringComparer.Ordinal).Select(x=>x.Count()+" 个："+x.Key);
                first.Note="已检查 "+g.Count()+" 个接口；"+string.Join("；",checks);
            }
            return first;
        }).ToList();
    }
    internal static List<string> DiagnosticModels() {
        var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var h=ClassDevs(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,AllClasses|Present);
        if(h==Invalid) throw new System.ComponentModel.Win32Exception();
        try {for(uint i=0;;i++) {
            var d=Info.New();if(!SetupDiEnumDeviceInfo(h,i,ref d))break;
            var id=Instance(h,ref d);
            var m=System.Text.RegularExpressions.Regex.Match(id,@"^USB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})\\",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if(!m.Success)continue;
            var name=PropString(h,ref d,Reported);
            if(string.IsNullOrEmpty(name)) name=PropString(h,ref d,Friendly);
            if(!string.IsNullOrEmpty(name)&&System.Text.RegularExpressions.Regex.IsMatch(name,@"mouse|headset|2\.4\s*g|dongle|receiver|keyboard|mic|鼠标|耳机|键盘|接收器|麦克风",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                found.Add(m.Groups[1].Value.ToUpperInvariant()+":"+m.Groups[2].Value.ToUpperInvariant()+" "+name.Replace("\r"," ").Replace("\n"," "));
        }} finally{SetupDiDestroyDeviceInfoList(h);}
        return found.OrderBy(x=>x).ToList();
    }
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent,uint child,uint flags);
    [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_Device_ID(uint inst,StringBuilder id,uint cap,uint flags);
    [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode, EntryPoint="CM_Get_DevNode_PropertyW")]
    static extern uint CM_DeviceProperty(uint instance,ref Key key,out uint type,[Out] byte[] value,ref uint size,uint flags);
    static string PhysicalUsbId(uint child,ushort vid,ushort pid,string fallback) {
        string ignored;return PhysicalUsbId(child,vid,pid,fallback,false,out ignored);
    }
    static string PhysicalUsbId(uint child,ushort vid,ushort pid,string fallback,bool getName,out string product) {
        product=null;
        string prefix=string.Format(@"USB\VID_{0:X4}&PID_{1:X4}\",vid,pid);
        for(int j=0;j<8;j++) {
            var id=new StringBuilder(512);
            if(CM_Get_Device_ID(child,id,(uint)id.Capacity,0)==0&&id.ToString().StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            {
                if(getName){
                    var buf=new byte[512];uint t,len=(uint)buf.Length;var key=Reported;
                    if(CM_DeviceProperty(child,ref key,out t,buf,ref len,0)==0&&t==18&&len<=buf.Length)
                        product=Encoding.Unicode.GetString(buf,0,(int)len).TrimEnd('\0').Trim();
                }
                return id.ToString();
            }
            uint p;if(CM_Get_Parent(out p,child,0)!=0)break;child=p;
        }
        return fallback;
    }
    internal static bool Fa09Collection(string path,out ushort page) {
        page=0;
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
            if(h.IsInvalid)return false;
            byte[] name=new byte[256];
            if(!HidD_GetProductString(h,name,(uint)name.Length))return false;
            string product=Encoding.Unicode.GetString(name).TrimEnd('\0');
            if(!string.Equals(product,"2.4G Wireless Receiver",StringComparison.OrdinalIgnoreCase)
                &&!string.Equals(product,"MCHOSE 2.4G Wireless",StringComparison.OrdinalIgnoreCase))return false;
            IntPtr p;if(!HidD_GetPreparsedData(h,out p))return false;
            try {Caps c;
                if(HidP_GetCaps(p,out c)!=0x110000 ||
                    (c.Page!=0xff02&&c.Page!=0xff04)||c.Usage!=2||c.Input!=20||c.Output!=20||
                    c.InButtons!=0||c.OutButtons!=0)return false;
                if(!Fa09Bytes(p,0,c.InValues)||!Fa09Bytes(p,1,c.OutValues))return false;
                page=c.Page;return true;
            } finally {HidD_FreePreparsedData(p);}
        }
    }
    static bool Fa09Bytes(IntPtr p,int direction,ushort n) {
        if(n==0||n>32)return false;
        IntPtr raw=Marshal.AllocHGlobal(72*n);
        try {
            if(HidP_GetValueCaps(direction,raw,ref n,p)!=0x110000 || n==0)return false;
            int count=0;
            for(int j=0;j<n;j++) {
                var b=IntPtr.Add(raw,72*j);
                if(Marshal.ReadByte(b,2)!=0x13||Marshal.ReadInt16(b,18)!=8)return false;
                count+=Marshal.ReadInt16(b,20);
            }
            return count==19;
        } finally {Marshal.FreeHGlobal(raw);}
    }
    static bool LogitechCollection(string path,out string reason){
        reason="未找到 20 字节 HID++ 接口";
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){
            if(h.IsInvalid){reason="Windows 无法打开接口（错误 "+Marshal.GetLastWin32Error()+"）";return false;}
            IntPtr p;if(!HidD_GetPreparsedData(h,out p)){reason="无法读取接口描述";return false;}
            try{Caps c;if(HidP_GetCaps(p,out c)!=0x110000)return false;
                return LogitechLongCaps(c.Page,c.Usage,c.Input,c.Output);
            }finally{HidD_FreePreparsedData(p);}
        }
    }
    internal static string InterfaceKey(string path) {
        var parts=path.Split('#');
        if(parts.Length<4)return path;
        // Ignore collection number only; the final instance discriminator is its collection index on Windows.
        var hardware=System.Text.RegularExpressions.Regex.Replace(parts[1],@"(?i)&col[0-9a-f]+$","");
        if(hardware==parts[1])return path;
        var instance=System.Text.RegularExpressions.Regex.Replace(parts[2],@"(?i)&[0-9a-f]{4}$","");
        return hardware+"#"+instance+"#"+parts[3];
    }
    internal static bool LogitechLongCaps(ushort page,ushort usage,ushort input,ushort output) {
        return input==20&&output==20&&((page==0xff00&&usage==2)||(page==0xff43&&usage==0x0602));
    }
    internal static int RazerInterface(string path,ushort vid,ushort pid){
        if(string.IsNullOrEmpty(path))return -1;
        var match=System.Text.RegularExpressions.Regex.Match(path,@"(?i)^\\\\\?\\hid#vid_([0-9a-f]{4})&pid_([0-9a-f]{4})&mi_([0-9a-f]{2})(?:&col[0-9a-f]+)?#");
        if(!match.Success||Convert.ToUInt16(match.Groups[1].Value,16)!=vid||Convert.ToUInt16(match.Groups[2].Value,16)!=pid)return -1;
        return Convert.ToInt32(match.Groups[3].Value,16);
    }
    internal static bool RazerCollection(string path,ushort vid,ushort pid){return RazerMode(path,vid,pid)!=0;}
    internal static int RazerMode(string path,ushort vid,ushort pid){
        int iface=RazerInterface(path,vid,pid);
        if(iface<0)return 0;
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){
            if(h.IsInvalid)return 0;
            IntPtr p;if(!HidD_GetPreparsedData(h,out p))return 0;
            try{
                Caps c;if(HidP_GetCaps(p,out c)!=0x110000)return 0;
                if(RazerMouse.AllowsFeature(vid,pid,iface,c.Page,c.Usage,c.Feature))return 2;
                return RazerMouse.AllowsCollection(vid,pid,iface,c.Page,c.Usage,c.Input,c.Output)?1:0;
            }
            finally{HidD_FreePreparsedData(p);}
        }
    }
    internal static bool CompxControl(string path,ushort vid,ushort pid){
        return CompxMouse.IsProfile(vid,pid)&&(!CompxMouse.NeedsInterfaceOne(vid,pid)||RazerInterface(path,vid,pid)==1)&&CompxCollection(path);
    }
    internal static bool CompxCollection(string path){return path!=null&&MatchesCollection(path,0xff02,2,17,17);}
    internal static string KindFromHid(ushort page,ushort usage){
        if(page!=1)return null;
        if(usage==2)return "mouse";
        if(usage==6||usage==7)return "keyboard";
        if(usage==4||usage==5)return "gamepad";
        return null;
    }
    internal static string KindFromHidIds(IEnumerable<string> ids){
        foreach(var id in ids){
            var m=System.Text.RegularExpressions.Regex.Match(id,@"^HID_DEVICE_UP:([A-F0-9]{4})_U:([A-F0-9]{4})$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if(m.Success)return KindFromHid(Convert.ToUInt16(m.Groups[1].Value,16),Convert.ToUInt16(m.Groups[2].Value,16));
        }
        foreach(var id in ids){
            if(id.Equals("HID_DEVICE_SYSTEM_MOUSE",StringComparison.OrdinalIgnoreCase))return "mouse";
            if(id.Equals("HID_DEVICE_SYSTEM_KEYBOARD",StringComparison.OrdinalIgnoreCase))return "keyboard";
            if(id.Equals("HID_DEVICE_SYSTEM_GAME",StringComparison.OrdinalIgnoreCase))return "gamepad";
        }
        return null;
    }
    static string UsbCollectionKind(IntPtr set,ref Info d,string path){
        uint t;var b=Prop(set,ref d,HardwareIds,out t);
        if(t==0x2012||t==18){
            var kind=KindFromHidIds(Encoding.Unicode.GetString(b).Split(new[]{'\0'},StringSplitOptions.RemoveEmptyEntries));
            if(kind!=null)return kind;
        }
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){
            if(h.IsInvalid)return null;
            IntPtr p;if(!HidD_GetPreparsedData(h,out p))return null;
            try{Caps c;return HidP_GetCaps(p,out c)==0x110000?KindFromHid(c.Page,c.Usage):null;}finally{HidD_FreePreparsedData(p);}
        }
    }
    // These known receiver identities only prevent a receiver from being mistaken for a wired device.
    internal static bool ReceiverOnly(ushort vid,ushort pid){return vid==0x0b05&&(pid==0x1a68||pid==0x1ace);}
    static string UsbDeviceLabel(uint instance,ushort vid,ushort pid,string path,string name,string fallback) {
        string parent;
        PhysicalUsbId(instance,vid,pid,null,true,out parent);
        if(!string.IsNullOrWhiteSpace(parent))return parent;
        string product=UsbProductName(path);
        if(!string.IsNullOrWhiteSpace(product))return product;
        return string.IsNullOrWhiteSpace(name)?fallback:name;
    }
    internal static bool ReceiverName(string n){
        return !string.IsNullOrEmpty(n)&&System.Text.RegularExpressions.Regex.IsMatch(n,@"2\.4\s*g|dongle|receiver|接收器|transceiver|wireless adapter|无线适配器|unifying|omni receiver",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    static string UsbProductName(string path){
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){
            if(h.IsInvalid)return null;
            var b=new byte[256];return HidD_GetProductString(h,b,(uint)b.Length)?Encoding.Unicode.GetString(b).TrimEnd('\0').Trim():null;
        }
    }

    static bool IsStandardMouse(string path) {
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)){
            if(h.IsInvalid)return false;
            IntPtr p;if(!HidD_GetPreparsedData(h,out p))return false;
            try{Caps c;return HidP_GetCaps(p,out c)==0x110000&&c.Page==1&&c.Usage==2;}
            finally{HidD_FreePreparsedData(p);}
        }
    }
    internal static bool LogitechShort(string path) {
        return MatchesCollection(path,0xff00,1,7,7);
    }
    internal static bool CorrectCollection(string path) { string why; return path!=null&&LogitechCollection(path,out why); }
    internal static bool MatchesCollection(string path,ushort page,ushort? usage,ushort input,ushort output) {
        using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
            if(h.IsInvalid) return false;
            IntPtr p; if(!HidD_GetPreparsedData(h,out p)) return false;
            try { Caps c; return HidP_GetCaps(p,out c)==0x110000 && c.Page==page && (!usage.HasValue||c.Usage==usage.Value) && c.Input==input && c.Output==output; }
            finally { HidD_FreePreparsedData(p); }
        }
    }
}
