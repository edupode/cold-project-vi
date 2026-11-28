//! From VIctory-Hydra (github.com/ToLIManl/VIctory-Hydra, GPL-3.0, by ToLIManl), unchanged.
//!
//! Shared byte-verified, transactional code-patching core for title images.
//!
//! Every patch site records its exact image RVA and the full expected bytes
//! from the verified build. `apply_set` checks every site in a dependent
//! group *before* writing any of them so a partial build mismatch never
//! leaves a half-patched image (for example patching output size without
//! also patching the back-buffer pool check).

pub use crate::frame_rate::Patched;

/// One place in the title's image to compare and optionally patch: its image
/// offset (RVA), a human label, and the full bytes expected there before
/// patching. Any replacement must be at most `bytes.len()` long.
pub struct Site {
    pub at: usize,
    pub what: &'static str,
    pub bytes: &'static [u8],
}

/// One concrete replacement bound to a verified [`Site`].
pub struct Change {
    pub site: &'static Site,
    pub code: Vec<u8>,
}

/// Compares `site`'s bytes in the image of `size` bytes at `base`.
///
/// # Safety
/// The image of `size` bytes at `base` is mapped and readable.
pub unsafe fn matches(base: usize, size: usize, site: &Site) -> Patched {
    if site.at.checked_add(site.bytes.len()).is_none_or(|end| end > size) {
        return Patched::Failed("outside the image");
    }
    // SAFETY: inside the mapped image (checked above).
    let there = unsafe { std::slice::from_raw_parts((base + site.at) as *const u8, site.bytes.len()) };
    if there == site.bytes { Patched::Done } else { Patched::Differs }
}

/// Writes `code` over `site` if its bytes are the expected ones.
///
/// # Safety
/// The image stays mapped and no thread runs its code yet.
#[cfg(windows)]
pub unsafe fn write(base: usize, size: usize, site: &Site, code: &[u8]) -> Patched {
    if code.len() > site.bytes.len() {
        return Patched::Failed("replacement longer than the bytes compared");
    }
    // SAFETY: the caller's promise.
    let found = unsafe { matches(base, size, site) };
    if found != Patched::Done {
        return found;
    }
    // SAFETY: verified bounds and no executing title threads yet.
    unsafe { write_checked(base, site, code) }
}

/// Writes `code` over an already-verified `site`.
///
/// # Safety
/// `site` has already passed [`matches`] and `code.len() <= site.bytes.len()`,
/// and no thread is executing title code yet.
#[cfg(windows)]
unsafe fn write_checked(base: usize, site: &Site, code: &[u8]) -> Patched {
    use windows_sys::Win32::System::Diagnostics::Debug::FlushInstructionCache;
    use windows_sys::Win32::System::Memory::{PAGE_EXECUTE_READWRITE, VirtualProtect};
    use windows_sys::Win32::System::Threading::GetCurrentProcess;

    let at = base + site.at;
    let mut old = 0;
    // SAFETY: inside the mapped image (verified); made writable only for the
    // copy and restored immediately, followed by icache flush.
    unsafe {
        if VirtualProtect(at as *const _, code.len(), PAGE_EXECUTE_READWRITE, &mut old) == 0 {
            return Patched::Failed("Windows would not make it writable");
        }
        std::ptr::copy_nonoverlapping(code.as_ptr(), at as *mut u8, code.len());
        VirtualProtect(at as *const _, code.len(), old, &mut old);
        FlushInstructionCache(GetCurrentProcess(), at as *const _, code.len());
    }
    Patched::Done
}

/// Applies a group of dependent [`Change`]s transactionally: every site's
/// length and original bytes are verified before *any* site is written.
///
/// Returns `Patched::Done` only if every site matched and wrote cleanly;
/// otherwise returns the first failure (`Differs` or `Failed`) and leaves
/// earlier sites untouched if the check phase failed.
///
/// # Safety
/// The image of `size` bytes at `base` stays mapped and no thread runs its
/// code yet.
#[cfg(windows)]
pub unsafe fn apply_set(base: usize, size: usize, changes: &[Change]) -> Patched {
    for change in changes {
        if change.code.len() > change.site.bytes.len() {
            return Patched::Failed("replacement longer than the bytes compared");
        }
        // SAFETY: the caller's promise.
        let found = unsafe { matches(base, size, change.site) };
        if found != Patched::Done {
            return found;
        }
    }
    for change in changes {
        // SAFETY: every site in `changes` passed `matches` above.
        let done = unsafe { write_checked(base, change.site, &change.code) };
        if done != Patched::Done {
            return done;
        }
    }
    Patched::Done
}

#[cfg(all(test, windows))]
mod tests {
    use super::*;
    use windows_sys::Win32::System::Memory::{
        MEM_COMMIT, MEM_RELEASE, MEM_RESERVE, PAGE_EXECUTE_READ, VirtualAlloc, VirtualFree, VirtualProtect,
    };

    #[test]
    fn apply_set_is_all_or_nothing_on_mismatch() {
        static S1: Site = Site { at: 0x100, what: "first", bytes: &[0x90, 0x90, 0x90, 0x90] };
        static S2: Site = Site { at: 0x200, what: "second", bytes: &[0x48, 0x89, 0xc0, 0xc3] };
        // SAFETY: fresh private allocation freed at end of test.
        unsafe {
            let mem = VirtualAlloc(std::ptr::null(), 0x1000, MEM_COMMIT | MEM_RESERVE, 0x40) as *mut u8;
            assert!(!mem.is_null());
            std::ptr::copy_nonoverlapping(S1.bytes.as_ptr(), mem.add(S1.at), S1.bytes.len());
            // S2 intentionally has one differing byte.
            std::ptr::copy_nonoverlapping([0x48u8, 0x89, 0xc1, 0xc3].as_ptr(), mem.add(S2.at), 4);
            let mut old = 0;
            VirtualProtect(mem.cast(), 0x1000, PAGE_EXECUTE_READ, &mut old);

            let changes = [
                Change { site: &S1, code: vec![0xcc, 0xcc, 0xcc, 0xcc] },
                Change { site: &S2, code: vec![0x90, 0x90, 0x90, 0xc3] },
            ];
            assert_eq!(apply_set(mem as usize, 0x1000, &changes), Patched::Differs);
            // S1 must NOT have been written because S2 differed!
            assert_eq!(std::slice::from_raw_parts(mem.add(S1.at), 4), S1.bytes);

            // Now fix S2 and retry: both must write atomically.
            VirtualProtect(mem.cast(), 0x1000, 0x40, &mut old);
            std::ptr::copy_nonoverlapping(S2.bytes.as_ptr(), mem.add(S2.at), S2.bytes.len());
            VirtualProtect(mem.cast(), 0x1000, PAGE_EXECUTE_READ, &mut old);
            assert_eq!(apply_set(mem as usize, 0x1000, &changes), Patched::Done);
            assert_eq!(std::slice::from_raw_parts(mem.add(S1.at), 4), &[0xcc, 0xcc, 0xcc, 0xcc]);
            assert_eq!(std::slice::from_raw_parts(mem.add(S2.at), 4), &[0x90, 0x90, 0x90, 0xc3]);

            VirtualFree(mem.cast(), 0, MEM_RELEASE);
        }
    }
}
