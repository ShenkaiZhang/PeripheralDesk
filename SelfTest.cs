using System;
using System.IO;
using System.Linq;
internal static class SelfTest {
    static int passed;
    static void Check(bool result,string name) {
        if(!result)throw new InvalidOperationException("FAIL "+name);
        passed++;
    }
    internal static void Ui() {
        System.Windows.Forms.Application.EnableVisualStyles();
        using(var mixer=new MixerPanel(true))Check(mixer.VerifyStableUpdates(),"unchanged snapshots cause zero list layouts; volume updates reuse controls; paused row alone is removed");
            using(var ui=new Desk(true,true)) {
                ui.PerformLayout();
                Check(ui.VerifyDemoRouting(),"playback and recording selectors route independently and rebind their sliders");
                Check(ui.Controls.Count==1,"desktop root layout");
                var grid=(System.Windows.Forms.TableLayoutPanel)ui.Controls[0];
                Check(grid.Controls.Count==11,"device controls, mixer and two-page navigation");
                Check(ui.VerifyMixerPage(),"second page adjusts one application independently and returns to device controls");
                var deck=grid.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single();
                Check(deck.Controls.Count==1,"empty deck placeholder");
                var sample=new[]{new Device{Id="on",State="已连接"},new Device{Id="off",State="未连接"},
                    new Device{Id="unknown",State="未取得在线状态"},new Device{Id="receiver",State="接收器已连接"}};
                Check(Desk.VisibleDevices(sample).Select(d=>d.Id).SequenceEqual(new[]{"on"}),"panel shows only confirmed online devices");
                Console.WriteLine("PASS WinForms layout smoke test (no window displayed)");
            }
    }
    internal static void Windows() {
        Ui();
        try {
            var bluetooth=NativeDevices.Bluetooth();
            var receivers=NativeDevices.Receivers();
            Console.WriteLine("PASS Windows device enumeration: "+bluetooth.Count+" Bluetooth entries, "+receivers.Count+" USB groups");
            Console.WriteLine("Bluetooth live battery rows: "+bluetooth.Count(x=>x.Battery.HasValue));
            foreach(var grp in bluetooth.GroupBy(x=>x.State)) Console.WriteLine("BT state count: "+grp.Key+" = "+grp.Count());
            foreach(var d in bluetooth.Where(x=>Audio.IsFreeClip2(x.Name)))Console.WriteLine("FreeClip status after endpoint check: "+d.State);
            foreach(var model in NativeDevices.DiagnosticModels())Console.WriteLine("USB peripheral: "+model);
            foreach(var d in receivers) Console.WriteLine(string.Format("USB {0:X4}:{1:X4} | {2} | {3} | {4}",d.Vendor,d.Product,d.Name,Desk.GroupOf(d),d.State));
        } catch(Exception e){Console.WriteLine("Device enumeration unavailable: "+e.GetType().Name+" "+e.Message);}
        try{var choices=Audio.ActiveChoices(Audio.Outputs());
            Console.WriteLine("Selectable render outputs (read only): "+choices.Count+", current in list="+choices.Any(x=>x.Id==Audio.DefaultEndpoint(false)));
            var mics=Audio.ActiveChoices(Audio.Endpoints(true));
            Console.WriteLine("Active recording inputs (read only): "+mics.Count+", current in list="+mics.Any(x=>x.Id==Audio.DefaultEndpoint(true)));
            Console.WriteLine("Default endpoint switch interface available (no change): "+Audio.DefaultPolicyAvailable());
        }catch(Exception e){Console.WriteLine("Audio picker unavailable: "+e.HResult.ToString("X8"));}
        try {foreach(var e in Audio.Outputs().Where(x=>x.Name.IndexOf("freeclip",StringComparison.OrdinalIgnoreCase)>=0))Console.WriteLine("FreeClip playback endpoint state: "+e.State);}catch(Exception e){Console.WriteLine("Endpoint enumeration error: "+e.HResult.ToString("X8"));}
        foreach(bool input in new[]{false,true}) {
            try {
                var endpoint=Audio.Access(input);
                Check(endpoint.Level>=0&&endpoint.Level<=100,"volume bounds");
                Console.WriteLine("PASS Windows "+(input?"recording":"playback")+" volume read (no change)");
            } catch(Exception e){Console.WriteLine("Windows "+(input?"recording":"playback")+" read unavailable: "+e.GetType().Name+" HRESULT="+e.HResult.ToString("X8"));}
        }
    }
    static void GroupAndAudioTests(){
        MixerGroups();
        Check(NativeDevices.KindFromHidIds(new[]{"HID_DEVICE_UP:0001_U:0002","HID_DEVICE_SYSTEM_MOUSE"})=="mouse"&&
              NativeDevices.KindFromHidIds(new[]{"HID_DEVICE_SYSTEM_KEYBOARD"})=="keyboard"&&
              NativeDevices.KindFromHid(1,5)=="gamepad"&&NativeDevices.KindFromHid(12,1)==null,"standard USB categories do not imply battery capability");
        Check(NativeDevices.ReceiverName("Universal 2.4G receiver")&&!NativeDevices.ReceiverName("USB Mechanical Keyboard"),"receiver is not a confirmed peripheral");
        Check(Desk.GroupOf(new Device{Name="brand XYZ",KindHint="mic"})=="mic"&&
              Desk.GroupOf(new Device{Name="ROG Keris"})=="mouse"&&Desk.GroupOf(new Device{Name="unnamed",Link="蓝牙"})=="other","explicit device group then fallback");
        var outList=new[]{new Audio.Endpoint{Id="head",Name="Headphones (HUAWEI FreeClip 2)",State=1},
            new Audio.Endpoint{Id="HEAD",Name="duplicate headset",State=1},new Audio.Endpoint{Id="spk",Name="USB Speakers",State=1},
            new Audio.Endpoint{Id="dead",Name="Unplugged",State=8}};
        var choices=Audio.ActiveChoices(outList);
        Check(choices.Count==2&&choices.All(o=>o.State==1)&&choices.Any(o=>o.Id=="spk"),"playback choices only active unique endpoint IDs");
        var devices=Desk.MergeAudio(new[]{new Device{Id="bt",Name="HUAWEI FreeClip 2",Link="蓝牙",State="已连接",Battery=56},
             new Device{Id="rx",Name="generic receiver",Link="USB 接收器",State="接收器已连接"}},outList,
             new[]{new Audio.Endpoint{Id="mic",Name="USB microphone",State=1}});
        Check(devices.Count==4&&devices.Single(x=>x.Id=="bt").Battery==56&&Desk.GroupOf(devices.Single(x=>x.Id=="bt"))=="headphones"&&
             Desk.GroupOf(devices.Single(x=>x.Id=="audio:mic"))=="mic"&&Desk.VisibleDevices(devices).Count==3,"USB audio and microphone visible without duplicate wireless card or fake receiver");
    }
    static void MixerGroups(){
        var sessions=new[]{
            new Audio.Session{EndpointId="a",InstanceId="one",AppKey="browser",Name="Browser",Level=20,State=1},
            new Audio.Session{EndpointId="a",InstanceId="two",AppKey="browser",Name="Browser",Level=60,Muted=true,State=0},
            new Audio.Session{EndpointId="b",InstanceId="three",AppKey="browser",Name="Browser",Level=40,State=1},
            new Audio.Session{EndpointId="a",InstanceId="four",AppKey="game",Name="Game",Level=80,State=1},
            new Audio.Session{EndpointId="a",InstanceId="expired",AppKey="gone",Name="Gone",State=2}};
        var groups=Audio.GroupSessions(sessions.Concat(new[]{sessions[0]}));
        Check(groups.Count==2,"one row per application across output devices");
        var browser=groups.Single(g=>g.Key=="browser");
        Check(browser.Sessions.Count==2&&browser.Mixed&&browser.Level==40&&!browser.Muted&&browser.Playing&&browser.Sessions.All(s=>s.State==1),
            "merge active streams only, dropping paused, expired and duplicate sessions");
        Check(groups.Single(g=>g.Key=="game").Level==80,"unrelated applications retain their volume");
        Check(Audio.GroupSessions(new[]{sessions[1]}).Count==0,"paused-only application is hidden");
        Check(Audio.GroupSessions(new Audio.Session[0]).Count==0,"empty mixer has no fabricated applications");
    }
    // Integration test uses a silent stream owned by this test process only.
    // It never sets another application's volume or the endpoint master volume.
    internal static void MixerWindows(){
        using(var stream=new MemoryStream()) {
            var writer=new BinaryWriter(stream);int bytes=44100*2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);
            writer.Write(44100);writer.Write(88200);writer.Write((short)2);writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(bytes);writer.Write(new byte[bytes]);writer.Flush();stream.Position=0;
            using(var player=new System.Media.SoundPlayer(stream)) {
                Audio.Session target=null;int original=0;bool muted=false;
                Audio.SessionScan observed=null;
                using(var notification=new System.Threading.AutoResetEvent(false))
                using(var monitor=new Audio.SessionMonitor(scan=>{System.Threading.Interlocked.Exchange(ref observed,scan);notification.Set();})) {
                try {
                    Check(WaitFor(()=>observed!=null,notification,3000)&&observed.Unavailable==0,"notification monitor initializes before playback");
                    var master=Audio.Access(false);player.Load();player.PlayLooping();
                    uint pid=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                    for(int i=0;i<40&&target==null;i++) {
                        System.Threading.Thread.Sleep(100);
                        target=Audio.ReadSessions().Sessions.FirstOrDefault(s=>s.ProcessId==pid&&s.State==1);
                    }
                    Check(target!=null,"live test stream appears after playback begins");
                    original=target.Level;muted=target.Muted;
                    Check(WaitFor(()=>observed.Sessions.Any(s=>s.Key==target.Key&&s.State==1),notification,2000),"new playing session notification arrives without periodic refresh");
                    var before=Audio.ReadSessions();Check(before.Unavailable==0,"all active endpoints and sessions readable");
                    Audio.SetSessions(new[]{target},37,true);
                    var after=Audio.ReadSessions();var updated=after.Sessions.Single(s=>s.Key==target.Key);
                    Check(updated.Level==37&&updated.Muted,"real ISimpleAudioVolume volume and mute write/readback");
                    Check(WaitFor(()=>observed.Sessions.Any(s=>s.Key==target.Key&&s.Level==37&&s.Muted),notification,2000),"live volume and mute notification arrives within two seconds");
                    Audio.SetSessions(new[]{target},0,false);
                    updated=Audio.ReadSessions().Sessions.Single(s=>s.Key==target.Key);
                    Check(updated.Level==0&&!updated.Muted,"zero volume and unmute are independent");
                    Check(WaitFor(()=>observed.Sessions.Any(s=>s.Key==target.Key&&s.Level==0&&!s.Muted),notification,2000),"unmute and zero volume update through notifications");
                    Audio.SetSessions(new[]{target},100,null);
                    Check(Audio.ReadSessions().Sessions.Single(s=>s.Key==target.Key).Level==100,"100 percent boundary");
                    var afterMaster=Audio.Access(false);
                    Check(master.Id==afterMaster.Id&&master.Level==afterMaster.Level&&master.Muted==afterMaster.Muted,"master endpoint volume unchanged");
                    var latest=Audio.ReadSessions();
                    Check(before.Sessions.Where(s=>s.Key!=target.Key).All(s=>!latest.Sessions.Any(t=>t.Key==s.Key&&(t.Level!=s.Level||t.Muted!=s.Muted))),
                        "other live application sessions unchanged");
                    bool staleRejected=false;
                    try{Audio.SetSessions(new[]{new Audio.Session{EndpointId=target.EndpointId,InstanceId="nonexistent-test-session"}},18,null);}
                    catch(InvalidOperationException){staleRejected=true;}
                    Check(staleRejected,"stale session identity rejected instead of changing a different app");
                    int otherCount=before.Sessions.Count(s=>s.Key!=target.Key);
                    Audio.SetSessions(new[]{target},original,muted);
                    player.Stop();
                    Check(WaitFor(()=>!Audio.GroupSessions(observed.Sessions).Any(g=>g.Sessions.Any(s=>s.Key==target.Key)),notification,2000),"stopped playback disappears through state notification");
                    target=null;
                    Console.WriteLine("PASS Windows mixer integration: "+passed+" checks; other sessions observed="+otherCount);
                } finally {
                    try{if(target!=null)Audio.SetSessions(new[]{target},original,muted);}finally{player.Stop();}
                }
                }
            }
        }
    }
    static bool WaitFor(Func<bool> condition,System.Threading.WaitHandle signal,int milliseconds){
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!condition()) {int remaining=milliseconds-(int)timer.ElapsedMilliseconds;if(remaining<=0)return false;signal.WaitOne(Math.Min(remaining,100));}
        return true;
    }

    internal static void Run() {
        try {
            LogitechRetryTests();
            GroupAndAudioTests();
            Check(Protocol.WindowsBattery(3,new byte[]{0})==0,"real zero");
            Check(Protocol.WindowsBattery(3,new byte[]{101})==null,"reject out of range");
            Check(Protocol.WindowsBattery(7,new byte[]{64,0,0,0})==64,"uint Windows reading");
            Check(Protocol.WindowsBattery(18,new byte[]{50})==null,"reject wrong property type");
            Check(Protocol.IsLogitechReceiver(0x046d,0xc547),"approved receiver");
            Check(!Protocol.IsLogitechReceiver(0x1532,0xc547),"reject wrong vendor");
            Check(!Protocol.IsLogitechReceiver(0x046d,0xffff),"reject unknown receiver");
            var f=Protocol.Frame(2,3,0x39,new byte[]{0,3,32});
            Check(f.Length==20 && f[0]==0x11 && f[1]==2 && f[2]==3 && f[3]==0x39 && f[6]==32,"frame");
            Check(Protocol.Response(f,20,2,3,0x39)==1,"match");
            Check(Protocol.Response(f,20,1,3,0x39)==0,"ignore different slot");
            Check(Protocol.Response(f,20,2,3,0x3a)==0,"ignore other transaction");
            Check(Protocol.Response(new byte[]{0x11,2,0xff,3,0x39,5,0},7,2,3,0x39)==-1,"matched device error");
            Check(Protocol.Response(f,5,2,3,0x39)==0,"reject truncated report");
            Check(Protocol.Battery(new byte[]{0,4,0},true)==null,"no exact percentage from flag only");
            Check(Protocol.Battery(new byte[]{80,4,0},true)==80,"unified percent");
            byte[] hx=new byte[62];hx[0]=0x66;hx[1]=0x89;hx[2]=4;hx[4]=63;
            Check(HyperX.IsReceiver(0x03f0,0x05b7)&&!HyperX.IsReceiver(0x03f0,0x9999),"HyperX allowlist");
            Check(HyperX.ParseLevel(hx,hx.Length)==63,"HyperX level");
            hx[2]=0;Check(HyperX.ParseLevel(hx,hx.Length)==-1,"reject HyperX empty reply");
            var request=Fa09.Request(0x4a);
            Check(request[0]==0x13&&request[19]==0x5e,"FA09 battery request checksum");
            Check(!Fa09.Reply(request,20,0x4a),"reject FA09 request echo");
            var reply=new byte[20];reply[0]=0x13;reply[1]=0x4a;reply[2]=1;reply[5]=80;
            Check(Fa09.Reply(reply,20,0x4a)&&!Fa09.Reply(reply,19,0x4a)&&!Fa09.Reply(reply,20,5),"FA09 match valid reply only");
            var headset=new[]{new Audio.Endpoint{Name="Headphones (HUAWEI FreeClip 2)",State=8},
                new Audio.Endpoint{Name="HUAWEI FreeClip2 (Stereo)",State=4}};
            Check(Audio.ConfirmedPlayback("HUAWEI FreeClip2",headset)==false,"AEP online but unplugged render remains offline");
            headset[0].State=1;
            Check(Audio.ConfirmedPlayback("HUAWEI FreeClip2",headset)==true,"active playback endpoint confirms headset");
            Check(Audio.ConfirmedPlayback("Unknown Bluetooth headset",headset)==null,"no unrelated endpoint inference");
            
            using(var small=Desk.LoadAppIcon(16))
            using(var bitmap=small.ToBitmap()){
                Check(small.Width==16&&small.Height==16&&bitmap.GetPixel(0,0).A==0,"embedded icon supports 16px and keeps transparent corners");
                Check(bitmap.GetPixel(8,8).A==255,"embedded icon has an opaque center");
            }
            Check(Protocol.ShortFrame(1,0,0x12,new byte[]{0,0,0x63}).SequenceEqual(new byte[]{0x10,1,0,0x12,0,0,0x63}),"Logitech 7-byte ping");
            var rec=new byte[20];rec[0]=0x11;rec[1]=255;rec[2]=0x83;rec[3]=0xb5;rec[4]=3;
            Check(Protocol.ReceiverInfoReply(rec,20)&&!Protocol.ReceiverInfoReply(rec,7),"receiver diagnostic rejects truncated frame");
            var pairing=(byte[])rec.Clone();pairing[4]=0x20;pairing[7]=0x40;pairing[8]=0xa9;pairing[11]=2;
            Check(Protocol.ReceiverInfoReply(pairing,20,0x20)&&!Protocol.ReceiverInfoReply(pairing,20,0x21)&&Protocol.PairedModel(pairing)=="40A9","paired mouse read is distinct from online state");
            pairing[11]=1;Check(Protocol.PairedModel(pairing)==null,"don't label paired keyboard a mouse");
            Check(Protocol.IsLogitechWired(0x046d,0xc09b)&&!Protocol.IsLogitechWired(0x046d,0xc54d),"wired Logitech and receiver dispatch differ");
            Check(NativeDevices.LogitechLongCaps(0xff00,2,20,20)&&!NativeDevices.LogitechLongCaps(0xff00,1,20,20),"HID++ query cannot select arbitrary report collections");
            Check(NativeDevices.InterfaceKey(@"\\?\hid#vid_046d&pid_c54d&mi_02&col01#7&AB&0&0000#{hid}")==
                  NativeDevices.InterfaceKey(@"\\?\hid#vid_046d&pid_c54d&mi_02&col02#7&AB&0&0001#{hid}"),"short and long are siblings on same USB interface");
            Check(NativeDevices.InterfaceKey(@"\\?\hid#vid_046d&pid_c54d&mi_01&col01#7&AB&0&0000#{hid}")!=
                  NativeDevices.InterfaceKey(@"\\?\hid#vid_046d&pid_c54d&mi_02&col02#7&AB&0&0001#{hid}"),"never pair across USB interfaces");
            Check(NativeDevices.RazerInterface(@"\\?\hid#vid_1532&pid_00b7&mi_00&col01#7&AB&0&0000#{hid}",0x1532,0x00b7)==0,"Razer model-specific interface extraction");
            Check(NativeDevices.RazerInterface(@"\\?\hid#vid_1532&pid_00b7&mi_00&col01#7&AB&0&0000#{hid}",0x1532,0x00c1)<0,"Razer mismatched device path rejected");
            Check(RazerMouse.AllowsCollection(0x1532,0x00b7,0,1,2,91,91)&&
                !RazerMouse.AllowsCollection(0x1532,0x00b7,2,1,2,91,91)&&
                !RazerMouse.AllowsCollection(0x1532,0x00b7,0,1,2,91,90)&&
                !RazerMouse.AllowsCollection(0x1532,0xfffe,0,1,2,91,91),"Razer only documented IDs, interface and report shape");
            var rq=RazerMouse.Frame(0x1f,7,0x80,new byte[]{0,0});
            Check(rq.Length==91&&rq[2]==0x1f&&rq[89]==0x85&&!RazerMouse.ValidReply(rq,91,0x1f,7,0x80,2),"Razer golden battery request is not a response");
            var rr=RazerMouse.Frame(0x3f,7,0x80,new byte[]{0,128});rr[1]=2;
            Check(RazerMouse.ValidReply(rr,91,0x3f,7,0x80,2)&&!RazerMouse.ValidReply(rr,91,0x1f,7,0x80,2)&&
                !RazerMouse.ValidReply(rr,91,0x3f,7,0x84,2)&&!RazerMouse.ValidReply(rr,90,0x3f,7,0x80,2),"Razer matching and report-size checks");
            rr[10]^=1;
            Check(!RazerMouse.ValidReply(rr,91,0x3f,7,0x80,2)&&RazerMouse.Percent(0)==0&&RazerMouse.Percent(255)==100,"reject corrupt battery and retain true zero");
            Check(CompxMouse.IsProfile(0x3554,0xf58a)&&!CompxMouse.IsProfile(0x3554,0xf5d5)&&!CompxMouse.IsProfile(0xffff,0xf58a),"CompX power query limited to known compatible hardware");
            Check(CompxMouse.TransportKind(0x3710,0x5406)=="receiver"&&CompxMouse.NeedsInterfaceOne(0x3710,0x5406)&&
                  CompxMouse.TransportKind(0x3554,0xf58f)=="usb"&&CompxMouse.TransportKind(0x3554,0xf58c)=="unknown","CompX power profile does not guess transport from names");
            Check(NativeDevices.ReceiverOnly(0x0b05,0x1a68)&&NativeDevices.ReceiverOnly(0x0b05,0x1ace)&&
                  !NativeDevices.ReceiverOnly(0x0b05,0x1a66),"keep ROG receivers out of generic wired device cards");
            Check(Desk.DiagnosticLine(new Device{Name="mouse",State="已连接",Battery=0}).Contains("电量=0%")&&
                  !Desk.DiagnosticLine(new Device{Name="mouse",State="未连接",Battery=20}).Contains("20%"),"diagnostics distinguish true zero from stale");
            var cp=CompxMouse.Request();
            Check(CompxMouse.Sum(cp)==0x55&&!CompxMouse.Parse(cp,17).HasValue,"outbound request is not a reply");
            cp[6]=80;cp[7]=1;cp[8]=0x0f;cp[9]=0xad;cp[16]=0;cp[16]=(byte)(0x55-CompxMouse.Sum(cp));
            Check(CompxMouse.Parse(cp,17)==80&&CompxMouse.Parse(cp,16)==null,"CompX live reply and truncation");
            cp[6]^=1;Check(CompxMouse.Parse(cp,17)==null,"reject corrupt CompX reply");
            Console.WriteLine("PASS "+passed+" protocol, devices and icon checks");
        } catch(Exception e) { Console.Error.WriteLine(e.Message);Environment.ExitCode=1; }
    }
    static void LogitechRetryTests(){
        int attempts=0,pauses=0;var formats=new System.Collections.Generic.List<bool>();var valid=new byte[]{3,0,5};
        var result=Logitech.RetryRead(shortReport=>{formats.Add(shortReport);return ++attempts==3?valid:null;},()=>true,true,()=>pauses++);
        Check(object.ReferenceEquals(result,valid)&&attempts==3&&pauses==2&&formats.SequenceEqual(new[]{true,false,true}),"HID++ delayed read succeeds after bounded short/long retries");
        attempts=0;result=Logitech.RetryRead(shortReport=>{attempts++;return null;},()=>false,true,()=>{});
        Check(result==null&&attempts==1,"device rejection or I/O failure is not retried as a timeout");
        attempts=0;result=Logitech.RetryRead(shortReport=>{attempts++;return null;},()=>true,false,()=>{});
        Check(result==null&&attempts==4,"unresponsive device stops after four read attempts");
        Check(Logitech.LiveName("PRO X 2",false,true)==null,"paired or cached model name alone never proves a mouse is online");
        Check(Logitech.LiveName("",true,true)=="Logitech 鼠标"&&Logitech.LiveName("PRO X 2",true,true)=="PRO X 2","live mouse remains visible when its optional model name is temporarily unavailable");
        Logitech.LiveName("PRO X 2",true,true,"test-receiver:1");
        Check(Logitech.LiveName("",true,true,"test-receiver:1")=="PRO X 2"&&Logitech.LiveName("",false,true,"test-receiver:1")==null,
            "previously read model name survives a failed name read but never supplies online status");
        Check(Logitech.LiveName("",true,true,"other-receiver:1")=="Logitech 鼠标", "model-name fallback is isolated by receiver and slot");
    }
}
