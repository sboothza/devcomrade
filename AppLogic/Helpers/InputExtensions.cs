using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;

namespace AppLogic.Helpers
{
    public static class InputExtensions
    {
        // private static readonly Lazy<int[]> _allKeys = new(() =>
        //                                                    {
        //                                                        // ignore some toggle keys (CapsLock etc) when we check if all keys are de-pressed
        //                                                        var toggleKeys = new[]
        //                                                                         {
        //                                                                             // https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        //                                                                             WinApi.VK_NUMLOCK, WinApi.VK_SCROLL, WinApi.VK_CAPITAL, 0xE7, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1F, 0xFF
        //                                                                         };
        //
        //                                                        return Enumerable.Range(1, 256)
        //                                                                         .Where(key => !toggleKeys.Contains(key))
        //                                                                         .ToArray();
        //                                                    },
        //                                                    LazyThreadSafetyMode.None);
        //
        // private static int[] AllKeys => _allKeys.Value;

        static InputExtensions()
        {
            _allKeys = Enum.GetValues<VirtualKeyCode>();
        }

        private static VirtualKeyCode[] _allKeys;
        private static VirtualKeyCode[] AllKeys => _allKeys;

        // public static bool IsAnyKeyPressed(this IKeyboardSimulator keyboardSimulator)
        // {
        //     return AllKeys.Any(keyboardSimulator.KeyPress);
        // }
    }
}
