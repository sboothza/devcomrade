// Copyright (C) 2020+ by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System.Threading.Tasks;
using System.Windows.Forms;
using AppLogic.Events;
using AppLogic.Helpers;

namespace AppLogic.Presenter
{
    internal class ClipboardFormatMonitor : NativeWindow
    {
        public ClipboardFormatMonitor(IEventTargetHub hub)
        {
            EventTargetHub = hub;

            var cp = new CreateParams
                     {
                         Caption = string.Empty,
                         Style = unchecked((int)WinApi.WS_POPUP),
                         Parent = WinApi.HWND_MESSAGE
                     };

            base.CreateHandle(cp);
        }

        private IEventTargetHub EventTargetHub { get; }

        public async Task StartAsync()
        {
            // AddClipboardFormatListener may fail when 
            // another app clipboard operation is in progress

            const int retryDelay = 500;
            var retryAttempts = 10;

            while (true)
            {
                if (WinApi.AddClipboardFormatListener(Handle))
                    return;

                if (--retryAttempts <= 0)
                    break;

                await Task.Delay(retryDelay);
            }

            throw WinUtils.CreateExceptionFromLastWin32Error();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WinApi.WM_CLIPBOARDUPDATE)
                EventTargetHub.Dispatch(this, new ClipboardUpdateEventArgs());

            base.WndProc(ref m);
        }
    }
}