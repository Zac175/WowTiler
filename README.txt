WoW Window Tiler 1.2
====================

Tiles your World of Warcraft clients on one monitor (or two), keeps every
window 16:9, and swaps any client into the big "main" slot with a Shift+click
or a hotkey. Made because resizing clients by hand got old.

It only moves and resizes windows. It never sends keys or clicks to the game,
so it is not multiboxing software - nothing is broadcast or automated.

Works with every current client: Retail, Classic Era, Classic progression,
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

LAYOUTS (Layout tab)
--------------------
  L-shape (default)   main + alts all exactly 16:9; alts fill the side column
                      top to bottom, then along the bottom. With 4 alts on a
                      16:9 monitor: main 2/3 of the width, alts 1/3.
  Main left/right     main fills the height, alts stack beside it.
  Main top/bottom     main fills the width, alts in a strip.
  Even grid           everything the same size.
  Mains top / left    several big "main" slots sharing the big region (set
                      how many with "Mains"), alts packed smaller beside them.
                      Promoting an alt replaces the main promoted longest ago.

  Alts on             put the alts on a second monitor; the main then gets
                      its whole monitor and the alts share a grid.
  Keep free           pixels kept empty at the Top / Bottom / Left / Right
                      edge - for a stream overlay, dock, or second taskbar.
  Main size           nudges how big the main is where the layout allows.
  Mains               number of big slots (Mains layouts and Custom: the
                      first N slots you draw are the mains).
  Fill the screen     uses every pixel but lets the main go off-ratio.
  Gap                 spacing between windows.
  Borderless          strips the title bars from the clients.

BEHAVIOR TAB
------------
  Auto-tile, focus-on-swap, keep-on-top, raid-minimizes-alts, click-swap on/off,
  Start with Windows (shortcut in your Startup folder; starts in the tray),
  Start minimized to the tray, resize method (leave on the default).

TRAY
----
Closing or minimizing the window sends it to the system tray. Right-click the
tray icon for the main actions and Exit; double-click (or run the exe again)
to open the window.

UPDATING
--------
Drop a new build next to the exe as "WowTiler.new.exe". The next time the
tiler starts it swaps the new one in by itself.

REBUILDING
----------
Edit WowTiler.cs, double-click build.bat (exit the tiler first).
