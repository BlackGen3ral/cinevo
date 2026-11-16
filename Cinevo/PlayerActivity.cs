using System.Runtime.InteropServices;
namespace Cinevo;
internal sealed class PlayerActivity
{
 private long last;
 public void Touch(long now)=>last=now;
 public bool Hide(long now,bool playing,bool interacting)=>playing&&!interacting&&now-last>=3000;
}
internal sealed class PlayerInput
{
 [StructLayout(LayoutKind.Sequential)]private struct Point{public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]private struct Pad{public uint Packet;public ushort Buttons;public byte LeftTrigger,RightTrigger;public short LX,LY,RX,RY;}
 private Point previous;private readonly uint[] packets=new uint[4];private bool supportsPad=true;
 public bool Changed(IntPtr handle){var changed=false;if(GetCursorPos(out var point)&&GetWindowRect(handle,out var rect)){changed=(point.X!=previous.X||point.Y!=previous.Y)&&point.X>=rect.Left&&point.X<=rect.Right&&point.Y>=rect.Top&&point.Y<=rect.Bottom;previous=point;}
  if(supportsPad)try{for(uint i=0;i<4;i++)if(XInputGetState(i,out var state)==0&&packets[i]!=state.Packet){packets[i]=state.Packet;changed=true;}}catch(Exception ex)when(ex is DllNotFoundException or EntryPointNotFoundException){supportsPad=false;}return changed;
 }
 [DllImport("user32")]private static extern bool GetCursorPos(out Point point);
 [DllImport("user32")]private static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("xinput1_4")]private static extern uint XInputGetState(uint user,out Pad state);
}
