using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MixerPanel : Panel {
    readonly Label status=new Label();
    sealed class StableRows : FlowLayoutPanel {internal StableRows(){DoubleBuffered=true;}}
    readonly FlowLayoutPanel rows=new StableRows();
    readonly bool demo;
    readonly List<Audio.Session> examples;
    Audio.SessionMonitor monitor;
    Audio.SessionScan latest;
    internal int AppCount {get{return rows.Controls.Count;}}
    internal MixerPanel(bool demoMode) {
        demo=demoMode;Dock=DockStyle.Fill;Margin=Padding.Empty;DoubleBuffered=true;
        examples=new List<Audio.Session>();
        string[] names={"Microsoft Edge","音乐播放器","游戏"};
        for(int i=0;i<names.Length;i++)examples.Add(new Audio.Session{EndpointId="demo",EndpointName="耳机 / 扬声器",
            InstanceId="demo-"+i,AppKey=names[i],Name=names[i],Level=new[]{65,40,80}[i],State=1});
        status.Text="播放声音后，应用会自动出现在这里";status.Height=46;status.Dock=DockStyle.Top;
        status.ForeColor=Design.Muted;status.Font=Design.Font(8);status.TextAlign=ContentAlignment.MiddleLeft;
        rows.Dock=DockStyle.Fill;rows.FlowDirection=FlowDirection.TopDown;rows.WrapContents=false;rows.AutoScroll=true;
        rows.Resize+=(s,e)=>ResizeRows();Controls.Add(rows);Controls.Add(status);
        VisibleChanged+=(s,e)=>{if(Visible&&IsHandleCreated){if(latest!=null)Accept(latest);EnsureMonitor();}};
        HandleCreated+=(s,e)=>{if(Visible)EnsureMonitor();};
    }
    void ResizeRows(){int width=Math.Max(220,rows.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);foreach(Control row in rows.Controls)if(row.Width!=width)row.Width=width;}
    internal void CancelPending(){foreach(var row in rows.Controls.OfType<AppVolumeRow>())row.CancelPending();}
    void EnsureMonitor(){
        if(demo||monitor!=null||!IsHandleCreated||IsDisposed)return;
        monitor=new Audio.SessionMonitor(scan=>{
            if(IsDisposed||!IsHandleCreated)return;
            try{BeginInvoke((Action)(()=>{if(IsDisposed)return;latest=scan;if(Visible)Accept(scan);}));}
            catch(InvalidOperationException){}
        });
    }
    internal Task RefreshSessions() {
        if(IsDisposed)return Task.FromResult(0);
        if(demo){var scan=new Audio.SessionScan();scan.Sessions.AddRange(examples);Accept(scan);}
        else{EnsureMonitor();if(monitor!=null)monitor.RequestRefresh();}
        return Task.FromResult(0);
    }
    void Accept(Audio.SessionScan scan){
        if(rows.Controls.OfType<AppVolumeRow>().Any(r=>r.Editing))return;
        // A transient read failure must not clear and recreate the existing list.
        if(scan.Unavailable==0)Render(Audio.GroupSessions(scan.Sessions));
        string message=scan.Unavailable>0?"部分应用暂不可用，正在恢复连接":
            rows.Controls.Count==0?"暂无正在播放的应用\n请先在浏览器、音乐软件或游戏中播放声音":
            (demo?"示例数据 · ":"")+"仅显示正在播放的应用\n同一软件的声音合并调节，音量实时同步";
        if(status.Text!=message)status.Text=message;
    }
    void Render(List<Audio.AppVolume> apps) {
        var old=rows.Controls.OfType<AppVolumeRow>().ToDictionary(r=>r.Key);
        var keys=new HashSet<string>(apps.Select(a=>a.Key));
        bool structure=old.Count!=apps.Count||keys.Any(k=>!old.ContainsKey(k));
        Point scroll=rows.AutoScrollPosition;
        if(structure)rows.SuspendLayout();
        try {
            foreach(var row in old.Values.Where(r=>!keys.Contains(r.Key)).ToList()){rows.Controls.Remove(row);row.Dispose();}
            for(int i=0;i<apps.Count;i++) {
                var app=apps[i];AppVolumeRow row;
                if(!old.TryGetValue(app.Key,out row)) {
                    row=new AppVolumeRow(app,Apply);row.EditFinished+=()=>{if(monitor!=null)monitor.RequestRefresh();};rows.Controls.Add(row);
                } else row.Sync(app);
                // Existing controls stay in place. New applications append without moving focused rows.
            }
        } finally{if(structure){rows.ResumeLayout();ResizeRows();rows.AutoScrollPosition=new Point(-scroll.X,-scroll.Y);}}
    }
    async Task Apply(Audio.AppVolume app,int? level,bool? mute) {
        if(demo){foreach(var s in app.Sessions){if(level.HasValue)s.Level=level.Value;if(mute.HasValue)s.Muted=mute.Value;}return;}
        await Task.Run(()=>Audio.SetSessions(app.Sessions,level,mute));
    }
    internal bool VerifyDemo() {
        if(!demo)return false;
        RefreshSessions().GetAwaiter().GetResult();
        var list=rows.Controls.OfType<AppVolumeRow>().ToList();
        if(list.Count!=3)return false;
        var first=list[0];var other=examples.Where(s=>s.AppKey!=first.Key).Select(s=>s.Level).ToArray();
        first.VerifyApply(23,true).GetAwaiter().GetResult();
        return examples.Single(s=>s.AppKey==first.Key).Level==23&&
            examples.Single(s=>s.AppKey==first.Key).Muted&&
            examples.Where(s=>s.AppKey!=first.Key).Select(s=>s.Level).SequenceEqual(other);
    }
    internal bool VerifyStableUpdates(){
        RefreshSessions().GetAwaiter().GetResult();
        var original=rows.Controls.Cast<Control>().ToArray();int layouts=0;
        LayoutEventHandler count=(s,e)=>layouts++;rows.Layout+=count;
        for(int i=0;i<8;i++)RefreshSessions().GetAwaiter().GetResult();
        rows.Layout-=count;
        if(layouts!=0||!original.SequenceEqual(rows.Controls.Cast<Control>()))return false;
        rows.Layout+=count;
        examples[0].Level=17;RefreshSessions().GetAwaiter().GetResult();
        rows.Layout-=count;
        if(layouts!=0||!original.SequenceEqual(rows.Controls.Cast<Control>())||rows.Controls.OfType<AppVolumeRow>().Single(r=>r.Key==examples[0].AppKey).DisplayedLevel!=17)return false;
        var removed=rows.Controls.OfType<AppVolumeRow>().Single(r=>r.Key==examples[1].AppKey);
        examples[1].State=0;RefreshSessions().GetAwaiter().GetResult();
        if(rows.Controls.Count!=2||rows.Controls.OfType<AppVolumeRow>().Any(r=>r.Key==examples[1].AppKey))return false;
        return removed.IsDisposed&&original.Where(c=>c!=removed).All(c=>!c.IsDisposed);
    }
    protected override void Dispose(bool disposing){if(disposing&&monitor!=null)monitor.Dispose();base.Dispose(disposing);}
}

internal sealed class AppVolumeRow : Panel {
    Audio.AppVolume app;
    readonly Label title,detail,level;
    readonly AudioSlider slider=new AudioSlider();
    readonly Button mute;
    readonly Timer commit=new Timer();
    readonly ToolTip tip=new ToolTip();
    readonly Func<Audio.AppVolume,int?,bool?,Task> apply;
    bool updating,writing;
    internal event Action EditFinished;
    string rendered;
    internal string Key {get{return app.Key;}}
    internal bool Editing {get{return writing||commit.Enabled||slider.Capture;}}
    internal int DisplayedLevel {get{return slider.Value;}}
    internal AppVolumeRow(Audio.AppVolume model,Func<Audio.AppVolume,int?,bool?,Task> writer) {
        apply=writer;app=model;Height=94;BackColor=Design.Card;Margin=new Padding(0,0,0,8);Padding=new Padding(11,6,9,6);DoubleBuffered=true;
        Resize+=(s,e)=>Design.RoundControl(this,12);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=3,Margin=Padding.Empty};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,24));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(layout);
        title=new Label{Dock=DockStyle.Fill,Font=Design.Font(10,true),AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty};
        detail=new Label{Dock=DockStyle.Fill,Font=Design.Font(8),ForeColor=Design.Muted,AutoEllipsis=true,Margin=Padding.Empty};
        layout.Controls.Add(title,0,0);layout.SetColumnSpan(title,3);layout.Controls.Add(detail,0,1);layout.SetColumnSpan(detail,3);
        slider.Dock=DockStyle.Fill;slider.Margin=Padding.Empty;layout.Controls.Add(slider,0,2);
        level=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=Design.Font(8.5f,true),Margin=Padding.Empty};layout.Controls.Add(level,1,2);
        mute=Design.Button("静音",66,26);mute.Dock=DockStyle.Fill;mute.Margin=Padding.Empty;layout.Controls.Add(mute,2,2);
        slider.Changed+=(s,e)=>{if(updating)return;level.Text=slider.Value+"%";rendered=null;commit.Stop();commit.Start();};
        commit.Interval=150;commit.Tick+=async(s,e)=>{if(slider.Capture)return;commit.Stop();await Apply(slider.Value,null);};
        mute.Click+=async(s,e)=>{bool pending=commit.Enabled;commit.Stop();await Apply(pending?(int?)slider.Value:null,!app.Muted);};
        Sync(model);
    }
    internal void CancelPending(){commit.Stop();slider.Capture=false;Sync(app);}
    internal void Sync(Audio.AppVolume model) {
        if(Editing)return;app=model;
        string signature=app.Name+"\n"+app.EndpointName+"\n"+app.Level+"\n"+app.Muted+"\n"+app.Mixed;
        if(rendered==signature)return;rendered=signature;updating=true;
        try {
            if(title.Text!=app.Name){title.Text=app.Name;tip.SetToolTip(title,app.Name);slider.AccessibleName=app.Name+"音量";}
            string description=app.EndpointName+" · "+(app.Mixed?"音量不同，拖动后统一":app.Muted?"已静音":"正在播放");
            if(detail.Text!=description){detail.Text=description;tip.SetToolTip(detail,description);}
            slider.Value=app.Level;string percent=app.Level+"%";if(level.Text!=percent)level.Text=percent;
            string action=app.Muted?"取消静音":"静音";if(mute.Text!=action)mute.Text=action;
            mute.AccessibleName=app.Name+mute.Text;
        } finally{updating=false;}
    }
    async Task Apply(int? value,bool? muted) {
        if(writing||IsDisposed)return;
        writing=true;slider.Enabled=mute.Enabled=false;
        bool success=false;
        try {
            await apply(app,value,muted);success=true;
            foreach(var s in app.Sessions){if(value.HasValue)s.Level=value.Value;if(muted.HasValue)s.Muted=muted.Value;}
        } catch(Exception) {if(!IsDisposed){rendered=null;detail.Text="未能全部调整，稍后自动刷新，请重试";}}
        finally {
            writing=false;
            if(!IsDisposed){slider.Enabled=mute.Enabled=true;if(success)Sync(app);if(EditFinished!=null)EditFinished();}
        }
    }
    internal Task VerifyApply(int value,bool muted){return Apply(value,muted);}
    protected override void Dispose(bool disposing){if(disposing){commit.Dispose();tip.Dispose();}base.Dispose(disposing);}
}
