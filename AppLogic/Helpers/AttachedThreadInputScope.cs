// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System;
using System.Text;
using System.Threading;

namespace AppLogic.Helpers
{
    /// <summary>
    ///     Attach Win32 input queue to another thread
    /// </summary>
    internal class AttachedThreadInputScope : IDisposable
    {
        private static readonly ThreadLocal<IDisposable?> _current = new();
        private readonly uint _currentThreadId;
        private readonly uint _foregroundThreadId;

        private AttachedThreadInputScope()
        {
            _current.Value?.Dispose();
            _current.Value = this;

            ForegroundWindow = WinApi.GetForegroundWindow();
            _currentThreadId = WinApi.GetCurrentThreadId();
            _foregroundThreadId = WinApi.GetWindowThreadProcessId(ForegroundWindow, out var _);

            if (_currentThreadId != _foregroundThreadId)
                // attach to the foreground thread
                if (!WinApi.AttachThreadInput(_currentThreadId, _foregroundThreadId, true))
                {
                    // unable to attach, see if the window is a Win10 Console Window
                    var className = new StringBuilder(256);
                    WinApi.GetClassName(ForegroundWindow, className, className.Capacity - 1);
                    if (string.CompareOrdinal("ConsoleWindowClass", className.ToString()) != 0)
                        return;
                    // consider attached to a console window
                }

            IsAttached = true;
        }

        public bool IsCurrent => _current.Value == this;
        public bool IsAttached { get; private set; }

        public IntPtr ForegroundWindow { get; } = IntPtr.Zero;

        void IDisposable.Dispose()
        {
            if (_current.Value == this)
            {
                _current.Value = null;
                if (IsAttached)
                {
                    IsAttached = false;
                    if (_currentThreadId != _foregroundThreadId)
                        WinApi.AttachThreadInput(_currentThreadId, _foregroundThreadId, false);
                }
            }
        }

        public static AttachedThreadInputScope Create()
        {
            return new AttachedThreadInputScope();
        }
    }
}