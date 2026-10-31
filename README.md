# Butane (starter)

A fast, private Windows browser. Dark monospace interface with a sidebar for tabs.
Built with C#, WPF and WebView2 (the Microsoft Edge engine).

## Run it

1. Install the **.NET 8 SDK**: https://dotnet.microsoft.com/download
2. Open a terminal in this folder and run:

       dotnet run

WebView2 Runtime is already installed on Windows 10/11. If you get an engine error,
install the "Evergreen" runtime from Microsoft's WebView2 page.

You can also open `Butane.csproj` in Visual Studio 2022 and press F5.

## What works now

- Sidebar tabs: add with "+ NEW", close with the x, click to switch
- Top bar: type a web address or a search (DuckDuckGo)
- BLOCKED counter and the SHIELD toggle (small starter blocklist of ad and tracker hosts)
- Bottom bar: load time, BACK, FORWARD, RELOAD
- Dark new tab page

## Files

- `MainWindow.xaml` - the layout (sidebar, top bar, page area, bottom bar)
- `MainWindow.xaml.cs` - the logic (tabs, navigation, blocking)
- `BrowserTab.cs` - one tab (its WebView2 and sidebar text)
- `App.xaml` - colors, font and control styles
- `App.xaml.cs` - app startup
- `Butane.csproj` - project settings and the WebView2 package

## Next steps

1. Put the project in git (`git init`) before changing anything.
2. Replace the starter blocklist with real filter lists (EasyList, EasyPrivacy).
3. Keyboard shortcuts (Ctrl+T, Ctrl+W, Ctrl+L).
4. Tab sleeping.
5. Settings, bookmarks, history.