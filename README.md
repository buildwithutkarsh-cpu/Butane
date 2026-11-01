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

- Multi-tab browsing: add with "+ NEW", close with the x, and click a tab to switch
- Multiple windows: use WINDOW / PRIVATE buttons or Ctrl+N / Ctrl+Shift+N
- Normal remembers first-party site data such as logins and preferences, while WebView2
  Balanced tracking prevention and the Butane Shield block known trackers
- Use ALLOW CURRENT SITE to bypass Butane's tracker blocklist for a site; WebView2's own
  tracking prevention still applies. Exceptions are saved in the normal profile.
- Private windows use an isolated, in-memory WebView2 profile; closing the window disposes its tabs
  so WebView2 discards the session without deleting a profile directory that may still be locked.
  Private remembers nothing locally, but websites, your network, and your ISP may still see activity.
- Keyboard shortcuts: Ctrl+T (new tab), Ctrl+W (close tab), Ctrl+Shift+W (close window),
  Ctrl+Tab / Ctrl+Shift+Tab
  (switch tabs), Ctrl+1–8 (select tab), Ctrl+9 (last tab), and Ctrl+L (address bar)
- Top bar: type a web address or search with DuckDuckGo
- Browser DNS-over-HTTPS: Cloudflare in secure mode
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
3. Tab sleeping.
4. Settings, bookmarks, history.