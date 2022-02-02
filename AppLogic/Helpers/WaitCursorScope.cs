// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace AppLogic.Helpers
{
    internal class WaitCursorScope : IDisposable
    {
        private const int DELAY = 200;
        private static readonly ThreadLocal<IDisposable?> _current = new();
        private readonly Cursor? _oldCursor;

        private readonly Func<bool>? _showWhen;
        private readonly Stopwatch _stopwatch = new();
        private readonly Timer? _timer;

        private WaitCursorScope(Func<bool>? showWhen = null)
        {
            _current.Value?.Dispose();
            _current.Value = this;

            _stopwatch.Start();
            _oldCursor = Cursor.Current;
            _showWhen = showWhen;
            _timer = new Timer
                     {
                         Interval = 250
                     };

            _timer.Tick += OnIdle;
            _timer.Start();
            Application.Idle += OnIdle;
        }

        private bool IsCurrent => _current.Value == this;

        void IDisposable.Dispose()
        {
            Stop();
        }

        private void OnIdle(object? s, EventArgs e)
        {
            if (IsCurrent                               &&
                _showWhen?.Invoke()            != false && // i.e., if null or true
                _stopwatch.ElapsedMilliseconds >= DELAY)
                Cursor.Current = Cursors.AppStarting;
        }

        public void Stop()
        {
            if (_current.Value == this)
            {
                _current.Value = null;
                _timer?.Dispose();
                Application.Idle -= OnIdle;
                Cursor.Hide();
                Cursor.Current = _oldCursor ?? Cursors.Default;
                Cursor.Show();
            }
        }

        public static IDisposable Create(Func<bool>? showWhen = null)
        {
            return new WaitCursorScope(showWhen);
        }
    }
}