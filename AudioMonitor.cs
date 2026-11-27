using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

// The callback methods only signal the worker. No COM calls, waits or UI work occur here.
[ComVisible(true), Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPdSessionEvents {
    [PreserveSig] int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string name,ref Guid context);
    [PreserveSig] int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string path,ref Guid context);
    [PreserveSig] int OnSimpleVolumeChanged(float volume,[MarshalAs(UnmanagedType.Bool)] bool mute,ref Guid context);
    [PreserveSig] int OnChannelVolumeChanged(uint count,IntPtr volumes,uint changed,ref Guid context);
    [PreserveSig] int OnGroupingParamChanged(ref Guid grouping,ref Guid context);
    [PreserveSig] int OnStateChanged(int state);
    [PreserveSig] int OnSessionDisconnected(int reason);
}
[ComVisible(true), Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPdSessionCreated {
    [PreserveSig] int OnSessionCreated(IntPtr session);
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class PdSessionSignal : IPdSessionEvents,IPdSessionCreated {
    readonly Action signal;
    internal PdSessionSignal(Action action){signal=action;}
    public int OnDisplayNameChanged(string name,ref Guid context){signal();return 0;}
    public int OnIconPathChanged(string path,ref Guid context){return 0;}
    public int OnSimpleVolumeChanged(float volume,bool mute,ref Guid context){signal();return 0;}
    public int OnChannelVolumeChanged(uint count,IntPtr volumes,uint changed,ref Guid context){return 0;}
    public int OnGroupingParamChanged(ref Guid grouping,ref Guid context){signal();return 0;}
    public int OnStateChanged(int state){signal();return 0;}
    public int OnSessionDisconnected(int reason){signal();return 0;}
    public int OnSessionCreated(IntPtr session){signal();return 0;}
}

internal static partial class Audio {
    internal sealed class SessionMonitor : IDisposable {
        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint flags);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
        readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly Thread worker;
        readonly Action<SessionScan> publish;
        readonly PdSessionSignal signal;
        readonly Dictionary<string,OutputWatch> outputs=new Dictionary<string,OutputWatch>(StringComparer.Ordinal);
        volatile bool stopped;
        sealed class SessionWatch {
            internal object Raw;
            internal bool Registered;
        }
        sealed class OutputWatch {
            internal object Manager;
            internal bool Registered;
            internal readonly Dictionary<string,SessionWatch> Sessions=new Dictionary<string,SessionWatch>(StringComparer.Ordinal);
        }
        internal SessionMonitor(Action<SessionScan> callback) {
            publish=callback;signal=new PdSessionSignal(RequestRefresh);
            worker=new Thread(Run){IsBackground=true,Name="PeripheralDesk audio notifications"};
            worker.SetApartmentState(ApartmentState.MTA);worker.Start();
        }
        internal void RequestRefresh(){if(stopped)return;try{wake.Set();}catch(ObjectDisposedException){}}
        void Run() {
            bool initialized=false;
            try {
                Ok(CoInitializeEx(IntPtr.Zero,0));initialized=true;
                while(!stopped) {
                    SessionScan scan;
                    try{scan=Scan();}catch(Exception){Clear();scan=new SessionScan{Unavailable=1};}
                    if(!stopped)publish(scan);
                    // Device discovery/recovery only; unchanged snapshots produce no UI updates.
                    wake.WaitOne(10000);
                    if(!stopped)Thread.Sleep(60); // Coalesce callback bursts, without blocking a callback.
                }
            } catch(Exception) {if(!stopped)publish(new SessionScan{Unavailable=1});}
            finally{Clear();if(initialized)CoUninitialize();wake.Dispose();}
        }
        SessionScan Scan() {
            var scan=new SessionScan();var endpoints=ActiveChoices(Outputs());
            var ids=new HashSet<string>(endpoints.Select(e=>e.Id),StringComparer.Ordinal);
            foreach(string removed in outputs.Keys.Where(k=>!ids.Contains(k)).ToList()){Close(outputs[removed]);outputs.Remove(removed);}
            IEnum enumerator=null;
            try {
                enumerator=(IEnum)new Enumerator();
                foreach(var endpoint in endpoints) {
                    OutputWatch watch;
                    if(!outputs.TryGetValue(endpoint.Id,out watch)){watch=new OutputWatch();outputs.Add(endpoint.Id,watch);}
                    ISessions sessions=null;
                    try {
                        if(watch.Manager==null) {
                            IDevice device=null;
                            try{Ok(enumerator.GetDevice(endpoint.Id,out device));var iid=typeof(ISessionManager).GUID;Ok(device.Activate(ref iid,23,IntPtr.Zero,out watch.Manager));}
                            finally{Release(device);}
                            Ok(((ISessionManager)watch.Manager).RegisterSessionNotification(signal));watch.Registered=true;
                        }
                        Ok(((ISessionManager)watch.Manager).GetSessionEnumerator(out sessions));
                        int count;Ok(sessions.GetCount(out count)); // Enables session creation callbacks.
                        var present=new HashSet<string>(StringComparer.Ordinal);
                        for(int i=0;i<count;i++) {
                            object raw=null;
                            try {
                                Ok(sessions.GetSession(i,out raw));var snapshot=ReadSession(raw,endpoint);
                                if(snapshot==null)continue;
                                present.Add(snapshot.InstanceId);
                                if(!watch.Sessions.ContainsKey(snapshot.InstanceId)) {
                                    Ok(((ISessionControl)raw).RegisterAudioSessionNotification(signal));
                                    watch.Sessions.Add(snapshot.InstanceId,new SessionWatch{Raw=raw,Registered=true});raw=null;
                                    // Re-read after registration to avoid missing a volume/state change in the gap.
                                    snapshot=ReadSession(watch.Sessions[snapshot.InstanceId].Raw,endpoint);
                                }
                                if(snapshot!=null)scan.Sessions.Add(snapshot);
                            } catch(COMException){scan.Unavailable++;}
                            catch(InvalidCastException){scan.Unavailable++;}
                            finally{Release(raw);}
                        }
                        foreach(string removed in watch.Sessions.Keys.Where(k=>!present.Contains(k)).ToList()){
                            Close(watch.Sessions[removed]);watch.Sessions.Remove(removed);
                        }
                    } catch(Exception) {scan.Unavailable++;Close(watch);outputs.Remove(endpoint.Id);}
                    finally{Release(sessions);}
                }
            } finally{Release(enumerator);}
            return scan;
        }
        void Close(SessionWatch session) {
            try{if(session.Registered)((ISessionControl)session.Raw).UnregisterAudioSessionNotification(signal);}catch(Exception){}
            Release(session.Raw);
        }
        void Close(OutputWatch output) {
            foreach(var session in output.Sessions.Values)Close(session);output.Sessions.Clear();
            try{if(output.Registered)((ISessionManager)output.Manager).UnregisterSessionNotification(signal);}catch(Exception){}
            Release(output.Manager);
        }
        void Clear(){foreach(var output in outputs.Values)Close(output);outputs.Clear();}
        public void Dispose(){stopped=true;try{wake.Set();}catch(ObjectDisposedException){}if(Thread.CurrentThread!=worker)worker.Join(2000);}
    }
}
