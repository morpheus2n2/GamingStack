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
