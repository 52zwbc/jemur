# Memory

Jemur runs all day in the notification area, so its footprint matters more than its peak. This page lists what it uses, why, and how to measure it yourself.

## Numbers

Measured on Windows 11 (build 26300), 1920 × 1080 at 125 % scaling, .NET 10, Release build with ReadyToRun.

*Private working set* is what Task Manager shows in the **Memory** column. *Private bytes* is the memory committed to the process, whether or not it is currently in RAM.

| State | Private working set | Private bytes |
|---|---|---|
| Empty line, idle (tucked away) | 1.7 MB | 63 MB |
| Empty line, line showing | 30 MB | 84 MB |
| Empty line, tucked away again | 3.4 MB | 97 MB |
| Empty line, control panel open | 55 MB | 138 MB |
| 7 items, idle (tucked away) | 2.9 MB | 107 MB |
| 7 items, line showing | 32 MB | 118 MB |
| 7 items, tucked away again | 2.6 MB | 113 MB |
| 7 items, control panel open | 82 MB | 170 MB |

For comparison, the first prototype, which used WinForms for the tray icon and screen capture plus `DropShadowEffect` on every card, sat at about 65 MB in Task Manager and 190 MB private bytes while idle.

Most of the remaining cost is WPF's own rendering stack (Direct3D, font caches, the composition engine), not Jemur's data: the managed heap is about 2 MB with a full line. Anything visible on screen has to be in RAM, so the numbers rise while you are using Jemur and fall again once it tucks away.

## What keeps it small

| Choice | Effect |
|---|---|
| No WinForms or System.Drawing. The tray icon uses `Shell_NotifyIcon`, capture uses GDI `BitBlt`, and monitors come from `MonitorFromPoint`. | Two large frameworks are never loaded (≈15 MB). |
| Thumbnails are decoded at their on-screen size (`DecodePixelWidth`), and the line and the control panel share one weakly cached copy. | A 4K screenshot costs about 0.3 MB on the line instead of 33 MB. |
| Cards fake their shadow with three translucent borders instead of `DropShadowEffect`. | No off-screen surface per card on every frame of a layered window. |
| The pendulum animation stops listening to `CompositionTarget.Rendering` once every card is still. | Zero CPU and no frame churn while idle. |
| When tucked away, the line window shrinks to a 36 px strip. | The layered window's back buffer drops from about 3.6 MB to about 0.4 MB. |
| After a capture, after closing the control panel, spotlight or mark-up editor, and after the line tucks away, Jemur runs a compacting GC and calls `SetProcessWorkingSetSizeEx(-1, -1)`. | Pages Jemur no longer touches go back to Windows. They return on demand, from the standby list, without hitting the disk. |
| Workstation GC, non-concurrent, `ConserveMemory = 7`, no tiered PGO. ReadyToRun on release builds. | Smaller heap and no background GC thread. Less JIT-compiled code in private memory. |

## Measure it yourself

Task Manager's **Memory** column is good enough for a quick look. For the numbers above:

```powershell
$p = Get-Process Jemur
(Get-Counter "\Process(Jemur)\Working Set - Private").CounterSamples.CookedValue / 1MB   # Task Manager "Memory"
$p.PrivateMemorySize64 / 1MB                                                             # private bytes
```

Set `JEMUR_DIAG=1` before starting Jemur to log managed-heap vs. process numbers at startup and when the control panel opens. They are written to `%LOCALAPPDATA%\Jemur\logs`.

To compare runs without touching your own data, point Jemur at a scratch folder:

```powershell
$env:JEMUR_DATA = "$env:TEMP\jemur-bench"
.\Jemur.exe
```

## Things we tried that didn't help

- **Software rendering** (`RenderMode.SoftwareOnly`) for the whole process raised idle memory by about 35 MB, because every surface then lives in system RAM.
- **Software rendering or no Mica for the control panel alone** changed nothing measurable.
