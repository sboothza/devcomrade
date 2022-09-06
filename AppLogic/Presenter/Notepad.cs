// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AppLogic.Helpers;
using Microsoft.Win32;

namespace AppLogic.Presenter
{
    /// <summary>
    ///     A plain text editor with multi-level Undo/Redo
    ///     For now, we the legacy WebBrowser for that and will replace it
    ///     with WebView2 when it is available
    /// </summary>
    [DesignerCategory("")]
    internal class Notepad : Form
    {
        private readonly Task _initTask;

        static Notepad()
        {
            // enable HTML5, more info: https://stackoverflow.com/a/18333982/1768303
            if (LicenseManager.UsageMode != LicenseUsageMode.Runtime)
                return;

            SetWebBrowserFeature("FEATURE_BROWSER_EMULATION", 11000);
            SetWebBrowserFeature("FEATURE_SPELLCHECKING", 1);
            SetWebBrowserFeature("FEATURE_DOMSTORAGE", 1);
            SetWebBrowserFeature("FEATURE_GPU_RENDERING", 1);
            SetWebBrowserFeature("FEATURE_ENABLE_CLIPCHILDREN_OPTIMIZATION", 1);
            SetWebBrowserFeature("FEATURE_DISABLE_NAVIGATION_SOUNDS", 1);
            SetWebBrowserFeature("FEATURE_WEBOC_DOCUMENT_ZOOM", 1);
            SetWebBrowserFeature("FEATURE_WEBOC_MOVESIZECHILD", 1);
            SetWebBrowserFeature("FEATURE_96DPI_PIXEL", 1);
            SetWebBrowserFeature("FEATURE_LOCALMACHINE_LOCKDOWN", 1);
        }

        public Notepad(CancellationToken token)
        {
            Browser = new CustomWebBrowser(this)
                      {
                          Dock = DockStyle.Fill,
                          AllowWebBrowserDrop = false,
                          ScriptErrorsSuppressed = true,
                          ScrollBarsEnabled = false,
                          AllowNavigation = false,
                          IsWebBrowserContextMenuEnabled = true,
                          WebBrowserShortcutsEnabled = true
                      };

            Text = $"{Application.ProductName} Notepad";
            ShowInTaskbar = false;
            Padding = new Padding(3);
            Icon = Icon.ExtractAssociatedIcon(Diagnostics.GetExecutablePath());

            var workingArea = Screen.PrimaryScreen.WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Width = workingArea.Width   / 2;
            Height = workingArea.Height / 2;
            Left = workingArea.Left + (workingArea.Width  - Width)  / 2;
            Top = workingArea.Top   + (workingArea.Height - Height) / 2;

            KeyPreview = true;

            Controls.Add(Browser);

            async void handleFocus(object? s, EventArgs e)
            {
                await Task.Yield();
                if (!IsDisposed && Handle == WinApi.GetActiveWindow())
                    FocusEditor();
            }

            Activated += handleFocus;
            GotFocus += handleFocus;

            FormClosing += (s, e) =>
                           {
                               if (e.CloseReason == CloseReason.UserClosing)
                               {
                                   e.Cancel = true;
                                   Hide();
                               }
                           };

            _initTask = LoadAsync(token);
        }

        private WebBrowser Browser { get; }

        private Lazy<IBrowser> BrowserInstance => new(() =>
                                                          Browser.ActiveXInstance as IBrowser ??
                                                          throw new InvalidComObjectException(nameof(BrowserInstance)),
                                                      false);

        private Lazy<IDocument> Document => new(() =>
                                                    BrowserInstance.Value.Document ??
                                                    throw new InvalidComObjectException(nameof(Document)),
                                                false);

        private Lazy<ITextArea> EditorElement => new(() =>
                                                         Document.Value.getElementById("editor") as ITextArea ??
                                                         throw new InvalidComObjectException(nameof(EditorElement)),
                                                     false);

        public bool IsReady => _initTask.IsCompleted && Browser.ReadyState == WebBrowserReadyState.Complete;

        public string? EditorText => IsReady ? EditorElement.Value.value : null;

        public event EventHandler? ControlEnterPressed;

        internal void OnControlEnterPressed()
        {
            ControlEnterPressed?.Invoke(this, EventArgs.Empty);
        }

        private static void SetWebBrowserFeature(string feature, uint value)
        {
            using var process = Process.GetCurrentProcess();
            using var key = Registry.CurrentUser.CreateSubKey(string.Concat(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\", feature),
                                                              RegistryKeyPermissionCheck.ReadWriteSubTree);

            var appName = Path.GetFileName(process!.MainModule!.FileName);
            key.SetValue(appName, value, RegistryValueKind.DWord);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Hide();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async Task LoadAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var scope = SubscriptionScope<WebBrowserDocumentCompletedEventHandler>.Create((s, e) => tcs.TrySetResult(DBNull.Value),
                                                                                                handler => Browser.DocumentCompleted += handler,
                                                                                                handler => Browser.DocumentCompleted -= handler);

            using var rego = token.Register(() => tcs.TrySetCanceled());
            Browser.DocumentText = HTML_SOURCE;

            await tcs.Task;
        }

        public void FocusEditor()
        {
            if (IsReady)
            {
                Browser.Focus();
                EditorElement.Value.focus();
            }
        }

        public Task WaitForReadyAsync(CancellationToken token)
        {
            return _initTask.WithCancellation(token);
        }

        private bool ExecCommand(string command, bool showUI, object value)
        {
            return IsReady && Document.Value.execCommand(command, showUI, value);
        }

        public bool SelectAll()
        {
            if (!IsReady)
                return false;

            EditorElement.Value.createTextRange()
                         .select();

            return true;
        }

        public bool Paste(string? text)
        {
            if (!IsReady)
                return false;

            var range = EditorElement.Value.createTextRange();
            if (range.text != text)
            {
                range.text = text ?? string.Empty;

                range = EditorElement.Value.createTextRange();
                range.select();

                return true;
            }

            return false;
        }

        #region WebBrowser stuff

        private const string HTML_SOURCE = @"
        <!doctype html>
        <head>
            <style>
                html, body { width: 100%; height: 100% }
                body { border: 0; margin: 0; padding: 0; }
                textarea { 
                    width: 100%; height: 100%; overflow: auto; 
                    font-family: Consolas; font-size: 14px;
                    border: 0; margin: 0; padding: 0
                }
            </style>
        </head>
        <body>
            <textarea id='editor' spellcheck='false' wrap='off' tabIndex='1'></textarea>
        </body>
        ";

        private class CustomWebBrowser : WebBrowser
        {
            private readonly Notepad _parent;

            public CustomWebBrowser(Notepad parent)
            {
                _parent = parent;
            }

            protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
            {
                if (e.Modifiers == Keys.Control && e.KeyCode == Keys.Enter)
                {
                    e.IsInputKey = true;
                    _parent.OnControlEnterPressed();
                    return;
                }

                // ignore these keys
                if (e.Control && e.KeyCode is Keys.N or Keys.L or Keys.O or Keys.P || e.KeyCode == Keys.F5)
                {
                    e.IsInputKey = true;
                    return;
                }

                base.OnPreviewKeyDown(e);
            }
        }

#pragma warning disable CS0618 // InterfaceIsIDispatch is obsolete
        private const string IID_IDispatch = "00020400-0000-0000-C000-000000000046";

        [ComVisible(true)]
        [Guid(IID_IDispatch)]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IDocument
        {
            IElement getElementById(string id);
            bool execCommand(string command, bool showUI, object value);
            ISelection selection { get; }
        }

        [ComVisible(true)]
        [Guid(IID_IDispatch)]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IElement
        {
            void focus();
        }

        [ComVisible(true)]
        [Guid(IID_IDispatch)]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface ITextArea
        {
            void focus();
            string value { get; set; }
            IRange createTextRange();
        }

        [ComVisible(true)]
        [Guid(IID_IDispatch)]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IBrowser
        {
            IDocument Document { get; }
            int ReadyState { get; }
        }

        [ComVisible(true)]
        [Guid(IID_IDispatch)]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface ISelection
        {
            IRange createRange();
        }

        [ComVisible(true)]
        [Guid(IID_IDispatch)]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IRange
        {
            string text { get; set; }
            void collapse(bool start);
            void select();
        }

#pragma warning restore CS0618 // Type or member is obsolete

        #endregion
    }
}