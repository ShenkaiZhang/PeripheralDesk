using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program {
    [STAThread] static void Main(string[] args) {
        if(args.Contains("--self-test")) { SelfTest.Run(); return; }
        if(args.Contains("--verify-ui")) { try{SelfTest.Ui();}catch(Exception e){Console.Error.WriteLine(e.GetType().FullName+": "+e.Message);Console.Error.WriteLine(e.StackTrace);Environment.ExitCode=1;} return; }
        if(args.Length==2 && args[0]=="--preview") { Application.EnableVisualStyles(); using(var panel=new Desk(true,true))panel.CapturePreview(args[1]); return; }
        if(args.Length==2 && args[0]=="--mixer-preview") { Application.EnableVisualStyles(); using(var panel=new Desk(true,true))panel.CapturePreview(args[1],true); return; }
        if(args.Length==2 && args[0]=="--tray-preview"){Desk.CaptureTrayPreview(args[1]);return;}
        if(args.Contains("--verify-windows")) { SelfTest.Windows(); return; }
        if(args.Contains("--verify-mixer-windows")) {try{Task.Run(()=>SelfTest.MixerWindows()).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}return;}
        using(var single=new Mutex(false,@"Local\PeripheralDesk.v1")) {
            if(!single.WaitOne(0)) {MessageBox.Show("外设台已运行，请在任务栏右侧打开。","外设台");return;}
            try {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Desk(args.Contains("--demo")));
            } finally {single.ReleaseMutex();}
        }
    }
}
internal sealed class Desk : Form {

    readonly FlowLayoutPanel cards=new FlowLayoutPanel();
    readonly TableLayoutPanel grid=new TableLayoutPanel();
    readonly Label online;
    readonly Button refresh;
    readonly ComboBox playbackPicker=new ComboBox(), microphonePicker=new ComboBox();
    readonly NotifyIcon tray=new NotifyIcon();
    readonly Icon appIcon=LoadAppIcon(32);
    readonly Icon trayIcon=LoadAppIcon(SystemInformation.SmallIconSize.Width);
    readonly System.Windows.Forms.Timer deviceTimer=new System.Windows.Forms.Timer(), audioTimer=new System.Windows.Forms.Timer();
    readonly VolumeRow output,input;
    readonly Label foot;
    readonly MixerPanel mixer;
    readonly Button devicesPage,appsPage;
    bool showingMixer;
    int deviceDeckHeight=68;
    string deviceStatus="等待首次扫描";
    readonly bool demo, silent;
    bool busy, closing, anchored, audioBusy;
    List<Device> current=new List<Device>();

    public Desk(bool demoMode,bool silentMode=false) {
        demo=demoMode;silent=silentMode;
        Text="外设台 · Peripheral Desk";ClientSize=new Size(350,430);
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;Icon=appIcon;
        StartPosition=FormStartPosition.Manual; anchored=!silent;
        BackColor=Design.Canvas;Font=Design.Font(9);ForeColor=Design.Ink;AutoScaleMode=AutoScaleMode.None;KeyPreview=true;

        grid.Dock=DockStyle.Fill;grid.Padding=new Padding(15,11,15,10);grid.ColumnCount=1;grid.RowCount=10;grid.BackColor=Design.Canvas;
        foreach(int h in new[]{49,28,68,29,32,32,60,60,26,36})grid.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        Controls.Add(grid);
        var header=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
        var brand=new Panel{Bounds=new Rectangle(0,7,34,34)};
        brand.Paint+=(s,e)=>e.Graphics.DrawIcon(appIcon,new Rectangle(0,0,34,34));header.Controls.Add(brand);
        var title=new Label{Text="外设台",Font=Design.Font(15,true),Bounds=new Rectangle(43,3,145,27),TextAlign=ContentAlignment.MiddleLeft};header.Controls.Add(title);
        var kicker=new Label{Text="PERIPHERAL DESK",Font=new Font("Segoe UI",7.5f,FontStyle.Bold),ForeColor=Design.Muted,Bounds=new Rectangle(46,29,145,13)};header.Controls.Add(kicker);
        refresh=Design.Button("↻",31,30);refresh.Bounds=new Rectangle(250,8,31,30);refresh.AccessibleName="立即扫描";refresh.Click+=async (s,e)=>{if(showingMixer)await mixer.RefreshSessions();else await Reload();};header.Controls.Add(refresh);
        var more=Design.Button("···",31,30);more.Bounds=new Rectangle(289,8,31,30);more.AccessibleName="更多操作";header.Controls.Add(more);grid.Controls.Add(header,0,0);

        online=new Label{Text="已连接设备",Dock=DockStyle.Fill,ForeColor=Design.Muted,Font=Design.Font(8.5f,true),TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty};
        grid.Controls.Add(online,0,1);
        cards.Dock=DockStyle.Fill;cards.FlowDirection=FlowDirection.TopDown;cards.WrapContents=false;cards.AutoScroll=true;cards.Margin=Padding.Empty;
        cards.Resize+=(s,e)=>ResizeTiles();grid.Controls.Add(cards,0,2);

        grid.Controls.Add(new Label{Text="声音",Font=Design.Font(8.5f,true),ForeColor=Design.Muted,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,3);
        output=new VolumeRow("耳机 / 扬声器",false,demo);
        input=new VolumeRow("麦克风",true,demo);
        grid.Controls.Add(AudioPickerRow(playbackPicker,false),0,4);
        grid.Controls.Add(AudioPickerRow(microphonePicker,true),0,5);
        grid.Controls.Add(output,0,6);grid.Controls.Add(input,0,7);
        foot=new Label{Dock=DockStyle.Fill,ForeColor=Design.Muted,Font=Design.Font(8),TextAlign=ContentAlignment.MiddleLeft,Text="等待首次扫描",AutoEllipsis=true,Margin=Padding.Empty};
        grid.Controls.Add(foot,0,8);
        mixer=new MixerPanel(demo){Visible=false};grid.Controls.Add(mixer,0,1);grid.SetRowSpan(mixer,7);
        var navigation=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        devicesPage=Design.Button("1 / 2  外设",150,30);appsPage=Design.Button("2 / 2  应用音量",150,30);
        devicesPage.Dock=appsPage.Dock=DockStyle.Fill;devicesPage.Margin=new Padding(0,3,4,0);appsPage.Margin=new Padding(4,3,0,0);
        devicesPage.Click+=(s,e)=>SelectPage(false);appsPage.Click+=(s,e)=>SelectPage(true);
        navigation.Controls.Add(devicesPage,0,0);navigation.Controls.Add(appsPage,1,0);grid.Controls.Add(navigation,0,9);
        SelectPage(false);
        VisibleChanged+=(s,e)=>{if(!Visible)mixer.CancelPending();};
        tray.Icon=trayIcon;tray.Text="外设台";tray.Visible=!silent;tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)ToggleDesk();};
        var menu=new ContextMenuStrip();menu.Items.Add("打开外设台",null,(s,e)=>ShowDesk());
        menu.Items.Add("立即扫描",null,async (s,e)=>await Reload());
        menu.Items.Add("复制兼容性诊断",null,(s,e)=>Copy());
        menu.Items.Add("Windows 声音设置",null,(s,e)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound"){UseShellExecute=true});}catch(Exception){foot.Text="无法打开 Windows 声音设置";}});
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("收起面板",null,(s,e)=>Hide());menu.Items.Add("退出外设台",null,(s,e)=>Exit());
        tray.ContextMenuStrip=menu;more.Click+=(s,e)=>menu.Show(more,0,more.Height);
        deviceTimer.Interval=30000;deviceTimer.Tick+=async (s,e)=>{await Reload();};
        audioTimer.Interval=2500;audioTimer.Tick+=(s,e)=>{if(Visible&&!showingMixer)SyncSound();};
        if(!silent)Shown+=async (s,e)=>{SyncSound();deviceTimer.Start();audioTimer.Start();await Reload();};
        KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){Hide();e.Handled=true;}};
        SizeChanged+=(s,e)=>Design.RoundControl(this,17);
        RenderCards();
        Design.RoundControl(this,17);PlaceByTray();
    }
    void ToggleDesk(){if(Visible&&ContainsFocus)Hide();else ShowDesk();}
    void SelectPage(bool apps){
        showingMixer=apps;mixer.CancelPending();
        grid.SuspendLayout();
        foreach(Control control in grid.Controls){int row=grid.GetRow(control);if(row>=1&&row<=7&&control!=mixer)control.Visible=!apps;}
        mixer.Visible=apps;
        devicesPage.BackColor=apps?Design.BlueWash:Design.Accent;devicesPage.ForeColor=apps?Design.Accent:Color.White;
        appsPage.BackColor=apps?Design.Accent:Design.BlueWash;appsPage.ForeColor=apps?Color.White:Design.Accent;
        devicesPage.AccessibleDescription=apps?"切换到第一页":"当前页";appsPage.AccessibleDescription=apps?"当前页":"切换到第二页";
        refresh.AccessibleName=apps?"刷新应用音量":"立即扫描";
        foot.Text=apps?"音量实时同步 · 仅显示正在播放的应用":deviceStatus;
        LayoutPage();grid.ResumeLayout();if(apps)RefreshMixer();
    }
    void LayoutPage(){
        if(showingMixer&&grid.RowStyles[1].Height==0&&grid.RowStyles[2].Height>0)return;
        int[] heights={28,deviceDeckHeight,29,32,32,60,60};
        for(int i=1;i<=7;i++)grid.RowStyles[i].Height=showingMixer?0:heights[i-1];
        if(showingMixer){
            int limit=silent?450:Math.Max(180,Math.Min(450,Screen.FromPoint(Cursor.Position).WorkingArea.Height-160));
            grid.RowStyles[2].Height=Math.Min(400,limit);
        }
        ResizePanel();
    }
    async void RefreshMixer(){await mixer.RefreshSessions();}
    void ShowDesk(){anchored=true;PlaceByTray();Show();Activate();SyncSound();}
    void PlaceByTray(){if(silent||!anchored)return;var a=Screen.FromPoint(Cursor.Position).WorkingArea;Location=new Point(a.Right-Width-13,a.Bottom-Height-12);}
    void ResizeTiles(){
        int width=Math.Max(225,cards.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);
        foreach(Control tile in cards.Controls)tile.Width=width;
    }
    internal static List<Device> VisibleDevices(IEnumerable<Device> devices){return devices.Where(d=>d.State=="已连接").ToList();}
    static readonly string[] GroupOrder={"mouse","keyboard","headphones","mic","gamepad","other"};
    static string GroupTitle(string kind){
        switch(kind){case "mouse":return "鼠标";case "keyboard":return "键盘";case "headphones":return "耳机 / 扬声器";
            case "mic":return "麦克风";case "gamepad":return "手柄";default:return "其他外设";}
    }
    internal static string GroupOf(Device d){
        var k=Design.Kind(d);return GroupOrder.Contains(k)?k:"other";
    }
    static string PickerLabel(bool capture){return capture?"麦克风设备":"播放设备";}
    Control AudioPickerRow(ComboBox picker,bool capture){
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,3),ColumnCount=2,RowCount=1};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,76));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        row.Controls.Add(new Label{Text=PickerLabel(capture),Dock=DockStyle.Fill,Margin=Padding.Empty,
            TextAlign=ContentAlignment.MiddleLeft,Font=Design.Font(8.5f,true),ForeColor=Design.Muted},0,0);
        picker.DropDownStyle=ComboBoxStyle.DropDownList;picker.Dock=DockStyle.Fill;picker.Margin=new Padding(0,2,0,0);
        picker.DropDownWidth=360;picker.AccessibleName="切换 Windows 默认"+PickerLabel(capture);picker.Enabled=false;
        picker.SelectionChangeCommitted+=async(s,e)=>await SwitchAudio(capture);
        row.Controls.Add(picker,1,0);return row;
    }
    static List<Audio.Endpoint> DemoEndpoints(bool capture){
        return capture?new List<Audio.Endpoint>{
            new Audio.Endpoint{Id="demo-mic",Name="示例桌面麦克风",State=1},
            new Audio.Endpoint{Id="demo-mic-usb",Name="示例 USB 麦克风",State=1},
            new Audio.Endpoint{Id="demo-mic-off",Name="示例已停用麦克风",State=2}}:
            new List<Audio.Endpoint>{
            new Audio.Endpoint{Id="demo-speaker",Name="示例扬声器",State=1},
            new Audio.Endpoint{Id="demo-headset",Name="示例 USB 耳机",State=1}};
    }
    string demoPlayback="demo-speaker",demoCapture="demo-mic";
    void SyncSound(){
        if(showingMixer)RefreshMixer();
        if(audioBusy)return;
        SyncAudio(false);SyncAudio(true);
    }
    void SyncAudio(bool capture){
        var picker=capture?microphonePicker:playbackPicker;
        var volume=capture?input:output;
        if(!picker.DroppedDown){
            var choices=new List<Audio.Endpoint>();string selected=null;
            try{
                choices=Audio.ActiveChoices(demo?DemoEndpoints(capture):Audio.Endpoints(capture));
                selected=demo?(capture?demoCapture:demoPlayback):Audio.DefaultEndpoint(capture);
            }catch(Exception){choices.Clear();}
            picker.AccessibleDescription=choices.Count>0?"":"没有可用的"+PickerLabel(capture);
            if(!picker.Items.Cast<Audio.Endpoint>().Select(x=>x.Id+"\n"+x.Name)
                .SequenceEqual(choices.Select(x=>x.Id+"\n"+x.Name))||
                !string.Equals((picker.SelectedItem as Audio.Endpoint??new Audio.Endpoint()).Id,selected,StringComparison.OrdinalIgnoreCase)){
                picker.BeginUpdate();
                try{
                    picker.Items.Clear();
                    foreach(var item in choices)picker.Items.Add(item);
                    picker.SelectedIndex=choices.FindIndex(e=>string.Equals(e.Id,selected,StringComparison.OrdinalIgnoreCase));
                }finally{picker.EndUpdate();}
            }
            picker.Enabled=choices.Count>0;
        }
        var demoChoice=picker.SelectedItem as Audio.Endpoint;
        volume.Sync(demo?demoChoice:null);
    }
    async Task SwitchAudio(bool capture){
        var picker=capture?microphonePicker:playbackPicker;
        var volume=capture?input:output;
        var next=picker.SelectedItem as Audio.Endpoint;
        if(next==null||audioBusy)return;
        audioBusy=true;playbackPicker.Enabled=microphonePicker.Enabled=false;volume.CancelPending();volume.Enabled=false;
        try{
            if(demo){
                if(capture)demoCapture=next.Id;else demoPlayback=next.Id;
                foot.Text="示例"+PickerLabel(capture)+"："+next.Name;
            }else {
                await Task.Run(()=>Audio.SetDefault(capture,next.Id));
                if(!closing)foot.Text="已切换默认"+PickerLabel(capture)+"："+next.Name;
            }
        }catch(Exception){
            if(!closing)foot.Text="未确认切换成功；可在 ··· → Windows 声音设置中切换";
        }finally{
            audioBusy=false;
            if(!closing){volume.Enabled=true;SyncSound();}
        }
    }
    internal static List<Device> MergeAudio(IEnumerable<Device> physical,IEnumerable<Audio.Endpoint> outList,IEnumerable<Audio.Endpoint> inList) {
        var existing=physical.ToList();
        foreach(bool input in new[]{false,true}){
            foreach(var e in Audio.ActiveChoices(input?inList:outList)){
                var kind=input?"mic":"headphones";
                var match=!input?existing.FirstOrDefault(d=>d.State=="已连接"&&!string.IsNullOrEmpty(d.Id)&&!d.Id.StartsWith("audio:")&&
                    (d.Link.Contains("蓝牙")||GroupOf(d)=="headphones")&&Audio.EndpointMatches(d.Name,e.Name)):null;
                if(match!=null){match.KindHint="headphones";continue;}
                existing.Add(new Device{Id="audio:"+e.Id,Name=e.Name,KindHint=kind,Link="系统音频",State="已连接",
                    Note="Windows 报告该音频端点可用",BatteryNotApplicable=true});
            }
        }
        return existing;
    }

    void RenderCards(){
        var live=VisibleDevices(current);
        online.Text="已连接设备   "+live.Count;
        var old=cards.Controls.Cast<Control>().ToList();
        cards.SuspendLayout();
        try{
            cards.Controls.Clear();foreach(var c in old)c.Dispose();
            if(live.Count==0){
                var empty=new Label{Text="暂无已确认在线的外设",Height=57,Font=Design.Font(9),ForeColor=Design.Muted,BackColor=Design.Card,TextAlign=ContentAlignment.MiddleCenter,Margin=new Padding(0,0,0,7)};
                cards.Controls.Add(empty);empty.Resize+=(s,e)=>Design.RoundControl(empty,11);
            }else foreach(var group in live.GroupBy(GroupOf).OrderBy(g=>Array.IndexOf(GroupOrder,g.Key))){
                var heading=new Label{Text=GroupTitle(group.Key)+"  "+group.Count(),AccessibleRole=AccessibleRole.StaticText,Height=25,
                    Margin=new Padding(0,3,0,0),Font=Design.Font(9,true),ForeColor=Design.Muted,TextAlign=ContentAlignment.MiddleLeft};
                cards.Controls.Add(heading);
                foreach(var d in group){
                    cards.Controls.Add(new DeviceTile(d){Margin=new Padding(0,0,0,7)});
                }
            }
            var maxDeck=silent?330:Math.Max(100,Math.Min(296,Screen.FromPoint(Cursor.Position).WorkingArea.Height-378)); deviceDeckHeight=Math.Max(67,Math.Min(maxDeck,live.Count*69+live.Select(GroupOf).Distinct().Count()*28));
        }finally{cards.ResumeLayout();}
        LayoutPage();ResizeTiles();
    }
    internal bool VerifyDemoRouting(){
        if(!demo||!silent)return false;
        SyncSound();
        if(playbackPicker.Items.Count!=2||microphonePicker.Items.Count!=2||
           output.CurrentId!="demo-speaker"||input.CurrentId!="demo-mic")return false;
        microphonePicker.SelectedIndex=microphonePicker.Items.Cast<Audio.Endpoint>().ToList().FindIndex(x=>x.Id=="demo-mic-usb");
        var mic=SwitchAudio(true);
        if(!mic.IsCompleted||mic.IsFaulted||demoCapture!="demo-mic-usb"||demoPlayback!="demo-speaker"||
           input.CurrentId!="demo-mic-usb"||output.CurrentId!="demo-speaker")return false;
        playbackPicker.SelectedIndex=playbackPicker.Items.Cast<Audio.Endpoint>().ToList().FindIndex(x=>x.Id=="demo-headset");
        var sound=SwitchAudio(false);
        return sound.IsCompleted&&!sound.IsFaulted&&demoPlayback=="demo-headset"&&demoCapture=="demo-mic-usb"&&
            output.CurrentId=="demo-headset"&&input.CurrentId=="demo-mic-usb";
    }
    internal bool VerifyMixerPage(){
        string playback=output.CurrentId,microphone=input.CurrentId;
        SelectPage(true);bool ok=showingMixer&&!cards.Visible&&mixer.VerifyDemo();
        SelectPage(false);return ok&&!showingMixer&&output.CurrentId==playback&&input.CurrentId==microphone;
    }
    internal void CapturePreview(string path,bool apps=false) {
        current=Demo();RenderCards();SyncSound();
        if(cards.Controls.OfType<DeviceTile>().Count()!=4)throw new InvalidOperationException("Demo card filter failed");
        foot.Text="示例数据    ·    每 30 秒刷新";
        if(apps)SelectPage(true);
        Location=new Point(-20000,-20000);Show();Application.DoEvents();grid.PerformLayout();PerformLayout();ResizeTiles();
        using(var preview=new Bitmap(ClientSize.Width,ClientSize.Height)) {
            DrawToBitmap(preview,new Rectangle(Point.Empty,ClientSize));preview.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    void Exit(){closing=true;Close();}
    protected override void OnFormClosing(FormClosingEventArgs e) {
        if(!closing&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();return;}
        closing=true;deviceTimer.Stop();audioTimer.Stop();tray.Visible=false;base.OnFormClosing(e);
    }
    static List<Device> Demo() {
        return new List<Device>{
            new Device{Id="demo1",Link="2.4G · Logitech",Name="G 系列无线鼠标",State="已连接",Battery=76,Note="仅演示，不访问硬件"},
            new Device{Id="demo2",Link="蓝牙",Name="蓝牙降噪耳机",State="已连接",Battery=34,Note="Windows 提供电量"},
            new Device{Id="demo-usb-k",Link="USB",Name="USB 机械键盘",KindHint="keyboard",State="已连接",BatteryNotApplicable=true},
            new Device{Id="demo-mic",Link="系统音频",Name="USB 麦克风",KindHint="mic",State="已连接",BatteryNotApplicable=true},
            new Device{Id="demo3",Link="蓝牙",Name="示例键盘",State="未连接",Note="不显示历史电量"},
            new Device{Id="demo4",Link="USB 接收器",Name="示例未知接收器",State="接收器已连接",Note="外设状态和电量未知"}};
    }
    sealed class ScanResult {
        public readonly List<Device> Devices=new List<Device>();
        public readonly List<string> Errors=new List<string>();
    }
    ScanResult Scan() {
        var r=new ScanResult();
        if(demo){r.Devices.AddRange(Demo());return r;}
        try {r.Devices.AddRange(NativeDevices.Bluetooth());}
        catch(Exception e){r.Errors.Add("蓝牙："+e.Message);}
        try {foreach(var x in NativeDevices.Receivers()){
            if(x.Path==null)r.Devices.Add(x);
            else if(Fa09.IsReceiver(x.Vendor,x.Product))r.Devices.Add(Fa09.Read(x));
            else if(HyperX.IsReceiver(x.Vendor,x.Product))r.Devices.Add(HyperX.Read(x));
            else if(RazerMouse.Supports(x.Vendor,x.Product))r.Devices.Add(RazerMouse.ReadDevice(x));
            else if(CompxMouse.IsProfile(x.Vendor,x.Product))r.Devices.Add(CompxMouse.Read(x));
            else if(Protocol.IsLogitechEndpoint(x.Vendor,x.Product))r.Devices.AddRange(Logitech.Read(x));
            else r.Devices.Add(x);
        }}
        catch(Exception e){r.Errors.Add("USB / 接收器："+e.Message);}
        try{var merged=MergeAudio(r.Devices,Audio.Outputs(),Audio.Endpoints(true));r.Devices.Clear();r.Devices.AddRange(merged);}
        catch(Exception e){r.Errors.Add("音频："+e.Message);}
        return r;
    }
    async Task Reload() {
        if(busy||closing)return;
        busy=true;refresh.Enabled=false;if(!showingMixer)foot.Text="正在扫描设备…";
        try {
            var result=await Task.Run(()=>Scan());
            if(closing||IsDisposed)return;
            current=result.Devices.OrderByDescending(d=>d.State=="已连接").ThenBy(d=>d.Link).ThenBy(d=>d.Name).ToList();
            RenderCards();
            UpdateTray();
            deviceStatus=(demo?"演示数据":("本次扫描 "+DateTime.Now.ToString("HH:mm")+" · 每 30 秒刷新"))+
                (result.Errors.Count>0?" · 部分数据未取得："+string.Join("；",result.Errors):" · 未知不等于没电");
            if(!showingMixer)foot.Text=deviceStatus;
        } catch(Exception e){if(!closing&&!showingMixer)foot.Text="扫描失败："+e.Message;}
        finally {busy=false;if(!closing){refresh.Enabled=true;}}
    }
    void ResizePanel(){
        int height=grid.Padding.Vertical+grid.RowStyles.Cast<RowStyle>().Sum(r=>(int)r.Height);
        if(ClientSize.Height!=height){ClientSize=new Size(350,height);PlaceByTray();}
    }
    internal static string DiagnosticLine(Device d) {
        return string.Format("{0} | {1} | {2} | {3} | 电量={4} | {5}",d.Name,d.Link,
            d.Vendor==0?"Windows":string.Format("{0:X4}:{1:X4}",d.Vendor,d.Product),d.State,
            d.State=="已连接"&&d.Battery.HasValue?d.Battery.Value+"%":"—",d.Note);
    }
    void Copy() {
        var lines=new List<string>{"PeripheralDesk 兼容性清单（不含硬件路径、序列号或蓝牙地址）"};
        lines.AddRange(current.Select(DiagnosticLine));
        try{Clipboard.SetText(string.Join(Environment.NewLine,lines));foot.Text="已复制型号、VID/PID 和状态，未包含设备地址。";}
        catch(Exception e){foot.Text="无法复制："+e.Message;}
    }
    protected override void Dispose(bool disposing){
        if(disposing){tray.Dispose();deviceTimer.Dispose();audioTimer.Dispose();}
        base.Dispose(disposing);
        if(disposing){appIcon.Dispose();trayIcon.Dispose();}
    }
    void UpdateTray() { tray.Text="外设台 · 已连接 "+VisibleDevices(current).Count+" 台"; }
    internal static Icon LoadAppIcon(int size){
        using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PeripheralDesk.AppIcon"))
            return new Icon(stream,size,size);
    }
    internal static void CaptureTrayPreview(string path) {
        using(var icon=LoadAppIcon(40))
        using(var smallIcon=LoadAppIcon(16))
        using(var b=new Bitmap(220,80))
        using(var g=Graphics.FromImage(b)){
            g.Clear(Color.FromArgb(241,244,248));
            using(var shade=new SolidBrush(Color.FromArgb(29,31,35)))g.FillRectangle(shade,110,0,110,80);
            foreach(int x in new[]{18,128}){
                g.DrawIcon(smallIcon,new Rectangle(x,32,16,16));g.DrawIcon(icon,new Rectangle(x+33,20,40,40));
            }
            b.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
internal static class GraphicsEx {
    internal static void FillRounded(this Graphics g,Brush b,Rectangle r,int radius) {
        using(var p=new System.Drawing.Drawing2D.GraphicsPath()){
            int d=radius*2;
            p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();g.FillPath(b,p);
        }
    }
}

internal sealed class VolumeRow : Panel {
    readonly bool input,demo;
    readonly Label caption,level;
    readonly AudioSlider slider=new AudioSlider();
    readonly Button mute;
    readonly System.Windows.Forms.Timer commit=new System.Windows.Forms.Timer();
    readonly string section;
    Audio.Snapshot snapshot;
    bool update;
    string failure;
    public VolumeRow(string title,bool isInput,bool demoMode) {
        input=isInput;demo=demoMode;section=title;Dock=DockStyle.Fill;BackColor=Design.Card;
        Margin=new Padding(0,0,0,6);Padding=new Padding(13,3,10,3);
        Resize+=(s,e)=>Design.RoundControl(this,12);
        var t=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=4,Margin=Padding.Empty};
        t.RowStyles.Add(new RowStyle(SizeType.Absolute,19));t.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,25));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,45));t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,54));
        Controls.Add(t);
        caption=new Label{Dock=DockStyle.Fill,ForeColor=Design.Muted,Font=Design.Font(8),AutoEllipsis=true,Text=title,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(1,0,0,0)};
        t.Controls.Add(caption,0,0);t.SetColumnSpan(caption,4);
        var ico=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};ico.Paint+=(s,e)=>{e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;Design.Glyph(e.Graphics,input?"mic":"headphones",9,ico.Height/2,Design.Accent);};
        t.Controls.Add(ico,0,1);
        slider.Dock=DockStyle.Fill;slider.Margin=Padding.Empty;slider.AccessibleName=title+"音量";
        slider.Changed+=(s,e)=>{level.Text=slider.Value+"%";if(!update&&snapshot!=null){commit.Stop();commit.Start();}};
        t.Controls.Add(slider,1,1);
        level=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Text="—",Font=Design.Font(8.75f,true),Margin=Padding.Empty};t.Controls.Add(level,2,1);
        mute=Design.Button("静音",53,23);mute.Margin=new Padding(1,0,0,1);mute.Dock=DockStyle.Fill;
        mute.Click+=(s,e)=>{if(snapshot!=null)Apply(null,!snapshot.Muted);};t.Controls.Add(mute,3,1);
        commit.Interval=230;commit.Tick+=(s,e)=>{if(slider.Capture)return;commit.Stop();Apply(slider.Value,null);};
    }
    internal void CancelPending(){commit.Stop();}
    internal string CurrentId{get{return snapshot==null?null:snapshot.Id;}}
    internal void Sync(Audio.Endpoint demoEndpoint) {
        if(commit.Enabled)return;
        try {
            if(!demo)snapshot=Audio.Access(input);
            else if(demoEndpoint==null)snapshot=null;
            else if(snapshot==null||snapshot.Id!=demoEndpoint.Id)snapshot=new Audio.Snapshot{Id=demoEndpoint.Id,Name=demoEndpoint.Name,Level=input?62:39};
        } catch {snapshot=null;}
        Render();
    }
    void Apply(int? val,bool? toggle) {
        if(snapshot==null)return;
        try {
            if(demo){if(val.HasValue)snapshot.Level=val.Value;if(toggle.HasValue)snapshot.Muted=toggle.Value;}
            else snapshot=Audio.Access(input,snapshot.Id,val,toggle);
        } catch(Exception e){failure=e.Message;}
        Render();
    }
    void Render() {
        update=true;
        try {
            caption.Text=section+" · "+(snapshot==null?"设备不可用":string.IsNullOrEmpty(snapshot.Name)?"Windows 默认设备":snapshot.Name)
                +(failure==null?"":" · 上次控制未确认");
            slider.Enabled=mute.Enabled=snapshot!=null;
            if(snapshot!=null){slider.Value=Math.Max(0,Math.Min(100,snapshot.Level));level.Text=slider.Value+"%";mute.Text=snapshot.Muted?"取消静音":"静音";}
            else level.Text="—";
            failure=null;
        }finally{update=false;}
    }
    protected override void Dispose(bool disposing){if(disposing)commit.Dispose();base.Dispose(disposing);}
}
