# What It Changes

GamingStack only changes Windows settings **after you say yes**, and it shows each setting's *real current
value* and what it would become before you decide. Every change is written to an
[undo script](Safety-and-Undo).

## Every setup type

| Setting | Becomes |
|---|---|
| File extensions | shown |
| Hidden files | shown |
| Right-click menu | the classic full menu |
| Taskbar widgets button / search box / Copilot button | hidden |
| Start-menu suggestions and ads | hidden |
| Delivery Optimization (peer-to-peer update sharing) | off (HTTP only) |
| Telemetry service (DiagTrack) | disabled |

## Gaming only

| Setting | Becomes |
|---|---|
| Game Mode | on |
| Hardware-accelerated GPU scheduling | on |
| Power plan | High performance or Ultimate Performance (your choice) |
| Background CPU reservation (MMCSS) | 0% |
| Fast Startup | off |
| Windows Update active hours | 16:00-23:00 |
| Xbox Game Bar / Game DVR | off |
| Nagle's algorithm (network latency) | off, on your active adapter |
| Mouse acceleration ("Enhance pointer precision") | off |
| CPU core parking | off |
| Foreground app priority | boosted |

## Developer only

| Setting | Becomes |
|---|---|
| Long file paths (over 260 characters) | on |
| Developer Mode | on |

## Optional extras

- **DNS servers**: Cloudflare, Google or Quad9 for your active adapter. The old setting (automatic or
  your own servers) is saved so it can be restored. Default is "keep mine".
- **Remove preinstalled apps**: News, Weather, Clipchamp, Solitaire, Teams (consumer), the Microsoft 365
  hub, TikTok, Disney+, Spotify (Store version). Only apps actually on your PC are offered, nothing is
  ticked by default, and the undo script reinstalls them from the Store.
- **Windows cleanup** (nothing ticked by default; only your own user's settings; only ones that would
  actually change are offered). Each is shown with a plain-words effect and its current value:

  | Setting | What changes |
  |---|---|
  | End Task on the taskbar | Right-click a taskbar app to close it in one click |
  | No web results in Start search | Start search only looks at your PC |
  | No tips or ads on the lock screen | Hides promotional tips |
  | No suggestions in Settings and notifications | Hides "suggested" content and tip notifications |
  | Stop quietly installing "suggested" apps | Windows no longer adds promoted/OEM apps in the background |
  | No "Finish setting up your PC" nags | Stops the repeating setup prompts |
  | Advertising ID and tailored experiences off | Apps and Windows stop using ad profiling |
  | File Explorer opens to This PC | Starts on your drives, not "Home" |
  | Hide the Task View button | Win+Tab and Alt+Tab still work |

  Inspired by the [TBOK Windows Performance Optimizer](https://github.com/TheBeardofKnowledge/TBOK-Win11Optimizer)
  by The Beard of Knowledge (no code copied; it has no licence). Some changes appear after you sign out and in.
