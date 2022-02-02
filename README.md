# What is it?

`#DevComrade` is a free and open-source Windows copy/paste/run productivity improvement tool for developers.
Branched from https://github.com/postprintum/devcomrade
Added some stuff - needs a cleanup before merge back

# What's new

- Simply use <kbd>Ctrl</kbd>+<kbd>V</kbd> for pasting plain, formatting-stripped text into any Windows application, system-wide.

  By default, `DevComrade` now monitors Windows Clipboard for text with rich formatting (coming from HTML, RTF, PDF, Word documents etc.) and seamlessly replaces it with plain text, ready to be pasted anywhere. To achieve this, `DevComrade` uses [Win32 Clipboard Monitoring API](https://docs.microsoft.com/en-us/windows/win32/dataxchg/using-the-clipboard#monitoring-clipboard-contents). This behavior can be switched on/off with <kbd>Win</kbd>+<kbd>F10</kbd> menu or via the [`.config` file](https://github.com/postprintum/devcomrade/blob/main/DevComrade/App.config).
- The new built-in Clipboard Notepad:
  - Press <kbd>Alt</kbd>+<kbd>Ins</kbd> to edit the clipboard text with DevComrade's built-in Notepad.
  - Press <kbd>Control</kbd>+<kbd>Enter</kbd> to close the built-in Notepad and save its content into the Clipboard.
  - Press <kbd>Esc</kbd> to close it without saving.
- <kbd>Win</kbd>+<kbd>Ins</kbd> to paste as unformatted single-line text (with line breaks removed, for pasting into CLI).
- <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>Ins</kbd> to paste as unformatted multi-line text.
- <kbd>Shift</kbd>+<kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>Ins</kbd> to paste only a number (e.g., a credit card or bank account number).
- <kbd>Ctrl</kbd>+<kbd>Win</kbd>+<kbd>Ins</kbd> to wrap the clipboard text with `<pre>` tag, e.g. for pasting code into Microsoft Teams' chat as HTML ([sometimes nothing else really works](https://twitter.com/search?q=%40MicrosoftTeams%20paste%20formatting&f=live)).

# Introduction

Copy-pasting from the online docs, StackOverflow or numerous blogs can be a tedious and sometimes even a dangerous task. Does the following sound familiar: you paste some text from a web page into a Terminal command line, and it gets executed immediately, before you even have a chance to edit it? Only because there was a CR/LF character at the end of the clipboard text.

Or, have you ever been annoyed with some broken formatting, indentation, inconsistent tabs/spaces when you paste a piece of code into Visual Studio Code editor, a blog post or an email message? A typical workaround for that is to use the good old `Notepad.exe` as a buffer.

Now I have two dedicated hotkeys for that, **<kbd>Win</kbd>+<kbd>Ins</kbd> (paste as single line) and <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>Ins</kbd> (paste as multiple lines)**, which uniformly work across all apps and browsers.

# Launch other applications with a custom hotkey

One other source of disappointment for me has always been how custom keyboard hotkeys work with Windows Shell shortcuts. It is a common struggle to find a convenient hotkey combination that still can be assigned to start a custom app. E.g., it is impossible to use <kbd>Win</kbd>+<kbd>Shift|Alt|Ctrl</kbd>+<kbd>Key</kbd> combos for that. And when it *can* be assigned, [it may take up to 10 seconds](https://superuser.com/q/426947/246232) for the program to actually start when the hotkey is pressed.

`DevComrade` has been made to solve this problem, too. It allows assigning a customizable action to (almost) any hotkey combination, and comes with an extensive set of predefined actions for pasting text and launching apps. By default, it has hotkeys for Windows Terminal and VSCode.

# Custom actions and scriptlets

Additional actions can be added as [C# scriptlets](https://github.com/dotnet/roslyn/blob/master/docs/wiki/Scripting-API-Samples.md) in the [`.config` file](https://github.com/postprintum/devcomrade/blob/main/DevComrade/App.config). E.g., generating a GUID:

```XML
<hotkey name="InsertGuid" menuItem="Insert &amp;Guid" isScript="true">
    <!-- this is an example of a C# scriptlet handler -->
    <![CDATA[
        await Host.FeedTextAsync(Guid.NewGuid().ToString("B").ToUpper(), Token);
    ]]>
</hotkey>
```
# How does it work?

When it comes to pasting text, `DevComrade` is different from many similar utilities (e.g., from the still-excellent [Puretext](https://stevemiller.net/puretext/)) in how it uses [Win32 simulated input API](https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) to elaborately feed the text into the currently active window, character by character, as though it was typed by a person. For example, it works well with Google's [Secure Shell Chrome extension](https://chrome.google.com/webstore/detail/secure-shell/iodihamcpbpeioajjeobimgagajmlibd?hl=en).

# Work in progress

`DevComrade` is a free and open-source software maintained under [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0). It's built with [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) and uses Windows Forms for its very simple context menu UI.

I consider it stable, **but it is still very much work in progress**. Eventually, some CI logic for publishing a Chocolatey package (including a code-signed executable) will be implemented. Meanwhile, feel free to build and run it directly from the source code. DevComrade does not collect any kind of telemetry, and never will.

# Try it out from the source code (it's super easy):

- Download and install [.NET 6.0 SDK](https://download.visualstudio.microsoft.com/download/pr/0f71eaf1-ce85-480b-8e11-c3e2725b763a/9044bfd1c453e2215b6f9a0c224d20fe/dotnet-sdk-6.0.100-win-x64.exe
), if you haven't got it installed already. That's the only needed prerequisite tool. Visual Studio or Visual Studio Code aren't required to build this app.

- Download and unzip [the source](https://github.com/postprintum/devcomrade/archive/main.zip), or use `git` to clone the repo to a folder of your choice, e.g.:
    ```
    mkdir DevComradeRepo && cd DevComradeRepo
    git clone https://github.com/postprintum/devcomrade .
    ```
- Build and run:
    ```
    .\Package\make-and-run.bat
    ```
- Or do that manually:
    ```
    dotnet publish -r win10-x64 -c Release --self-contained .\DevComrade

    start .\DevComrade\bin\Release\net6.0-windows\win10-x64\DevComrade.exe
    ```
Once started, `DevComrade` shows up as <img src="./Art/BulbIcon.ico" alt="DevComrade Icon" height="16"/> icon in the system tray. Some of the features to try out:

- Press <kbd>Win</kbd>+<kbd>F10</kbd> to see the list of the available shortcuts and actions.
- Copy some code into the Clipboard and try <kbd>Alt</kbd>+<kbd>Ins</kbd>, to see it pasted into the instant internal Notepad pop-up window. Press <kbd>Esc</kbd> to simply hide it when finished, or <kbd>Win</kbd>+<kbd>Win</kbd>+<kbd>N</kbd> to open it again.
- Press <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>E</kbd> to open Windows Terminal then <kbd>Win</kbd>+<kbd>Ins</kbd> to paste the Clipboard's content as a single line of text. It won't get executed until your press <kbd>Enter</kbd>.
- Copy any URL into clipboard (e.g., from a console window output, spaces and broken lines are OK), then press <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>O</kbd> to open it in your default web browser.

This tool has been working well for my own personal needs, but outside that its future depends on your feedback. Feel free to [open an issue](https://github.com/postprintum/devcomrade/issues) or [send me a DM on Twitter](https://twitter.com/noseratio).

<hr>

<img src="./Art/menu.jpg" alt="DevComrade Win+F10 Menu" width="800"/>

<hr>

<img src="./Art/notepad.jpg" alt="DevComrade Alt+Ins Notepad" width="800"/>
