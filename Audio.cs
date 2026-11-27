using System;
using System.Runtime.InteropServices;

internal static partial class Audio {
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class Enumerator {}
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")] interface IEnum {
        [PreserveSig] int EnumAudioEndpoints(int flow,uint state,out ICollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IDevice endpoint);
    }
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")] interface ICollection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index,out IDevice endpoint);
    }
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("D666063F-1587-4E43-81F1-B948E807363F")] interface IDevice {
        [PreserveSig] int Activate(ref Guid iid,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object volume);
        [PreserveSig] int OpenPropertyStore(uint access,out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [StructLayout(LayoutKind.Sequential)] struct Key {public Guid Guid;public uint Pid;}
    [StructLayout(LayoutKind.Explicit,Size=24)] struct Variant {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")] interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint i,out Key key);
        [PreserveSig] int GetValue(ref Key key,out Variant value);
    }
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("5CDF2C82-841E-4546-9722-0CF74078229A")] interface IVolume {
        [PreserveSig] int Register(IntPtr callback);
        [PreserveSig] int Unregister(IntPtr callback);
        [PreserveSig] int ChannelCount(out uint count);
        [PreserveSig] int SetDb(float db,IntPtr context);
        [PreserveSig] int SetScalar(float value,IntPtr context);
        [PreserveSig] int GetDb(out float db);
        [PreserveSig] int GetScalar(out float value);
        [PreserveSig] int SetChannelDb(uint channel,float value,IntPtr context);
        [PreserveSig] int SetChannelScalar(uint channel,float value,IntPtr context);
        [PreserveSig] int GetChannelDb(uint channel,out float value);
        [PreserveSig] int GetChannelScalar(uint channel,out float value);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)]bool value,IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)]out bool value);
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref Variant value);
    internal sealed class Snapshot { public string Id,Name; public int Level; public bool Muted; }
    static void Ok(int code) { if(code<0) Marshal.ThrowExceptionForHR(code); }
    static void Release(object o) { if(o!=null&&Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
    static string Name(IDevice e) {
        IPropertyStore p=null;
        try {
            if(e.OpenPropertyStore(0,out p)<0||p==null)return "";
            var k=new Key{Guid=new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),Pid=14};
            Variant v; if(p.GetValue(ref k,out v)<0)return "";
            try { return v.Type==31?Marshal.PtrToStringUni(v.Pointer):""; }
            finally {PropVariantClear(ref v);}
        } finally {Release(p);}
    }
    static string AudioName(string s) {
        return System.Text.RegularExpressions.Regex.Replace((s??"").ToLowerInvariant(),@"[^\p{L}\p{N}]","");
    }
    internal static bool IsFreeClip2(string s){return AudioName(s).Contains("freeclip2");}
    internal static bool? ConfirmedPlayback(string name,System.Collections.Generic.IEnumerable<Endpoint> outputs){
        if(outputs==null)return null;
        string target=AudioName(name);
        if(IsFreeClip2(name))target="freeclip2";
        else if(target.Length<8)return null;
        bool seen=false;
        foreach(var endpoint in outputs){
            if(!AudioName(endpoint.Name).Contains(target))continue;
            seen=true;
            if((endpoint.State&1)!=0)return true;
        }
        return seen?(bool?)false:null;
    }
    internal sealed class Endpoint { internal string Name,Id; internal uint State; public override string ToString(){return Name;} }
    // Windows' desktop audio policy COM ABI. Only the endpoint setter is called.
    [ComImport,Guid("F8679F50-850A-41CF-9C72-430F290290C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEndpointPolicy {
        void Reserved0();void Reserved1();void Reserved2();void Reserved3();void Reserved4();void Reserved5();void Reserved6();void Reserved7();void Reserved8();void Reserved9();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)]string id,int role);
    }
    static string DefaultId(IEnum e,bool input,int role){
        IDevice d=null;
        try{if(e.GetDefaultAudioEndpoint(input?1:0,role,out d)<0||d==null)return null;string id;Ok(d.GetId(out id));return id;}
        finally{Release(d);}
    }
    internal static string DefaultEndpoint(bool input){
        IEnum e=null;try{e=(IEnum)new Enumerator();return DefaultId(e,input,1)??DefaultId(e,input,0);}
        finally{Release(e);}
    }
    internal static System.Collections.Generic.List<Endpoint> ActiveChoices(System.Collections.Generic.IEnumerable<Endpoint> all) {
        var result=new System.Collections.Generic.List<Endpoint>();var seen=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var x in all)if(x!=null&&x.State==1&&!string.IsNullOrWhiteSpace(x.Id)&&seen.Add(x.Id))result.Add(x);
        result.Sort((a,b)=>StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name));return result;
    }
    internal static bool EndpointMatches(string physical,string endpoint){
        string target=AudioName(physical), name=AudioName(endpoint);
        return IsFreeClip2(physical)?name.Contains("freeclip2"):target.Length>=6&&name.Contains(target);
    }
    internal static bool DefaultPolicyAvailable() {
        object p=null;
        try {p=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"),true));
            return p is IEndpointPolicy;
        }catch(Exception){return false;}finally{Release(p);}
    }
    internal static void SetDefault(bool input,string id) {
        string label=input?"麦克风":"播放设备";
        if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("没有选中"+label);
        IEnum e=null;object policy=null;
        try{
            e=(IEnum)new Enumerator();
            if(!ActiveChoices(Endpoints(input)).Exists(o=>string.Equals(o.Id,id,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(label+"已经离线，没有切换");
            string[] old={DefaultId(e,input,0),DefaultId(e,input,1)};
            if(string.Equals(old[0],id,StringComparison.OrdinalIgnoreCase)&&string.Equals(old[1],id,StringComparison.OrdinalIgnoreCase))return;
            policy=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"),true));
            var setter=(IEndpointPolicy)policy;
            var changed=new bool[2];
            try {
                foreach(int role in new[]{0,1}){
                    string prior=DefaultId(e,input,role);
                    if(string.Equals(prior,id,StringComparison.OrdinalIgnoreCase))continue;
                    if(!string.Equals(prior,old[role],StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("系统默认设备同时被修改");
                    Ok(setter.SetDefaultEndpoint(id,role));changed[role]=true;
                }
                for(int role=0;role<2;role++){
                    if(!string.Equals(DefaultId(e,input,role),id,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Windows 没有确认默认"+label+"已切换");
                }
            }catch(Exception){
                for(int role=1;role>=0;role--)try{
                    if(changed[role]&&old[role]!=null&&string.Equals(DefaultId(e,input,role),id,StringComparison.OrdinalIgnoreCase))
                        setter.SetDefaultEndpoint(old[role],role);
                }catch(Exception){}
                throw;
            }
        }finally{Release(policy);Release(e);}
    }

    internal static System.Collections.Generic.List<Endpoint> Outputs() {return Endpoints(false);}
    internal static System.Collections.Generic.List<Endpoint> Endpoints(bool input) {
        var list=new System.Collections.Generic.List<Endpoint>();
        IEnum e=null;ICollection c=null; 
        try {
            e=(IEnum)new Enumerator();Ok(e.EnumAudioEndpoints(input?1:0,15,out c));
            uint count;Ok(c.GetCount(out count));
            for(uint n=0;n<count;n++){
                IDevice d=null;try { if(c.Item(n,out d)<0||d==null)continue;uint state;if(d.GetState(out state)<0)continue;
                    string name=Name(d),id;if(d.GetId(out id)>=0&&!string.IsNullOrWhiteSpace(name))list.Add(new Endpoint{Name=name,Id=id,State=state});
                }finally{Release(d);}
            }
            return list;
        }finally{Release(c);Release(e);}
    }
    internal static Snapshot Access(bool input,string expected=null,int? set=null,bool? mute=null) {
        IEnum e=null; IDevice d=null;object v=null;
        try {
            e=(IEnum)new Enumerator();
            Ok(e.GetDefaultAudioEndpoint(input?1:0,1,out d));
            string id; Ok(d.GetId(out id));
            if(expected!=null&&!string.Equals(id,expected,StringComparison.Ordinal))
                throw new InvalidOperationException("默认音频设备已变化，没有改动");
            var g=typeof(IVolume).GUID;Ok(d.Activate(ref g,23,IntPtr.Zero,out v));
            var vol=(IVolume)v;
            if(set.HasValue)Ok(vol.SetScalar(Math.Max(0,Math.Min(100,set.Value))/100f,IntPtr.Zero));
            if(mute.HasValue)Ok(vol.SetMute(mute.Value,IntPtr.Zero));
            float t; bool m;Ok(vol.GetScalar(out t));Ok(vol.GetMute(out m));
            return new Snapshot{Id=id,Name=Name(d),Level=(int)Math.Round(t*100),Muted=m};
        } finally {Release(v);Release(d);Release(e);}
    }
}
