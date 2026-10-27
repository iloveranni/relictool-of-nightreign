using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace NightreignRelicTool.FinalUI {
 // #2: DWM present pacing on a worker; only coalesced window movement reaches the UI.
 public sealed class EdgeDock:IDisposable {
  readonly Window window; readonly Stopwatch clock=Stopwatch.StartNew();
  readonly DispatcherTimer pointer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(80)};
  readonly DispatcherTimer settle=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(150)};
  HwndSource source;IntPtr hwnd;
  RectI expanded,work;int edge=-1;bool moving,dragging,disposed,showing=true;double leaveAt=-1,startTime,duration,lastFrame;int startX,startY,targetX,targetY;
  CancellationTokenSource pulse; int queued; Task pulseTask;
  Window hint; bool suppressLocation; int appliedX,appliedY;
  public bool IsDocked {get{return edge>=0;}}public bool IsAnimating {get{return moving;}}
  public EdgeDock(Window w){window=w;w.SourceInitialized+=Initialize;w.Closed+=Closed;w.StateChanged+=StateChanged;w.LocationChanged+=LocationChanged;pointer.Tick+=Detect;settle.Tick+=Settled;}
  void StateChanged(object s,EventArgs e){if(window.WindowState==WindowState.Minimized){StopMotion();pointer.Stop();RemoveHint();}else if(IsDocked){Animate(true);pointer.Start();}}
  void LocationChanged(object s,EventArgs e){
   if(disposed||moving||suppressLocation)return;
   RectI actual;if(GetWindowRect(hwnd,out actual)&&actual.Left==appliedX&&actual.Top==appliedY)return;
   if(IsDocked){edge=-1;pointer.Stop();RemoveHint();SetWindowRgn(hwnd,IntPtr.Zero,true);}
   settle.Stop();settle.Start();
  }
  void Settled(object s,EventArgs e){if(Mouse.LeftButton==MouseButtonState.Pressed)return;settle.Stop();if(!IsDocked)TryDock();}
  void Initialize(object s,EventArgs e){hwnd=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(hwnd);source.AddHook(Hook);}
  IntPtr Hook(IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled){
   if(msg==0x231){dragging=true;StopMotion();SetWindowRgn(hwnd,IntPtr.Zero,true);edge=-1;pointer.Stop();RemoveHint();}
   if(msg==0x232){dragging=false;TryDock();}
   if(msg==0x7E || msg==0x2E0){StopMotion();edge=-1;pointer.Stop();RemoveHint();SetWindowRgn(hwnd,IntPtr.Zero,true);}
   return IntPtr.Zero;
  }
  public void TryDock(){
   if(disposed||dragging||window.WindowState!=WindowState.Normal)return;
   GetWindowRect(hwnd,out expanded);var monitor=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor))return;work=monitor.Work;
   int[] distances={Math.Abs(expanded.Left-work.Left),Math.Abs(expanded.Right-work.Right),Math.Abs(expanded.Top-work.Top),Math.Abs(expanded.Bottom-work.Bottom)};
   int best=0;for(int i=1;i<4;i++)if(distances[i]<distances[best])best=i;
   if(distances[best]>18){edge=-1;pointer.Stop();return;}
   edge=best;int width=expanded.Right-expanded.Left,height=expanded.Bottom-expanded.Top;
   expanded.Left=Math.Max(work.Left,Math.Min(work.Right-width,expanded.Left));expanded.Top=Math.Max(work.Top,Math.Min(work.Bottom-height,expanded.Top));
   if(edge==0)expanded.Left=work.Left;if(edge==1)expanded.Left=work.Right-width;if(edge==2)expanded.Top=work.Top;if(edge==3)expanded.Top=work.Bottom-height;
   expanded.Right=expanded.Left+width;expanded.Bottom=expanded.Top+height;
   appliedX=expanded.Left;appliedY=expanded.Top;suppressLocation=true;try{SetWindowPos(hwnd,IntPtr.Zero,expanded.Left,expanded.Top,0,0,0x15);}finally{suppressLocation=false;}
   showing=true;leaveAt=clock.Elapsed.TotalMilliseconds;pointer.Start();
  }
  void Detect(object s,EventArgs e){
   if(!IsDocked||dragging||window.WindowState!=WindowState.Normal)return;
   PointI p;GetCursorPos(out p);RectI r;GetWindowRect(hwnd,out r);
   bool inside=p.X>=Math.Max(work.Left,r.Left)-2&&p.X<Math.Min(work.Right,r.Right)+2&&p.Y>=Math.Max(work.Top,r.Top)-2&&p.Y<Math.Min(work.Bottom,r.Bottom)+2;
   bool interacting=Mouse.LeftButton==MouseButtonState.Pressed||Mouse.Captured!=null;
   if(inside||interacting){leaveAt=-1;if(!showing)Animate(true);}
   else {if(leaveAt<0)leaveAt=clock.Elapsed.TotalMilliseconds;if(showing&&clock.Elapsed.TotalMilliseconds-leaveAt>=550)Animate(false);}
  }
  public void Animate(bool show){
   if(!IsDocked||disposed)return;RectI r;GetWindowRect(hwnd,out r);startX=r.Left;startY=r.Top;targetX=expanded.Left;targetY=expanded.Top;
   int width=expanded.Right-expanded.Left,height=expanded.Bottom-expanded.Top;int strip=Math.Max(6,(int)Math.Round(6*VisualTreeHelper.GetDpi(window).DpiScaleX));
   if(!show){if(edge==0)targetX=work.Left-width+strip;if(edge==1)targetX=work.Right-strip;if(edge==2)targetY=work.Top-height+strip;if(edge==3)targetY=work.Bottom-strip;}
   double distance=Math.Max(Math.Abs(targetX-startX),Math.Abs(targetY-startY));duration=Math.Max(70,260*distance/Math.Max(1,(edge<2?width:height)-strip));
   showing=show;startTime=lastFrame=clock.Elapsed.TotalMilliseconds;
   if(show)leaveAt=-1;
   if(!show)ShowHint();
   if(!moving){moving=true;pulse=new CancellationTokenSource();var token=pulse.Token;
    pulseTask=Task.Run(()=>{
     while(!token.IsCancellationRequested){
      // This wait is on the worker, never the WPF dispatcher. No timer, FPS cap or busy spin.
      int hr=DwmFlush();if(hr<0){window.Dispatcher.BeginInvoke(new Action(StopMotion));return;}
      if(token.IsCancellationRequested)return;
      if(Interlocked.Exchange(ref queued,1)==0)window.Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>{
       Interlocked.Exchange(ref queued,0);if(!token.IsCancellationRequested&&moving&&!disposed)Render();
      }));
     }
    });
   }
  }
  void Render(){
   double now=clock.Elapsed.TotalMilliseconds,t=Math.Min(1,(now-startTime)/duration),smooth=t*t*(3-2*t);
   int x=(int)Math.Round(startX+(targetX-startX)*smooth),y=(int)Math.Round(startY+(targetY-startY)*smooth);
   // Native movement avoids WPF measure/arrange and display preference writes each frame.
   appliedX=x;appliedY=y;SetWindowPos(hwnd,IntPtr.Zero,x,y,0,0,0x15);
   ClipToWork(x,y);
   lastFrame=now;
   if(t>=1){StopMotion();if(showing){SetWindowRgn(hwnd,IntPtr.Zero,true);RemoveHint();}}
  }
  void ClipToWork(int x,int y){
   int w=expanded.Right-expanded.Left,h=expanded.Bottom-expanded.Top;
   // Crop outside this monitor's work area: never expose hidden content on a neighbour or taskbar.
   IntPtr region=CreateRectRgn(Math.Max(0,work.Left-x),Math.Max(0,work.Top-y),Math.Min(w,work.Right-x),Math.Min(h,work.Bottom-y));
   if(SetWindowRgn(hwnd,region,true)==0)DeleteObject(region);
  }
  void StopMotion(){if(moving){moving=false;var ending=pulse;pulse=null;ending.Cancel();pulseTask.ContinueWith(t=>ending.Dispose());}}
  void ShowHint(){
   if(hint!=null)return;
   var dpi=VisualTreeHelper.GetDpi(window);
   hint=new Window{Title="遗物位置 · 贴边",Owner=window,ShowInTaskbar=false,ShowActivated=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Ui.Gold,Topmost=window.Topmost};
   hint.Width=edge<2?6:(expanded.Right-expanded.Left)/dpi.DpiScaleX;
   hint.Height=edge<2?(expanded.Bottom-expanded.Top)/dpi.DpiScaleY:6;
   hint.Left=(edge==0?work.Left:edge==1?work.Right-6*dpi.DpiScaleX:expanded.Left)/dpi.DpiScaleX;
   hint.Top=(edge==2?work.Top:edge==3?work.Bottom-6*dpi.DpiScaleY:expanded.Top)/dpi.DpiScaleY;
   hint.MouseEnter+=(s,e)=>Animate(true);hint.MouseLeftButtonDown+=(s,e)=>Animate(true);hint.Show();
   // Use physical monitor coordinates for mixed-DPI/negative-origin arrangements.
   int stripX=(int)Math.Round(6*dpi.DpiScaleX),stripY=(int)Math.Round(6*dpi.DpiScaleY);
   SetWindowPos(new WindowInteropHelper(hint).Handle,IntPtr.Zero,
    edge==0?work.Left:edge==1?work.Right-stripX:expanded.Left,
    edge==2?work.Top:edge==3?work.Bottom-stripY:expanded.Top,
    edge<2?stripX:expanded.Right-expanded.Left,edge<2?expanded.Bottom-expanded.Top:stripY,0x14);
  }
  void RemoveHint(){if(hint!=null){hint.Close();hint=null;}}
  void Closed(object s,EventArgs e){Dispose();}
  public void Dispose(){if(disposed)return;disposed=true;StopMotion();RemoveHint();pointer.Stop();settle.Stop();settle.Tick-=Settled;pointer.Tick-=Detect;window.StateChanged-=StateChanged;window.LocationChanged-=LocationChanged;window.SourceInitialized-=Initialize;window.Closed-=Closed;if(source!=null)source.RemoveHook(Hook);}
  [StructLayout(LayoutKind.Sequential)]struct RectI{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct PointI{public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public RectI Monitor,Work;public int Flags;}
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr w,out RectI r);
  [DllImport("user32.dll")]static extern bool GetCursorPos(out PointI p);
  [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr w,uint f);
  [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern bool GetMonitorInfo(IntPtr m,ref MonitorInfo i);
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr w,IntPtr z,int x,int y,int cx,int cy,uint flags);
  [DllImport("user32.dll")]static extern int SetWindowRgn(IntPtr w,IntPtr region,bool redraw);
  [DllImport("gdi32.dll")]static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
  [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr o);
  [DllImport("dwmapi.dll",PreserveSig=true)]static extern int DwmFlush();
 }
}
