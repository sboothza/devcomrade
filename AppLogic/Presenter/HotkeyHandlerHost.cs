﻿// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AppLogic.Config;
using AppLogic.Events;
using AppLogic.Helpers;
using Microsoft.Win32;
using WindowsInput;

namespace AppLogic.Presenter
{
    [DesignerCategory("")]
    internal partial class HotkeyHandlerHost : Form,
                                               IMessageFilter,
                                               INotifyPropertyChanged,
                                               IHotkeyHandlerHost,
                                               IContainer,
                                               IEventTargetProp<ClipboardUpdateEventArgs>,
                                               IEventTargetProp<ControlClipboardMonitoringEventArgs>,
                                               IEventTargetHub
    {
        private const int ASYNC_LOCK_TIMEOUT = 250;
        private const int CLIPBOARD_MONITORING_DELAY = 100;

        // SemaphoreSlim as an async lock for hotkey handlers to avoid re-entrance
        private readonly SemaphoreSlim _asyncLock = new(1);

        private readonly Lazy<ClipboardFormatMonitor> _clipboardFormatMonitor;

        private readonly Container _componentContainer = new();

        // when this is signaled, the container's RunAsync exits
        private readonly CancellationTokenSource _cts;

        // map hotkey ID to handler
        private readonly Dictionary<int, HotkeyHandler> _handlersByHotkeyIdMap = new();

        // map hotkey Name to handler
        private readonly Dictionary<string, HotkeyHandler> _handlersByHotkeyNameMap = new();

        private readonly Lazy<ContextMenuStrip> _menu;

        private readonly Lazy<Notepad> _notepad;

        // for playing sound notifications
        private readonly Lazy<SoundPlayer?> _soundPlayer;

        // classes which provide hotkey handlers
        private int _hotkeyId; // IDs for Win32 RegisterHotKey

        private int _menuActive;

        private bool _updatingClipboard;

        private readonly InputSimulator _inputSimulator;

        public HotkeyHandlerHost(CancellationToken token)
        {
            ShowInTaskbar = false;
            CreateHandle();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _soundPlayer = new Lazy<SoundPlayer?>(CreateSoundPlayer, false);
            _menu = new Lazy<ContextMenuStrip>(CreateContextMenu, false);
            _notepad = new Lazy<Notepad>(CreateNotepad, false);
            _clipboardFormatMonitor = new Lazy<ClipboardFormatMonitor>(() => new ClipboardFormatMonitor(this), false);

            _inputSimulator = new InputSimulator();

            Completion = RunAsync();
        }

        // cancellation for RunAsync
        private CancellationToken Token => _cts.Token;

        private ContextMenuStrip Menu => _menu.Value;

        private Notepad Notepad => _notepad.Value;

        // the task of RunAsync
        private Task Completion { get; }

        private bool IsMenuActive => _menuActive > 0;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Parent = WinApi.GetDesktopWindow();
                cp.Style = unchecked((int)WinApi.WS_POPUP);
                cp.ExStyle = unchecked((int)(WinApi.WS_EX_NOACTIVATE | WinApi.WS_EX_TOOLWINDOW));
                return cp;
            }
        }

        private static int PauseFormattingRemovalTimeout =>
            Configuration.GetOption("pauseFormattingRemovalTimeout", 60);

        public static bool IsFormattingRemovalEnabled =>
            Configuration.GetOption("removeClipboardFormatting", true);

        bool IHotkeyHandlerHost.ClipboardContainsText()
        {
            return Clipboard.ContainsText();
        }

        string IHotkeyHandlerHost.GetClipboardText()
        {
            return Clipboard.GetText(TextDataFormat.UnicodeText);
        }

        void IHotkeyHandlerHost.ClearClipboard()
        {
            UpdateClipboard(Clipboard.Clear);
        }

        void IHotkeyHandlerHost.SetClipboardText(string text)
        {
            UpdateClipboard(() => Clipboard.SetText(text, TextDataFormat.UnicodeText));
        }

        void IHotkeyHandlerHost.SetClipboardDataObject(object data)
        {
            UpdateClipboard(() => Clipboard.SetDataObject(data));
        }

        async Task IHotkeyHandlerHost.FeedTextAsync(string text, CancellationToken token)
        {
            using var threadInputScope = AttachedThreadInputScope.Create();
            if (threadInputScope.IsAttached)
            {
                using (WaitCursorScope.Create())
                {
                    await KeyboardInput.WaitForAllKeysReleasedAsync(token);
                }

                _inputSimulator.Keyboard.TextEntry(text);
            }
        }

        void IHotkeyHandlerHost.PlayNotificationSound()
        {
            if (_soundPlayer.Value is {} soundPlayer)
            {
                soundPlayer.Stop();
                soundPlayer.Play();
            }
        }

        void IHotkeyHandlerHost.ShowMenu()
        {
            async Task showMenuAsync()
            {
                // show the menu and await its dismissal
                if (!await _asyncLock.WaitAsync(ASYNC_LOCK_TIMEOUT, Token))
                    return;

                OnEnterMenu();
                try
                {
                    var menuClosedTcs = new TaskCompletionSource<DBNull>(TaskCreationOptions.RunContinuationsAsynchronously);

                    await using var rego = Token.Register(() => menuClosedTcs.TrySetCanceled());

                    using var menuCloseScope = SubscriptionScope<ToolStripDropDownClosedEventHandler>.Create((_, _) => menuClosedTcs.TrySetResult(DBNull.Value),
                                                                                                             handler => Menu.Closed += handler,
                                                                                                             handler => Menu.Closed -= handler);

                    if (!WinUtils.TryGetThirdPartyForgroundWindow(out var targetWindow))
                    {
                        // special treatment for our Internal Notepad
                        if (_notepad.IsValueCreated && _notepad.Value.ContainsFocus)
                            targetWindow = WinApi.GetForegroundWindow();
                        else
                            targetWindow = WinUtils.GetPrevActiveWindow();
                    }

                    using (AttachedThreadInputScope.Create())
                    {
                        // steal the focus
                        WinApi.SetForegroundWindow(Handle);
                        await InputUtils.TimerYield(token: Token);
                    }

                    try
                    {
                        Menu.Show(this, Cursor.Position);
                        await menuClosedTcs.Task;
                    }
                    finally
                    {
                        // restore the focus
                        Cursor.Hide();
                        Cursor.Show();
                        WinApi.SetForegroundWindow(targetWindow);
                    }
                }
                finally
                {
                    OnExitMenu();
                    _asyncLock.Release();
                }
            }

            showMenuAsync()
                .IgnoreCancellations();
        }

        async Task IHotkeyHandlerHost.ShowNotepad(string? text)
        {
            using var threadInputScope = AttachedThreadInputScope.Create();

            if (!Notepad.Visible)
                Notepad.Show();

            WinApi.SetForegroundWindow(Notepad.Handle);

            await Notepad.WaitForReadyAsync(Token);

            Notepad.FocusEditor();

            if (text != null)
                Notepad.Paste(text);
        }

        int IHotkeyHandlerHost.TabSize =>
            Configuration.GetOption("tabSize", 2);

        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case WinApi.WM_HOTKEY:
                    if (_handlersByHotkeyIdMap.TryGetValue((int)m.WParam, out var handler))
                    {
                        HandleHotkeyAsync(handler)
                            .IgnoreCancellations();

                        return true;
                    }

                    break;

                case WinApi.WM_QUIT:
                    Quit();
                    return true;
            }

            return false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Quit()
        {
            _cts.Cancel();
        }

        private void OnEnterMenu()
        {
            _menuActive++;
        }

        private void OnExitMenu()
        {
            _menuActive--;
        }

        private ValueTask<IDisposable> WithLockAsync()
        {
            return Disposable.CreateAsync(() => _asyncLock.WaitAsync(Token),
                                          () => _asyncLock.Release());
        }

        // standard hotkey handler providers
        private static IEnumerable<Type> GetHotkeyHandlerProviders()
        {
            yield return typeof(PredefinedHotkeyHandlers);
            yield return typeof(ScriptHotkeyHandlers);
        }

        public void RaisePropertyChange([CallerMemberName] string propertyname = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        public Task AsTask()
        {
            return Completion;
        }

        private void RegisterWindowsHotkey(HotkeyHandler hotkeyHandler)
        {
            var hotkey = hotkeyHandler.Hotkey;
            if (!hotkey.HasHotkey)
                throw new InvalidOperationException(nameof(RegisterWindowsHotkey));

            if (WinApi.RegisterHotKey(IntPtr.Zero,
                                      ++_hotkeyId,
                                      hotkey.Mods!.Value | WinApi.MOD_NOREPEAT,
                                      hotkey.Vkey!.Value))
            {
                _handlersByHotkeyIdMap.Add(_hotkeyId, hotkeyHandler);
            }
            else
            {
                var error = WinUtils.CreateExceptionFromLastWin32Error();
                throw new WarningException($"{hotkeyHandler.Hotkey.Name}: {error.Message}", error);
            }
        }

        private void SetCurrentFolder()
        {
            if (Configuration.TryGetOption("currentFolder", out var folder))
                folder = Environment.ExpandEnvironmentVariables(folder);

            if (folder == null || !Directory.Exists(folder))
                folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            Directory.SetCurrentDirectory(folder);
        }

        private void InitializeClipboardFormatMonitoring()
        {
            if (!IsFormattingRemovalEnabled)
                return;

            _clipboardFormatMonitor.Value.StartAsync()
                                   .IgnoreCancellations();

            this.AddListener<ClipboardUpdateEventArgs>((_, _) => handleOnClipboardTextChangedAsync());

            handleOnClipboardTextChangedAsync();

            async void handleOnClipboardTextChangedAsync()
            {
                try
                {
                    await onClipboardTextChangedAsync();
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException || ClipboardAccess.IsClipboardError(ex))
                        // absorb cancellations and clipboard errors
                        Trace.WriteLine(ex.Message);
                    else
                        throw;
                }
            }

            async Task onClipboardTextChangedAsync()
            {
                if (_updatingClipboard || IsFormattingRemovalPaused)
                    return;

                _updatingClipboard = true;
                try
                {
                    await ClipboardAccess.EnsureAsync(IntPtr.Zero,
                                                      CLIPBOARD_MONITORING_DELAY,
                                                      Token);

                    if (!Clipboard.ContainsText())
                        return;

                    if (Clipboard.ContainsText(TextDataFormat.Html) ||
                        Clipboard.ContainsText(TextDataFormat.Rtf))
                    {
                        var text = string.Empty;
                        if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                            text = Clipboard.GetText(TextDataFormat.UnicodeText);

                        if (text.IsNullOrEmpty())
                            text = Clipboard.GetText(TextDataFormat.Text);

                        if (!text.IsNullOrEmpty())
                        {
                            Clipboard.SetText(text, TextDataFormat.UnicodeText);
                            await InputUtils.InputYield(delay: CLIPBOARD_MONITORING_DELAY, token: Token);
                        }
                    }
                }
                finally
                {
                    _updatingClipboard = false;
                }
            }
        }

        private void InitializeHotkeys()
        {
            // Roaming config gets precedence over Local
            var hotkeys = Configuration.RoamingHotkeys.Union(Configuration.LocalHotkeys)
                                       .ToArray();

            // instantiate the known hotkey handler providers
            var providers = GetHotkeyHandlerProviders()
                            .Select(type => Activator.CreateInstance(type, this))
                            .OfType<IHotkeyHandlerProvider>()
                            .ToArray();

            foreach (var hotkey in hotkeys)
            {
                foreach (var provider in providers)
                    if (provider.CanHandle(hotkey, out var callback))
                    {
                        var handler = new HotkeyHandler(hotkey, callback);
                        _handlersByHotkeyNameMap.Add(hotkey.Name, handler);
                        if (hotkey.HasHotkey)
                            RegisterWindowsHotkey(handler);
                    }
            }
        }

        protected override void Dispose(bool disposing)
        {
            _cts.Dispose();

            foreach (var hotkeyId in _handlersByHotkeyIdMap.Keys)
                WinApi.UnregisterHotKey(IntPtr.Zero, hotkeyId);

            _handlersByHotkeyNameMap.Clear();
            _handlersByHotkeyIdMap.Clear();

            if (_notepad.IsValueCreated)
                _notepad.Value.Dispose();

            _componentContainer.Dispose();

            base.Dispose(disposing);
        }

        private SoundPlayer? CreateSoundPlayer()
        {
            if (!Configuration.GetOption("playNotificationSound", true))
                return null;

            if (!Configuration.TryGetOption("notifySound", out var soundPath))
                return null;

            SoundPlayer? soundPlayer = null;
            var filePath = Environment.ExpandEnvironmentVariables(soundPath);
            if (File.Exists(filePath))
            {
                soundPlayer = new SoundPlayer();
                try
                {
                    soundPlayer.SoundLocation = filePath;
                    Add(soundPlayer);
                }
                catch
                {
                    soundPlayer.Dispose();
                    throw;
                }
            }

            return soundPlayer;
        }

        private async Task HandleHotkeyAsync(HotkeyHandler hotkeyHandler)
        {
            while (!await _asyncLock.WaitAsync(ASYNC_LOCK_TIMEOUT, Token))
                if (IsMenuActive)
                    Menu.Close(ToolStripDropDownCloseReason.Keyboard);
                else
                    // discard this hotkey event, as we only allow 
                    // one handler at a time to prevent re-entrance
                    return;

            // try to get an instant async lock
            try
            {
                await InputUtils.TimerYield(token: Token);
                await hotkeyHandler.Callback(hotkeyHandler.Hotkey, Token);
            }
            finally
            {
                _asyncLock.Release();
            }
        }

        /// <summary>
        ///     Provide tray menu items
        /// </summary>
        private IEnumerable<(string, MenuItemEventHandler?, MenuItemSetUpdaterCallback?)> GetMenuItems()
        {
            // first add hotkey handlers which also have menuItem in the config file
            var handlers = _handlersByHotkeyNameMap.Values
                                                   .Where(handler => handler.Hotkey.MenuItem.IsNotNullNorWhiteSpace())
                                                   .ToArray();

            if (handlers.Length > 0)
            {
                foreach (var handler in handlers)
                {
                    var hotkey = handler.Hotkey;
                    string menuItemText;
                    if (hotkey.HasHotkey)
                    {
                        var hotkeyTitle = WinUtils.GetHotkeyTitle(hotkey.Mods!.Value, hotkey.Vkey!.Value);
                        menuItemText = $"{hotkey.MenuItem}|{hotkeyTitle}";
                    }
                    else
                    {
                        menuItemText = hotkey.MenuItem!;
                    }

                    yield return (
                                     menuItemText,
                                     (_, _) => HandleHotkeyAsync(handler)
                                         .IgnoreCancellations(),
                                     null);

                    if (hotkey.AddSeparator)
                        yield return GetSeparatorMenuItem();
                }

                yield return GetSeparatorMenuItem();
            }

            yield return ("Auto Start", AutoStart, update =>
                                                   {
                                                       update(IsAutorun);
                                                       PropertyChanged += (_, e) =>
                                                                          {
                                                                              if (string.CompareOrdinal(e.PropertyName, nameof(IsAutorun)) == 0)
                                                                                  update(IsAutorun);
                                                                          };
                                                   }
                         );

            if (IsFormattingRemovalEnabled)
                yield return ($"Pause &Formattting Removal for {PauseFormattingRemovalTimeout} min",
                              PauseFormattingRemoval, update =>
                                                      {
                                                          update(IsFormattingRemovalPaused);
                                                          PropertyChanged += (_, e) =>
                                                                             {
                                                                                 if (string.CompareOrdinal(e.PropertyName, nameof(IsFormattingRemovalPaused)) == 0)
                                                                                     update(IsFormattingRemovalPaused);
                                                                             };
                                                      }
                             );

            yield return ("Edit Local Config", EditLocalConfig, null);
            yield return ("Edit Roaming Config", EditRoamingConfig, null);
            yield return ("Restart", Restart, null);

            if (!Diagnostics.IsAdmin())
                yield return ("Restart as Admin", RestartAsAdmin, null);

            yield return GetSeparatorMenuItem();
            yield return ($"About {Application.ProductName}", About, null);
            yield return ("E&xit", Exit, null);
        }

        private EventHandler AsAsync(MenuItemEventHandler? handler)
        {
            // we make all click handlers async because 
            // we want the menu to be dismissed first
            void handle(object s, EventArgs e)
            {
                async Task handleAsync()
                {
                    await InputUtils.InputYield(token: Token);
                    handler?.Invoke(s, e);
                }

                handleAsync()
                    .IgnoreCancellations();
            }

            return handle!;
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var contextMenu = new ContextMenuStrip();
            contextMenu.Opened += (_, _) => OnEnterMenu();
            contextMenu.Closed += (_, _) => OnExitMenu();

            foreach (var (text, handler, setUpdater) in GetMenuItems())
                if (text == "-")
                {
                    contextMenu.Items.Add(new ToolStripSeparator());
                }
                else
                {
                    var left = text;
                    var right = string.Empty;
                    var separator = text.LastIndexOf('|');
                    if (separator >= 0)
                    {
                        left = text[..separator];
                        right = text[(separator + 1)..];
                    }

                    var menuItem = new ToolStripMenuItem(left, null, AsAsync(handler))
                                   {
                                       ShortcutKeyDisplayString = right
                                   };

                    setUpdater?.Invoke(value => menuItem.Checked = value);
                    contextMenu.Items.Add(menuItem);
                }

            return contextMenu;
        }

        private Notepad CreateNotepad()
        {
            var notepad = new Notepad(Token);

            notepad.ControlEnterPressed += (_, _) => saveNotepadToClipboard()
                                               .IgnoreCancellations();

            return notepad;

            async Task saveNotepadToClipboard()
            {
                using var lockObject = await WithLockAsync();
                var text = notepad.EditorText;
                notepad.Hide();
                if (text.IsNotNullNorEmpty())
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
            }
        }

        private NotifyIcon CreateTrayIconMenu()
        {
            var notifyIcon = new NotifyIcon(this)
                             {
                                 Text = Application.ProductName,
                                 ContextMenuStrip = Menu,
                                 Icon = Icon.ExtractAssociatedIcon(Diagnostics.GetExecutablePath())
                             };

            void clicked(object? s, EventArgs e)
            {
                (this as IHotkeyHandlerHost).ShowMenu();
            }

            notifyIcon.Click += clicked;
            notifyIcon.DoubleClick += clicked;

            return notifyIcon;
        }

        // async entry point
        private async Task RunAsync()
        {
            SetCurrentFolder();
            InitializeHotkeys();
            InitializeClipboardFormatMonitoring();

            Application.AddMessageFilter(this);
            try
            {
                var trayIconMenu = CreateTrayIconMenu();
                Add(trayIconMenu);
                trayIconMenu.Visible = true;
                try
                {
                    // this infinite delay defines the async scope 
                    // for AddMessageFilter/RemoveMessageFilter
                    // the token is cancelled when the app exits
                    await Task.Delay(Timeout.Infinite, Token);
                }
                finally
                {
                    trayIconMenu.Visible = false;
                }
            }
            finally
            {
                Application.RemoveMessageFilter(this);
            }
        }

        private void UpdateClipboard(Action updateAction)
        {
            var updatingClipboard = _updatingClipboard;
            try
            {
                updateAction();
            }
            finally
            {
                _updatingClipboard = updatingClipboard;
            }
        }

        #region Events

        EventTarget<ClipboardUpdateEventArgs> IEventTargetProp<ClipboardUpdateEventArgs>.Value { get; init; } = new();

        EventTarget<ControlClipboardMonitoringEventArgs> IEventTargetProp<ControlClipboardMonitoringEventArgs>.Value { get; init; } = new();

        #endregion

        #region Menu Handlers

        private const string FEEDBACK_URL = "https://github.com/postprintum/devcomrade/issues";
        private const string ABOUT_URL = "https://github.com/postprintum/devcomrade";

        private delegate void MenuItemEventHandler(object s, EventArgs e);

        private delegate void MenuItemSetUpdaterCallback(Action<bool> updater);

        private static void About(object? s, EventArgs e)
        {
            Diagnostics.ShellExecute(ABOUT_URL);
        }

        private static void Feedback(object? s, EventArgs e)
        {
            Diagnostics.ShellExecute(FEEDBACK_URL);
        }

        private static void EditLocalConfig(object? s, EventArgs e)
        {
            Diagnostics.ShellExecute(Configuration.LocalConfigPath);
        }

        private static void EditRoamingConfig(object? s, EventArgs e)
        {
            var path = Configuration.RoamingConfigPath;
            if (!File.Exists(path) ||
                File.ReadAllText(path)
                    .IsNullOrWhiteSpace())
            {
                // copy local config to roaming config
                var folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder!);

                File.WriteAllText(path, Configuration.GetDefaultRoamingConfig(), Encoding.UTF8);
            }

            Diagnostics.ShellExecute(path);
        }

        private void Restart(object? s, EventArgs e)
        {
            Diagnostics.StartProcess(Diagnostics.GetExecutablePath());
            Quit();
        }

        private void RestartAsAdmin(object? s, EventArgs e)
        {
            var startInfo = new ProcessStartInfo
                            {
                                UseShellExecute = true,
                                FileName = Diagnostics.GetExecutablePath(),
                                Verb = "runas"
                            };

            try
            {
                using var process = Process.Start(startInfo);
                Quit();
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != WinApi.ERROR_CANCELLED)
                    throw;
            }
        }

        private void AutoStart(object? s, EventArgs e)
        {
            // s is ToolStripMenuItem menuItem
            IsAutorun = !IsAutorun;
        }

        private void PauseFormattingRemoval(object? s, EventArgs e)
        {
            IsFormattingRemovalPaused = !IsFormattingRemovalPaused;
        }

        private void Exit(object? s, EventArgs e)
        {
            Quit();
        }

        private static (string, MenuItemEventHandler?, MenuItemSetUpdaterCallback?) GetSeparatorMenuItem()
        {
            return ("-", null, null);
        }

        #endregion

        #region IContainer

        public ComponentCollection Components => _componentContainer.Components;

        public void Add(IComponent? component)
        {
            _componentContainer.Add(component);
        }

        public void Add(IComponent? component, string? name)
        {
            _componentContainer.Add(component, name);
        }

        public void Remove(IComponent? component)
        {
            _componentContainer.Remove(component);
        }

        #endregion

        #region IsFormattingRemovalPaused

        private CancellationTokenSource? _formattingRemovalPausedCts;

        private bool _isFormattingRemovalPaused;

        internal bool IsFormattingRemovalPaused
        {
            get => _isFormattingRemovalPaused;
            set
            {
                if (value != _isFormattingRemovalPaused)
                    setValueAsync()
                        .IgnoreCancellations();

                async Task setValueAsync()
                {
                    _formattingRemovalPausedCts?.Cancel();
                    _isFormattingRemovalPaused = value;

                    RaisePropertyChange(nameof(IsFormattingRemovalPaused));

                    if (!value)
                        return;

                    _formattingRemovalPausedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(Token);

                    await Task.Delay(TimeSpan.FromMinutes(PauseFormattingRemovalTimeout),
                                     _formattingRemovalPausedCts.Token);

                    _isFormattingRemovalPaused = false;
                }
            }
        }

        #endregion

        #region IsAutorun

        private const string AUTORUN_REGKEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        internal bool IsAutorun
        {
            get
            {
                var valueName = Application.ProductName;
                using var regKey = Registry.CurrentUser.OpenSubKey(AUTORUN_REGKEY, false);
                var value = regKey?.GetValue(valueName, string.Empty)
                                  ?.ToString();

                return value.IsNotNullNorEmpty() &&
                       File.Exists(value)        &&
                       string.Compare(Path.GetFullPath(value.Trim()),
                                      Path.GetFullPath(Diagnostics.GetExecutablePath()),
                                      StringComparison.OrdinalIgnoreCase) ==
                       0;
            }
            set
            {
                var valueName = Application.ProductName;
                using var regKey = Registry.CurrentUser.OpenSubKey(AUTORUN_REGKEY, true);
                if (regKey == null)
                    throw WinUtils.CreateExceptionFromLastWin32Error();

                if (value)
                {
                    var valueData = Diagnostics.GetExecutablePath();
                    regKey.SetValue(valueName, valueData, RegistryValueKind.String);
                }
                else
                {
                    regKey.DeleteValue(valueName);
                }

                RaisePropertyChange();
            }
        }

        #endregion
    }
}