// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using AppLogic.Models;

namespace AppLogic.Presenter
{
    public delegate Task HotkeyHandlerCallback(Hotkey hotkey, CancellationToken token);

    public class HotkeyHandler
    {
        public HotkeyHandler(Hotkey hotkey, HotkeyHandlerCallback callback)
        {
            Hotkey = hotkey;
            Callback = callback;
        }

        public Hotkey Hotkey { get; }
        public HotkeyHandlerCallback Callback { get; }
    }
}