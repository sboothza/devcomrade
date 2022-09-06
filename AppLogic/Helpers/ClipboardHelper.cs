using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;

namespace AppLogic.Helpers
{
    public static class ClipboardHelper
    {
        public static void Paste()
        {
            var sim = new InputSimulator();
            sim.Keyboard.TextEntry("this is a test");
            //sim.Keyboard.KeyDown(VirtualKeyCode.CONTROL);
            //sim.Keyboard.KeyPress(VirtualKeyCode.VK_V);
            //sim.Keyboard.KeyUp(VirtualKeyCode.CONTROL);

            //InputSimulator SimulateKeyPress(VirtualKeyCode.SPACE);


            //var handle = WinApi.GetFocusedHandle();
            // var handle = WinApi.GetForegroundWindow();
            // WinApi.BlockInput(true);

            //var childWindows = WinApi.GetAllChildHandles(handle);

            // WinApi.PostMessage(handle, WinApi.WM_PASTE, IntPtr.Zero, IntPtr.Zero);

            // if (childWindows.Any())
            //     foreach (var child in childWindows)
            //         WinApi.PostMessage(child, WinApi.WM_PASTE, IntPtr.Zero, IntPtr.Zero);
            // else
            //     WinApi.PostMessage(handle, WinApi.WM_PASTE, IntPtr.Zero, IntPtr.Zero);

            //Console.WriteLine(child.ToString());

            // var result = WinApi.PostMessage(handle, WinApi.WM_PASTE, IntPtr.Zero, IntPtr.Zero);
            // Console.WriteLine(result);
            //   WinApi.BlockInput(false);
        }
    }
}
