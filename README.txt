WoW Window Tiler 1.5
====================

Tiles your World of Warcraft clients on one monitor (or two) - 16:9, ultrawide
or super-ultrawide - keeps every window 16:9, and swaps any client into the
big "main" slot with a Shift+click or a hotkey. Made because resizing clients
by hand got old.

It only moves and resizes windows. It never sends keys or clicks to the game,
so it is not multiboxing software - nothing is broadcast or automated.

Works with every current client: Retail, Classic Era, Classic progression
(Cata/MoP), WoW Forever, PTR and Beta builds. Windows 10/11. No install, no admin.

FILES
-----
  WowTiler.exe   the app (double-click to run)
  WowTiler.ini   your settings (created on first run)
  README.txt     this file
  WowTiler.cs    source code
  WowTiler.ico   app icon
  build.bat      rebuilds the exe from the source with the C# compiler that
                 ships with Windows (no Visual Studio needed)

FIRST-TIME SETUP (once per client)
----------------------------------
Each client must be in plain "Windowed" mode - not "Windowed (Fullscreen)",
not "Fullscreen". In those modes the game renders at a fixed resolution and
stretches it, so a client that gets enlarged looks blurry (or can't be moved).

  Easiest: click "Fix display mode" in the tiler. It writes the setting into
           each client's Config.wtf (a .wowtiler-backup copy is kept), then
           restart the clients - the game reads Config.wtf at startup.
  By hand: in each client  Esc > System > Graphics > Display Mode = Windowed
           > Apply > Okay   (click Okay within 5 seconds or it reverts)

Clients in the wrong mode show in red in the client list.

EVERYDAY USE
------------
  Shift + click into an alt     it becomes the main      (key is configurable)
  Ctrl+Alt+Right / Left         next / previous client becomes the main
  Ctrl+Alt+1 ... 9              that slot becomes the main
  Ctrl+Alt+Home                 re-tile everything
  Ctrl+Alt+End                  Raid mode on / off

All hotkeys can be changed on the Hotkeys tab: click a box, press the
combination you want. Backspace clears one. Changes apply immediately.

New clients that log in are picked up and tiled automatically; closed ones
drop out and the rest re-tile. Labels you type next to each client are
remembered and reapplied next session in login order.

Untick "TILE" next to a client to leave it alone - it will not be moved or
resized (handy for a raid client on another monitor).

RAID MODE
---------
Makes the current main full-monitor and borderless: looks like Windowed
(Fullscreen) but renders at full resolution. The same key brings the tiled
layout back. Alts stay behind the main, or get minimized if you tick that
option on the Behavior tab. Nothing re-tiles while raid mode is on.

LAYOUT (Layout tab)
-------------------
The layout is worked out for you. For your monitor - 16:9, 21:9 ultrawide or
32:9 super-ultrawide - and however many clients are open, the tiler tries
every arrangement and keeps the one that leaves the least empty screen, with
every window at the chosen aspect. You only say what you want:

  Main goes           where the big window sits:
                        Automatic   whichever side wastes the least screen
                        Left        alts on the right
                        Right       alts on the left
                        Top         alts below
                        Bottom      alts above
  Mains               how many big windows (1-4). Promoting an alt replaces
                      the main promoted longest ago.
  Alt size            Balanced                  good main, alts still usable
                      Small alts, biggest main  the main gets all it can
                      Large alts                alts at least half the main
  Aspect              16:9 by default; other ratios, or free (stretch to fill).

The preview shows the result, and the status bar names the arrangement that
was picked. Drag a client onto another in the preview to swap them;
double-click one to make it the main.

  Customize layout    under the preview. Drag and resize the slots yourself,
                      add or delete slots; the first N slots are the mains.
                      Click "Done editing" when you are happy.
  Back to automatic   drops the custom layout and lets the tiler work it out
                      again.

  Monitor             which monitor to tile on.
  Alts on             put the alts on a second monitor; the main then gets
                      its whole monitor and the alts share a grid.
  Keep free           pixels kept empty at the top / bottom / left / right
                      edge - for a stream overlay, dock, or second taskbar.
  Gap                 spacing between windows.
  Cover the taskbar   tile over the taskbar as well.
  Borderless          strips the title bars from the clients.

BEHAVIOR TAB
------------
  Auto-tile, focus-on-swap, keep-on-top, raid-minimizes-alts, click-swap on/off,
  Start with Windows (shortcut in your Startup folder; starts in the tray),
  Start minimized to the tray, resize method (leave on the default),
  Check for updates.

TRAY
----
Closing or minimizing the window sends it to the system tray. Right-click the
tray icon for the main actions and Exit; double-click (or run the exe again)
to open the window.

UPDATING
--------
The tiler checks GitHub for a newer version when it starts (and every few
hours) and asks before installing it. "Check for updates" on the Behavior tab
does it on demand. By hand: drop a new build next to the exe as
"WowTiler.new.exe" and it is swapped in the next time the tiler starts.

REBUILDING
----------
Edit WowTiler.cs, double-click build.bat (exit the tiler first).
