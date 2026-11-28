//! Problem marks and the game's worst moments, in the session log, in every build.
//!
//! **Marks.** A player who sees a problem presses Numpad 1-9 (or Alt+1-9, for keyboards without a numpad; the
//! number row alone also moves the title's D-pad) while our window has the focus: a `MARK` line goes into the log
//! with what kind of problem it is and the moment's numbers (frame rate, longest frame, video memory, RAM, minutes
//! played), a short beep says it was taken, and the numbers panel (F3) says so for a few seconds. The launcher's
//! summary and `scripts/report-stats.py` count them by kind, so the worst places of the game show from the players'
//! own reports.
//!
//! **Hitches and drops**, found without anyone pressing anything: a `HITCH` line when frames take far longer than
//! the ones around them (at most one line every 5 s, summing what happened in it), and an `FPS DROP` line when the
//! frame rate of the last 5 s falls under 60 % of the half minute before. Not in the first minute (loading).

use std::collections::VecDeque;
use std::sync::Mutex;
use std::time::{Duration, Instant};

use windows_sys::Win32::UI::Input::KeyboardAndMouse::{GetAsyncKeyState, VK_MENU, VK_NUMPAD1};
use windows_sys::Win32::UI::WindowsAndMessaging::{GetForegroundWindow, GetWindowThreadProcessId};

/// The kinds of problem, by key 1-9: a short name for the log and the panel.
pub(super) const KINDS: [&str; 9] = [
    "VISUAL",       // 1: stretched bodies, wrong colours, flickering, things drawn wrong
    "TEXTURES",     // 2: black, missing or blurry textures
    "CROWD FPS",    // 3: low frame rate near people, crowds, fights
    "STUTTER",      // 4: hitches, short freezes, lag
    "FAST FPS",     // 5: low frame rate driving, flying or moving fast
    "AUDIO",        // 6: sound cut, missing or crackling
    "INPUT",        // 7: keys, mouse or pad not answering
    "GAMEPLAY",     // 8: stuck, odd physics, a mission that fails
    "OTHER",        // 9: anything else, or the game seems about to close
];

/// How long the panel shows that a mark was taken.
const BANNER_FOR: Duration = Duration::from_secs(4);
/// No hitch or drop lines before this much of the run (the start loads).
const QUIET_START: Duration = Duration::from_secs(60);
/// At most one hitch line in this time.
const HITCH_EVERY: Duration = Duration::from_secs(5);
/// At most one drop line in this time.
const DROP_EVERY: Duration = Duration::from_secs(10);
/// Frame times kept (about 10 s at 30 fps).
const KEPT: usize = 300;

struct State {
    started: Instant,
    last: Option<Instant>,
    /// Recent frame times (ms), newest last.
    times: VecDeque<f32>,
    keys: [bool; 9],
    banner: Option<(String, Instant)>,
    /// Hitches since the last hitch line: how many, the longest, and when that line was.
    hitches: (u32, f32),
    hitch_said: Instant,
    /// Frames and time in the current 5 s, and the frame rate of the half minute before (0: none yet).
    window: (u32, Instant),
    before: f64,
    drop_said: Instant,
    marks: [u32; 9],
}

static STATE: Mutex<Option<State>> = Mutex::new(None);

fn ours_in_front() -> bool {
    let mut process = 0;
    // SAFETY: plain queries; room for the answer.
    unsafe { GetWindowThreadProcessId(GetForegroundWindow(), &mut process) };
    process == std::process::id()
}

fn down(vk: u16) -> bool {
    // SAFETY: a plain query.
    unsafe { GetAsyncKeyState(i32::from(vk)) < 0 }
}

/// The median and the longest of the last `ms` milliseconds of frames, and their frame rate.
fn recent(times: &VecDeque<f32>, ms: f32) -> (f32, f32, f64) {
    let mut sum = 0.0;
    let mut taken = Vec::new();
    for &t in times.iter().rev() {
        if sum >= ms {
            break;
        }
        sum += t;
        taken.push(t);
    }
    if taken.is_empty() {
        return (0.0, 0.0, 0.0);
    }
    let longest = taken.iter().copied().fold(0.0, f32::max);
    taken.sort_by(f32::total_cmp);
    let median = taken[taken.len() / 2];
    (median, longest, taken.len() as f64 * 1000.0 / f64::from(sum.max(1.0)))
}

fn memory() -> String {
    let gb = |b: u64| b as f64 / (1u64 << 30) as f64;
    let (_, private) = super::panel::process_memory();
    match super::host().and_then(|h| h.video_memory()) {
        Some([budget, used, _, _]) => format!("VRAM {:.1}/{:.1} GB, RAM {:.1} GB", gb(used), gb(budget), gb(private)),
        None => format!("RAM {:.1} GB", gb(private)),
    }
}

/// A frame was presented: the keys, and the hitch and drop watch.
pub(super) fn frame() {
    let now = Instant::now();
    let mut state = STATE.lock().unwrap_or_else(|e| e.into_inner());
    let s = state.get_or_insert_with(|| State {
        started: now,
        last: None,
        times: VecDeque::with_capacity(KEPT),
        keys: [false; 9],
        banner: None,
        hitches: (0, 0.0),
        hitch_said: now,
        window: (0, now),
        before: 0.0,
        drop_said: now,
        marks: [0; 9],
    });
    let minutes = (now - s.started).as_secs_f64() / 60.0;
    let ms = s.last.map_or(0.0, |l| (now - l).as_secs_f64() as f32 * 1000.0);
    s.last = Some(now);
    if ms > 0.0 {
        if s.times.len() == KEPT {
            s.times.pop_front();
        }
        s.times.push_back(ms);
    }

    // The marks: Numpad 1-9, or Alt with 1-9, with our window in front.
    let alt = down(VK_MENU);
    for i in 0..9u16 {
        let pressed = down(VK_NUMPAD1 + i) || (alt && down(u16::from(b'1') + i));
        let was = std::mem::replace(&mut s.keys[i as usize], pressed);
        if !pressed || was || !ours_in_front() {
            continue;
        }
        let kind = KINDS[i as usize];
        s.marks[i as usize] += 1;
        let (median, longest, fps) = recent(&s.times, 2000.0);
        tracing::warn!(
            "MARK {} {kind} at {minutes:.1} min: {fps:.1} fps, frame {median:.0} ms, longest {longest:.0} ms in the last 2 s; {} (a player's mark, Numpad/Alt+{})",
            i + 1,
            memory(),
            i + 1
        );
        s.banner = Some((format!("MARKED {}: {kind}  (IN THE LOG)", i + 1), now));
        // (Off the presenting thread: Beep waits for the sound to end.)
        std::thread::spawn(|| {
            // SAFETY: a plain call.
            unsafe { windows_sys::Win32::System::Diagnostics::Debug::Beep(1200, 90) };
        });
    }

    if now - s.started < QUIET_START || ms <= 0.0 {
        s.window = (0, now);
        return;
    }
    // Hitches: a frame far longer than the ones around it.
    let (median, _, _) = recent(&s.times, 4000.0);
    if ms > (4.0 * median).max(100.0) {
        s.hitches.0 += 1;
        s.hitches.1 = s.hitches.1.max(ms);
    }
    if s.hitches.0 > 0 && now - s.hitch_said >= HITCH_EVERY {
        tracing::warn!(
            "HITCH at {minutes:.1} min: {} frames over {:.0} ms in the last {:.0} s, the longest {:.0} ms (frames around them {median:.0} ms); {}",
            s.hitches.0,
            (4.0 * median).max(100.0),
            (now - s.hitch_said).as_secs_f64(),
            s.hitches.1,
            memory()
        );
        s.hitches = (0, 0.0);
        s.hitch_said = now;
    }
    // Drops: the last 5 s against the half minute before.
    s.window.0 += 1;
    let span = now - s.window.1;
    if span >= Duration::from_secs(5) {
        let fps = f64::from(s.window.0) / span.as_secs_f64();
        if s.before > 5.0 && fps < 0.6 * s.before && now - s.drop_said >= DROP_EVERY {
            tracing::warn!(
                "FPS DROP at {minutes:.1} min: {fps:.1} fps in the last 5 s, {:.1} in the half minute before; {}",
                s.before,
                memory()
            );
            s.drop_said = now;
        }
        s.before = if s.before == 0.0 { fps } else { s.before + (fps - s.before) / 6.0 };
        s.window = (0, now);
    }
}

/// The panel's line about marks: the last one taken for a few seconds, else how to take one.
pub(super) fn banner() -> String {
    let state = STATE.lock().unwrap_or_else(|e| e.into_inner());
    match state.as_ref().and_then(|s| s.banner.as_ref()) {
        Some((text, at)) if at.elapsed() < BANNER_FOR => text.clone(),
        _ => "MARK A PROBLEM: NUMPAD 1-9 / ALT+1-9".to_owned(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn recent_takes_the_last_milliseconds_only() {
        let times: VecDeque<f32> = [100.0, 100.0, 20.0, 20.0, 20.0, 400.0].into_iter().collect();
        let (median, longest, fps) = recent(&times, 450.0);
        assert_eq!(longest, 400.0);
        assert_eq!(median, 20.0);
        assert!((fps - 4.0 * 1000.0 / 460.0).abs() < 1e-6);
        assert_eq!(recent(&VecDeque::new(), 1000.0), (0.0, 0.0, 0.0));
    }

    #[test]
    fn nine_kinds_fit_the_panel() {
        assert_eq!(KINDS.len(), 9);
        for k in KINDS {
            assert!(format!("MARKED 9: {k}  (IN THE LOG)").len() <= 40, "{k}");
        }
    }
}
