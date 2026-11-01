# Butane

Butane is a Windows desktop browser built with C#, WPF, and WebView2 (the Microsoft Edge
engine). It has a dark monospace interface with a sidebar for tabs.

## Run it

1. Install the **.NET 8 SDK**: https://dotnet.microsoft.com/download
2. Open a terminal in this folder and run:

       dotnet run

The WebView2 Runtime is generally installed with Windows 10/11. If the browser engine
does not start, install the Evergreen WebView2 Runtime from Microsoft's WebView2 page.
You can also open `Butane.csproj` in Visual Studio 2022 and press F5.

## Build an installer

Install Inno Setup 6, then run this from PowerShell:

    .\BuildInstaller.ps1

The script publishes a self-contained `win-x64` build and creates
`dist\Butane-Setup-1.0.0.exe`. To set another release version, pass for example
`-AppVersion 1.2.0`. The installer creates Start Menu shortcuts, optionally creates a desktop
shortcut, and registers an uninstaller in Windows Settings under **Installed apps**. Uninstalling
removes the application but preserves your browser data in `%LOCALAPPDATA%\Butane`.

The .NET runtime is included. The Microsoft Edge WebView2 Runtime is still required; install the
Evergreen WebView2 Runtime from Microsoft's WebView2 page if it is missing.

## Features

- Multi-tab browsing: use **+ NEW**, close tabs with **x**, and select tabs in the sidebar.
- Background tabs are suspended to reduce activity and memory use; selecting a sleeping tab
  resumes it. Tabs stay awake while one of their popup windows is open.
- Website popups open in linked tabs, preserving `window.opener` for sign-in and other
  cross-window flows. Closing the popup releases the opener tab.
- Multiple normal and private windows: use the **WINDOW** and **PRIVATE** buttons.
- Search the web with DuckDuckGo or enter a web address in the address bar.
- Cloudflare DNS-over-HTTPS in secure mode.
- Butane Shield blocks requests to a small built-in list of known ad and tracker hosts.
  The **BLOCKED** counter shows blocked requests. This starter blocklist is not a replacement
  for comprehensive filter lists.
- **ALLOW CURRENT SITE** adds or removes a site exception for Butane Shield. Exceptions are
  saved in the normal profile. WebView2 tracking prevention continues to apply to exceptions.
- Settings are available from the sidebar and persist in
  `%LOCALAPPDATA%\Butane\settings.json`. Choose whether to enable Butane Shield, select
  WebView2 tracking prevention (**Off**, **Basic**, **Balanced**, or **Strict**), and optionally
  clear cookies when the last normal window closes. Cookie cleanup does not clear other site data.
- Private windows use isolated, in-memory WebView2 profiles. Closing a private window discards
  its session. Private browsing does not hide activity from websites, your network, or your ISP.
- Bookmark pages with **SAVE** or `Ctrl+D`; manage saved pages from the bookmarks library.
- Successful normal-window visits appear in **HISTORY**, where entries can be reopened, removed,
  or cleared. Private-window browsing is not recorded.
- Downloads start automatically in the Downloads folder using the suggested filename (or a random
  name if it is too long), and appear in **DOWNLOADS** with progress, cancel, open, and
  show-in-folder actions. Normal-window download records are saved; private-window records remain
  only for that window session.
- Normal-window bookmarks, browsing history, and download records are stored in
  `%LOCALAPPDATA%\Butane\library.json`.
- Bottom toolbar with page load status, **BACK**, **FORWARD**, and **RELOAD**.
- Dark new-tab page.

### Keyboard shortcuts

- `Ctrl+T` — new tab
- `Ctrl+W` — close tab
- `Ctrl+Shift+W` — close window
- `Ctrl+N` — new normal window
- `Ctrl+Shift+N` — new private window
- `Ctrl+Tab` / `Ctrl+Shift+Tab` — switch tabs
- `Ctrl+1`–`Ctrl+8` — select a tab by position
- `Ctrl+9` — select the last tab
- `Ctrl+L` — focus the address bar
- `Ctrl+D` — bookmark the current page

## Project files

- `MainWindow.xaml` and `MainWindow.xaml.cs` — browser layout and behavior
- `BrowserTab.cs` — per-tab WebView2 and sidebar state
- `BrowserSettings.cs` — saved browser preferences
- `BrowserLibraryStore.cs` — persistent bookmarks, history, and download records
- `LibraryWindow.xaml` and `LibraryWindow.xaml.cs` — bookmarks, history, and downloads manager
- `SettingsWindow.xaml` and `SettingsWindow.xaml.cs` — cookie and tracking settings
- `App.xaml` and `App.xaml.cs` — application resources and startup
- `Butane.csproj` — project settings and WebView2 dependency
- `butane.ico` and the matching PNG/SVG files — application, window, and installer branding

## Not implemented yet

- Comprehensive filter lists such as EasyList or EasyPrivacy
