using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

internal static partial class Audio {
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    interface ISessionManager {
        [PreserveSig] int GetAudioSessionControl(ref Guid id, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid id, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out ISessions sessions);
        [PreserveSig] int RegisterSessionNotification(IPdSessionCreated notification);
        [PreserveSig] int UnregisterSessionNotification(IPdSessionCreated notification);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    interface ISessions {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }
    // Flattened IAudioSessionControl + IAudioSessionControl2 vtable, in SDK order.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    interface ISessionControl {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IPdSessionEvents notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IPdSessionEvents notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    interface ISessionVolume {
        [PreserveSig] int SetMasterVolume(float level, IntPtr context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    internal sealed class Session {
        internal string EndpointId, EndpointName, InstanceId, AppKey, Name;
        internal uint ProcessId;
        internal int Level, State;
        internal bool Muted;
        internal string Key { get { return EndpointId+"\n"+InstanceId; } }
    }
    internal sealed class AppVolume {
        internal string Key, Name, EndpointName;
        internal List<Session> Sessions;
        internal int Level { get { return Sessions.Max(s=>s.Level); } }
        internal bool Muted { get { return Sessions.All(s=>s.Muted); } }
        internal bool Mixed { get { return Sessions.Any(s=>s.Level!=Level||s.Muted!=Muted); } }
        internal bool Playing { get { return Sessions.Any(s=>s.State==1); } }
    }
    internal sealed class SessionScan {
        internal readonly List<Session> Sessions=new List<Session>();
        internal int Unavailable;
    }
    internal static List<AppVolume> GroupSessions(IEnumerable<Session> sessions) {
        return sessions.Where(s=>s.State==1&&!string.IsNullOrEmpty(s.InstanceId))
            .GroupBy(s=>s.Key).Select(g=>g.First())
            .GroupBy(s=>s.AppKey??("session:"+s.Key),StringComparer.Ordinal)
            .Select(g=>new AppVolume{Key=g.Key,Name=g.First().Name,
                EndpointName=g.Select(s=>s.EndpointId).Distinct().Count()>1?g.Select(s=>s.EndpointId).Distinct().Count()+" 个播放设备":g.First().EndpointName,Sessions=g.ToList()})
            .OrderBy(g=>g.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(g=>g.EndpointName).ThenBy(g=>g.Key).ToList();
    }
    static Session ReadSession(object raw,Endpoint endpoint) {
        var control=(ISessionControl)raw;
        int state;Ok(control.GetState(out state));if(state==2)return null;
        string instance;Ok(control.GetSessionInstanceIdentifier(out instance));
        uint pid;Ok(control.GetProcessId(out pid));
        string name=null,key=null;
        if(control.IsSystemSoundsSession()==0){name="系统声音";key="system";}
        else if(pid!=0) {
            try {using(var process=Process.GetProcessById((int)pid)) {
                if(process.HasExited)return null;
                name=process.ProcessName;
                try {
                    var module=process.MainModule;
                    key="exe:"+module.FileName.ToUpperInvariant();
                    if(!string.IsNullOrWhiteSpace(module.FileVersionInfo.FileDescription))name=module.FileVersionInfo.FileDescription;
                } catch(Exception) { key="process:"+process.ProcessName.ToUpperInvariant(); }
            }} catch(ArgumentException){return null;} catch(InvalidOperationException){return null;}
            catch(System.ComponentModel.Win32Exception) { key="pid:"+pid; }
        }
        if(string.IsNullOrWhiteSpace(name)) {
            string display;
            if(control.GetDisplayName(out display)>=0&&!string.IsNullOrWhiteSpace(display)&&!display.StartsWith("@"))name=display;
            else name=pid==0?"音频应用":"应用 "+pid;
        }
        float level;bool mute;var volume=(ISessionVolume)raw;
        Ok(volume.GetMasterVolume(out level));Ok(volume.GetMute(out mute));
        return new Session{EndpointId=endpoint.Id,EndpointName=endpoint.Name,InstanceId=instance,ProcessId=pid,
            AppKey=key??("session:"+instance),Name=name,Level=(int)Math.Round(level*100),Muted=mute,State=state};
    }
    // A fresh manager per scan queries each active output, including non-default routes.
    // Only plain snapshots cross threads; every COM reference is released on its owner thread.
    internal static SessionScan ReadSessions() {
        var result=new SessionScan();IEnum enumerator=null;
        try {
            enumerator=(IEnum)new Enumerator();
            foreach(var endpoint in ActiveChoices(Outputs())) {
                IDevice device=null;object manager=null;ISessions sessions=null;
                try {
                    Ok(enumerator.GetDevice(endpoint.Id,out device));
                    var iid=typeof(ISessionManager).GUID;Ok(device.Activate(ref iid,23,IntPtr.Zero,out manager));
                    Ok(((ISessionManager)manager).GetSessionEnumerator(out sessions));
                    int count;Ok(sessions.GetCount(out count));
                    for(int i=0;i<count;i++) {
                        object raw=null;
                        try {Ok(sessions.GetSession(i,out raw));var session=ReadSession(raw,endpoint);if(session!=null)result.Sessions.Add(session);}
                        catch(COMException){result.Unavailable++;}
                        catch(InvalidCastException){result.Unavailable++;}
                        finally{Release(raw);}
                    }
                } catch(COMException){result.Unavailable++;}
                finally{Release(sessions);Release(manager);Release(device);}
            }
        } finally{Release(enumerator);}
        return result;
    }
    internal static void SetSessions(IEnumerable<Session> expected,int? level,bool? mute) {
        var targets=expected.GroupBy(s=>s.Key).Select(g=>g.First()).ToList();
        if(targets.Count==0)throw new InvalidOperationException("应用已关闭，请刷新列表");
        int changed=0;IEnum enumerator=null;
        try {
            enumerator=(IEnum)new Enumerator();
            foreach(var group in targets.GroupBy(s=>s.EndpointId)) {
                IDevice device=null;object manager=null;ISessions sessions=null;
                try {
                    Ok(enumerator.GetDevice(group.Key,out device));uint state;Ok(device.GetState(out state));
                    if(state!=1)throw new InvalidOperationException("播放设备已断开，请刷新列表");
                    var iid=typeof(ISessionManager).GUID;Ok(device.Activate(ref iid,23,IntPtr.Zero,out manager));
                    Ok(((ISessionManager)manager).GetSessionEnumerator(out sessions));
                    var ids=new HashSet<string>(group.Select(s=>s.InstanceId),StringComparer.Ordinal);
                    int count;Ok(sessions.GetCount(out count));
                    for(int i=0;i<count;i++) {
                        object raw=null;
                        try {
                            Ok(sessions.GetSession(i,out raw));var control=(ISessionControl)raw;
                            string id;Ok(control.GetSessionInstanceIdentifier(out id));
                            if(!ids.Contains(id))continue;
                            int sessionState;Ok(control.GetState(out sessionState));if(sessionState==2)continue;
                            var volume=(ISessionVolume)raw;
                            if(level.HasValue)Ok(volume.SetMasterVolume(Math.Max(0,Math.Min(100,level.Value))/100f,IntPtr.Zero));
                            if(mute.HasValue)Ok(volume.SetMute(mute.Value,IntPtr.Zero));
                            float actual;bool muted;Ok(volume.GetMasterVolume(out actual));Ok(volume.GetMute(out muted));
                            if((level.HasValue&&Math.Abs(actual-Math.Max(0,Math.Min(100,level.Value))/100f)>0.011f)||
                                (mute.HasValue&&muted!=mute.Value))throw new InvalidOperationException("Windows 未确认音量修改");
                            changed++;
                        } finally{Release(raw);}
                    }
                } finally{Release(sessions);Release(manager);Release(device);}
            }
        } finally{Release(enumerator);}
        if(changed!=targets.Count)throw new InvalidOperationException("部分声音已结束或变化，请刷新后重试");
    }
}
