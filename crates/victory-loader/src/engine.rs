//! From VIctory-Hydra (github.com/ToLIManl/VIctory-Hydra, GPL-3.0, by ToLIManl: commits a3f529bc and
//! ecbd78be, 2026-11-28), with its `patch` module; brought here unchanged apart from this note.
//!
//! Verified RAGE 9 engine-level graphics quality controls for
//! `game_scarlett_master.exe` (build `1.0.2.1`, Xbox Series X|S).
//!
//! Ported conceptually from `HSgod/ps5-fpsunlocker-gta6` (`low_shadows` and
//! `low_volumetric`), with every RVA and expected byte sequence independently
//! disassembled and verified against `game_scarlett_master.exe` (`1.0.2.1`):
//!
//! - **`VICTORY_LOW_SHADOWS`** (`0`/unset = stock, `1`/`1280`/`low` = 1280 px
//!   shadow atlas tier [PS5 `low_shadows=1` match], `2`/`1024`/`ultra` = 1024 px
//!   engine default fallback, `1536`/`med` = tier 0, `2048`/`high` = tier 2):
//!   Patches the central `GetShadowMapResolution(out, tier, half_flag, ...)`
//!   helper at `+0x1ce8bb0` (which indexes the `u32[4]` tier table at
//!   `+0x63deff8`: `[1536, 1280, 2048, 4096]`). All four engine callers
//!   (`+0xcc38e6` primary shadow atlas, `+0xcc39e2` secondary half-res atlas,
//!   `+0x5a40f85`, and `CSettingsManager::ApplySettings -> +0x5ad0aae` which
//!   computes `1024.0f / shadow_res` into `+0x84ddbc0`) call `+0x1ce8bb0`, so
//!   patching it once keeps both render-target sizes and shader texel scales
//!   synchronized.
//!
//! - **`VICTORY_LOW_VOLUMETRIC`** (`0`/unset = stock, `1` = 128 px / 7 mips
//!   [PS5 `low_volumetric=1` match]):
//!   Patches the volumetric / reflection map setter at `+0x1c9bea0` (called
//!   from `CSettingsManager::ApplySettings` at `+0x2243605 -> +0x599c810 ->
//!   +0x1c9bea0`). Clearing its `setg al` (`0f 9f c0` -> `90 90 90`) leaves
//!   `eax = 0`, fixing the map resolution (`+0x8551628`) to `128 << 0 = 128`
//!   and mip count (`+0x855162c`) to `7 + 0 = 7` (instead of `256` / `8`).
//!
//! Each setting is OFF by default, compared byte-for-byte before writing, and
//! publishes its outcome in `VICTORY_ENGINE_STATUS_<NAME>` (`Done` / `Differs`
//! / `Failed`) so the F1 menu can report honest status.

use crate::patch::{Change, Patched, Site};

/// Shadow map resolution override requested via `VICTORY_LOW_SHADOWS`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ShadowPreset {
    /// Tier 1 (`1280` px, `1280x5120` 4-cascade atlas): exact PS5 `low_shadows=1`.
    Low1280,
    /// Engine fallback (`1024` px, `1024x4096` 4-cascade atlas): smallest native size.
    UltraLow1024,
    /// Tier 0 (`1536` px).
    Med1536,
    /// Tier 2 (`2048` px).
    High2048,
}

impl ShadowPreset {
    pub fn parse(spec: &str) -> Option<Self> {
        match spec.trim().to_ascii_lowercase().as_str() {
            "" | "0" | "off" | "stock" => None,
            "1" | "low" | "1280" => Some(Self::Low1280),
            "2" | "ultra" | "ultralow" | "1024" => Some(Self::UltraLow1024),
            "med" | "medium" | "1536" => Some(Self::Med1536),
            "high" | "2048" => Some(Self::High2048),
            _ => None,
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Low1280 => "1280px (tier 1)",
            Self::UltraLow1024 => "1024px (ultra-low)",
            Self::Med1536 => "1536px (tier 0)",
            Self::High2048 => "2048px (tier 2)",
        }
    }
}

/// Reads `VICTORY_LOW_SHADOWS`. `None` if unset, `0`, or unrecognized (logged).
pub fn asked_low_shadows() -> Option<ShadowPreset> {
    let spec = std::env::var("VICTORY_LOW_SHADOWS").ok()?;
    let trimmed = spec.trim();
    if trimmed.is_empty() || trimmed == "0" || trimmed.eq_ignore_ascii_case("off") || trimmed.eq_ignore_ascii_case("stock") {
        return None;
    }
    let preset = ShadowPreset::parse(trimmed);
    if preset.is_none() {
        eprintln!("VIctory: VICTORY_LOW_SHADOWS={spec}: expected 0 (stock), 1/1280 (low), 2/1024 (ultra-low), 1536, or 2048");
    }
    preset
}

/// Reads `VICTORY_LOW_VOLUMETRIC` (`1` = force 128 px / 7 mips).
pub fn asked_low_volumetric() -> bool {
    std::env::var("VICTORY_LOW_VOLUMETRIC")
        .ok()
        .is_some_and(|v| matches!(v.trim().to_ascii_lowercase().as_str(), "1" | "on" | "low" | "128"))
}

/// Parses `VICTORY_LOD_SCALE` (`0.05..=1.0`, stock Series S = `0.50`, Series X = `0.75`).
pub fn parse_lod_scale(spec: &str) -> Option<f32> {
    let s = spec.trim();
    if s.is_empty() || s.eq_ignore_ascii_case("stock") || s.eq_ignore_ascii_case("off") || s == "0" {
        return None;
    }
    match s.to_ascii_lowercase().as_str() {
        "low" => Some(0.25),
        "med" | "medium" => Some(0.35),
        "high" => Some(0.50),
        _ => s.parse::<f32>().ok().filter(|v| v.is_finite() && (0.05..=1.0).contains(v)),
    }
}

/// Reads `VICTORY_LOD_SCALE` (`0.05..=1.0`, `low`/`med`/`high`, or `0`/`off`/`stock`).
pub fn asked_lod_scale() -> Option<f32> {
    let spec = std::env::var("VICTORY_LOD_SCALE").ok()?;
    let trimmed = spec.trim();
    if trimmed.is_empty() || trimmed == "0" || trimmed.eq_ignore_ascii_case("off") || trimmed.eq_ignore_ascii_case("stock") {
        return None;
    }
    let val = parse_lod_scale(trimmed);
    if val.is_none() {
        eprintln!("VIctory: VICTORY_LOD_SCALE={spec}: expected 0/stock, low (0.25), med (0.35), or float 0.05..=1.0");
    }
    val
}

/// Parses `VICTORY_SMALL_CULL` (`0.25..=16.0`, stock = `1.0`; higher culls tiny distant objects).
pub fn parse_small_cull(spec: &str) -> Option<f32> {
    let s = spec.trim();
    if s.is_empty() || s.eq_ignore_ascii_case("stock") || s.eq_ignore_ascii_case("off") || s == "0" {
        return None;
    }
    match s.to_ascii_lowercase().as_str() {
        "med" | "medium" => Some(2.0),
        "high" | "perf" => Some(3.0),
        "max" | "ultra" => Some(4.5),
        _ => s.parse::<f32>().ok().filter(|v| v.is_finite() && (0.25..=16.0).contains(v)),
    }
}

/// Reads `VICTORY_SMALL_CULL` (`0.25..=16.0`, `med`/`high`/`max`, or `0`/`off`/`stock`).
pub fn asked_small_cull() -> Option<f32> {
    let spec = std::env::var("VICTORY_SMALL_CULL").ok()?;
    let trimmed = spec.trim();
    if trimmed.is_empty() || trimmed == "0" || trimmed.eq_ignore_ascii_case("off") || trimmed.eq_ignore_ascii_case("stock") {
        return None;
    }
    let val = parse_small_cull(trimmed);
    if val.is_none() {
        eprintln!("VIctory: VICTORY_SMALL_CULL={spec}: expected 0/stock, med (2.0), high (3.0), max (4.5), or float 0.25..=16.0");
    }
    val
}

/// Parses `VICTORY_VEHICLE_LOD_BIAS` (`-2.0..=4.0`, stock = `0.0`).
pub fn parse_vehicle_lod_bias(spec: &str) -> Option<f32> {
    let s = spec.trim();
    if s.is_empty() || s.eq_ignore_ascii_case("stock") || s.eq_ignore_ascii_case("off") || s == "0" {
        return None;
    }
    s.parse::<f32>().ok().filter(|v| v.is_finite() && (-2.0..=4.0).contains(v))
}

/// Reads `VICTORY_VEHICLE_LOD_BIAS` (`-2.0..=4.0` or `0`/`off`/`stock`).
pub fn asked_vehicle_lod_bias() -> Option<f32> {
    let spec = std::env::var("VICTORY_VEHICLE_LOD_BIAS").ok()?;
    let trimmed = spec.trim();
    if trimmed.is_empty() || trimmed == "0" || trimmed.eq_ignore_ascii_case("off") || trimmed.eq_ignore_ascii_case("stock") {
        return None;
    }
    let val = parse_vehicle_lod_bias(trimmed);
    if val.is_none() {
        eprintln!("VIctory: VICTORY_VEHICLE_LOD_BIAS={spec}: expected 0/stock or float -2.0..=4.0");
    }
    val
}

/// Verified image RVA of the `u32[4]` shadow map resolution table
/// `[1536, 1280, 2048, 4096]` in `game_scarlett_master.exe` `1.0.2.1`.
pub const SHADOW_TABLE_RVA: usize = 0x63deff8;

/// Verified image RVAs written by [`VOLUMETRIC_RES`]:
/// dirty flag (`+0x8554a74`), tier flag (`+0x8551640`), resolution (`+0x8551628`),
/// and mip count (`+0x855162c`).
pub const VOLUMETRIC_DIRTY_RVA: usize = 0x8554a74;
pub const VOLUMETRIC_TIER_RVA: usize = 0x8551640;
pub const VOLUMETRIC_RES_RVA: usize = 0x8551628;
pub const VOLUMETRIC_MIPS_RVA: usize = 0x855162c;

/// Verified image RVAs written by `CSettingsManager::ApplySettings` (`+0x22434a0`)
/// in `game_scarlett_master.exe` `1.0.2.1`:
/// - `LOD_SCALE_GLOBAL_RVA` (`+0xa15e200`): raw `<lodScale>` float (followed by `call +0x5edebc0`
///   which computes `clamp(lodScale, 0, 1) * 950.0 + 50.0` into `+0x6b520b0`).
/// - `SMALL_CULL_GLOBAL_RVA` (`+0x684984c`): `<smallCullThreshold>` float.
/// - `VEHICLE_LOD_BIAS_GLOBAL_RVA` (`+0x8fe0850`): `<vehicleLodBias>` float (written by `+0x5eef340`).
pub const LOD_SCALE_GLOBAL_RVA: usize = 0xa15e200;
pub const SMALL_CULL_GLOBAL_RVA: usize = 0x684984c;
pub const VEHICLE_LOD_BIAS_GLOBAL_RVA: usize = 0x8fe0850;
pub const VEHICLE_LOD_BIAS_SETTER_RVA: usize = 0x5eef340;

/// `+0x1ce8bb0`..`+0x1ce8bd3` (`game_scarlett_master.exe` `1.0.2.1`):
/// `GetShadowMapResolution(out, tier, half_flag, ...)` prologue and tier-table lookup.
pub const SHADOW_RES: Site = Site {
    at: 0x1ce8bb0,
    what: "shadow map resolution tier lookup",
    bytes: &[
        0x56, 0x57, 0x53, 0x48, 0x83, 0xec, 0x20, // push rsi; push rdi; push rbx; sub rsp, 20h
        0xbe, 0x00, 0x04, 0x00, 0x00, // mov esi, 400h (1024 default)
        0x83, 0xfa, 0x03, // cmp edx, 3                   [+12]
        0x77, 0x0d, // ja +0xd (to +0x1ce8bce)      [+15]
        0x89, 0xd0, // mov eax, edx                 [+17]
        0x4c, 0x8d, 0x15, 0x2e, 0x64, 0x6f, 0x04, // lea r10, [+0x63deff8]        [+19]
        0x41, 0x8b, 0x34, 0x82, // mov esi, [r10+rax*4]         [+26]
        0x48, 0x8b, 0x7c, 0x24, 0x60, // mov rdi, [rsp+60h]           [+30]
    ],
};

/// `+0x1c9bea0`..`+0x1c9bece` (`game_scarlett_master.exe` `1.0.2.1`):
/// Volumetric / reflection map resolution & mip setter called from
/// `CSettingsManager::ApplySettings` (`+0x2243605 -> +0x599c810 -> +0x1c9bea0`).
pub const VOLUMETRIC_RES: Site = Site {
    at: 0x1c9bea0,
    what: "volumetric/reflection map resolution setter",
    bytes: &[
        0x31, 0xc0, // xor eax, eax
        0x85, 0xc9, // test ecx, ecx
        0xb9, 0x80, 0x00, 0x00, 0x00, // mov ecx, 80h (128)
        0xc6, 0x05, 0xc4, 0x8b, 0x8b, 0x06, 0x01, // mov byte [+0x8554a74], 1
        0x0f, 0x9f, 0xc0, // setg al                      [+16]
        0xc4, 0xe2, 0x79, 0xf7, 0xc9, // shlx ecx, ecx, eax
        0x89, 0x05, 0x82, 0x57, 0x8b, 0x06, // mov [+0x8551640], eax
        0x83, 0xc0, 0x07, // add eax, 7
        0x89, 0x0d, 0x61, 0x57, 0x8b, 0x06, // mov [+0x8551628], ecx
        0x89, 0x05, 0x5f, 0x57, 0x8b, 0x06, // mov [+0x855162c], eax
        0xc3, // ret
    ],
};

/// Replacement bytes for [`SHADOW_RES`] for `preset`.
///
/// Only bytes `12..19` (`cmp edx, 3; ja +0xd; mov eax, edx`) are changed:
/// - Table tiers replace them with `mov edx, <tier>; mov eax, edx`, keeping
///   `edx` valid for the downstream cascade/mip check (`cmp edx, 2`) at `+0x1ce8bf2`.
/// - `UltraLow1024` replaces them with `mov edx, 1; jmp +0xb` (landing on
///   `mov rdi, [rsp+60h]` at byte 30 = `+0x1ce8bce`), keeping `esi = 1024` and
///   `edx = 1`.
pub fn shadow_res_code(preset: ShadowPreset) -> Vec<u8> {
    let mut code = SHADOW_RES.bytes.to_vec();
    match preset {
        ShadowPreset::Med1536 => {
            code[12..19].copy_from_slice(&[0xba, 0x00, 0x00, 0x00, 0x00, 0x89, 0xd0]);
        }
        ShadowPreset::Low1280 => {
            code[12..19].copy_from_slice(&[0xba, 0x01, 0x00, 0x00, 0x00, 0x89, 0xd0]);
        }
        ShadowPreset::High2048 => {
            code[12..19].copy_from_slice(&[0xba, 0x02, 0x00, 0x00, 0x00, 0x89, 0xd0]);
        }
        ShadowPreset::UltraLow1024 => {
            // mov edx, 1 ; jmp +0x0b (19 + 11 = 30 -> +0x1ce8bce)
            code[12..19].copy_from_slice(&[0xba, 0x01, 0x00, 0x00, 0x00, 0xeb, 0x0b]);
        }
    }
    code
}

/// `+0x224360a`..`+0x224361a` (`game_scarlett_master.exe` `1.0.2.1`):
/// `CSettingsManager::ApplySettings` loading `<lodScale>` (`[rsi+0xd4]`),
/// writing it to `[+0xa15e200]`, and passing `xmm0` to `+0x5edebc0`
/// (`clamp(lodScale, 0, 1) * 950.0 + 50.0 -> [+0x6b520b0]`).
pub const LOD_SCALE: Site = Site {
    at: 0x224360a,
    what: "world LOD scale override",
    bytes: &[
        0xc5, 0xfa, 0x10, 0x86, 0xd4, 0x00, 0x00, 0x00, // vmovss xmm0, [rsi+0xd4]
        0xc5, 0xfa, 0x11, 0x05, 0xe6, 0xab, 0xf1, 0x07, // vmovss [+0xa15e200], xmm0
    ],
};

/// `+0x2243651`..`+0x2243661` (`game_scarlett_master.exe` `1.0.2.1`):
/// `CSettingsManager::ApplySettings` copying `<smallCullThreshold>` (`[rsi+0x10c]`)
/// into `[+0x684984c]`.
pub const SMALL_CULL: Site = Site {
    at: 0x2243651,
    what: "small-object cull threshold override",
    bytes: &[
        0xc5, 0xfa, 0x10, 0x86, 0x0c, 0x01, 0x00, 0x00, // vmovss xmm0, [rsi+0x10c]
        0xc5, 0xfa, 0x11, 0x05, 0xeb, 0x61, 0x60, 0x04, // vmovss [+0x684984c], xmm0
    ],
};

/// `+0x2243720`..`+0x224372d` (`game_scarlett_master.exe` `1.0.2.1`):
/// `CSettingsManager::ApplySettings` passing `<vehicleLodBias>` (`[rsi+0x184]`)
/// to `+0x5eef340` (`vmovss [+0x8fe0850], xmm0; ret`).
pub const VEHICLE_LOD_BIAS: Site = Site {
    at: 0x2243720,
    what: "vehicle LOD bias override",
    bytes: &[
        0xc5, 0xfa, 0x10, 0x86, 0x84, 0x01, 0x00, 0x00, // vmovss xmm0, [rsi+0x184]
        0xe8, 0x13, 0xbc, 0xca, 0x03, // call +0x5eef340
    ],
};

/// Replacement bytes for [`VOLUMETRIC_RES`]: NOPs out `setg al` (`+16..+19`)
/// so `eax` remains `0`, fixing resolution to `128` and mip count to `7`.
pub fn low_volumetric_code() -> Vec<u8> {
    let mut code = VOLUMETRIC_RES.bytes.to_vec();
    code[16..19].copy_from_slice(&[0x90, 0x90, 0x90]);
    code
}

/// Replacement bytes for [`LOD_SCALE`] (`16` bytes, self-contained):
/// `mov eax, imm32; mov [+0xa15e200], eax; vmovd xmm0, eax; nop`.
/// Updates both `[+0xa15e200]` and `xmm0` for the following `call +0x5edebc0`.
pub fn lod_scale_code(scale: f32) -> Vec<u8> {
    let disp = (LOD_SCALE_GLOBAL_RVA as i64 - (LOD_SCALE.at + 5 + 6) as i64) as i32;
    let mut code = Vec::with_capacity(16);
    code.push(0xb8); // mov eax, imm32
    code.extend_from_slice(&scale.to_bits().to_le_bytes());
    code.extend_from_slice(&[0x89, 0x05]); // mov dword ptr [rip+disp], eax
    code.extend_from_slice(&disp.to_le_bytes());
    code.extend_from_slice(&[0xc5, 0xf9, 0x6e, 0xc0]); // vmovd xmm0, eax
    code.push(0x90); // nop
    code
}

/// Replacement bytes for [`SMALL_CULL`] (`16` bytes, self-contained):
/// `mov dword ptr [+0x684984c], imm32; 6x nop`.
pub fn small_cull_code(threshold: f32) -> Vec<u8> {
    let disp = (SMALL_CULL_GLOBAL_RVA as i64 - (SMALL_CULL.at + 10) as i64) as i32;
    let mut code = Vec::with_capacity(16);
    code.extend_from_slice(&[0xc7, 0x05]); // mov dword ptr [rip+disp], imm32
    code.extend_from_slice(&disp.to_le_bytes());
    code.extend_from_slice(&threshold.to_bits().to_le_bytes());
    code.extend_from_slice(&[0x90; 6]);
    code
}

/// Replacement bytes for [`VEHICLE_LOD_BIAS`] (`13` bytes, self-contained):
/// `mov dword ptr [+0x8fe0850], imm32; 3x nop`.
pub fn vehicle_lod_bias_code(bias: f32) -> Vec<u8> {
    let disp = (VEHICLE_LOD_BIAS_GLOBAL_RVA as i64 - (VEHICLE_LOD_BIAS.at + 10) as i64) as i32;
    let mut code = Vec::with_capacity(13);
    code.extend_from_slice(&[0xc7, 0x05]); // mov dword ptr [rip+disp], imm32
    code.extend_from_slice(&disp.to_le_bytes());
    code.extend_from_slice(&bias.to_bits().to_le_bytes());
    code.extend_from_slice(&[0x90; 3]);
    code
}

#[cfg(windows)]
fn record_status(env_name: &str, result: Patched) {
    let status = match result {
        Patched::Done => "Done",
        Patched::Differs => "Differs",
        Patched::Failed(_) => "Failed",
    };
    // SAFETY: called single-threaded at startup before title execution.
    unsafe {
        std::env::set_var(env_name, status);
    }
}

/// Applies all asked RAGE 9 engine-level settings patches transactionally,
/// logging each result and publishing `VICTORY_ENGINE_STATUS_*` for the F1 UI.
///
/// # Safety
/// The image of `size` bytes at `base` stays mapped and no thread runs its
/// code yet.
#[cfg(windows)]
pub unsafe fn apply_asked(base: usize, size: usize) {
    if let Some(preset) = asked_low_shadows() {
        let changes = [Change {
            site: &SHADOW_RES,
            code: shadow_res_code(preset),
        }];
        // SAFETY: the caller's promise.
        let done = unsafe { crate::patch::apply_set(base, size, &changes) };
        record_status("VICTORY_ENGINE_STATUS_LOW_SHADOWS", done);
        let offset = format!("{:#x}", SHADOW_RES.at);
        match done {
            Patched::Done => tracing::info!(site = %offset, preset = preset.label(), "engine: low shadows patched"),
            Patched::Differs => tracing::warn!(site = %offset, "engine: low shadows not patched (bytes differ; another build?)"),
            Patched::Failed(why) => tracing::warn!(site = %offset, why, "engine: low shadows not patched"),
        }
        println!("engine         {} ({}) at title+{offset}: {done:?}", SHADOW_RES.what, preset.label());
    }

    if asked_low_volumetric() {
        let changes = [Change {
            site: &VOLUMETRIC_RES,
            code: low_volumetric_code(),
        }];
        // SAFETY: the caller's promise.
        let done = unsafe { crate::patch::apply_set(base, size, &changes) };
        record_status("VICTORY_ENGINE_STATUS_LOW_VOLUMETRIC", done);
        let offset = format!("{:#x}", VOLUMETRIC_RES.at);
        match done {
            Patched::Done => tracing::info!(site = %offset, "engine: low volumetric patched (128px / 7 mips)"),
            Patched::Differs => tracing::warn!(site = %offset, "engine: low volumetric not patched (bytes differ; another build?)"),
            Patched::Failed(why) => tracing::warn!(site = %offset, why, "engine: low volumetric not patched"),
        }
        println!("engine         {} (128px / 7 mips) at title+{offset}: {done:?}", VOLUMETRIC_RES.what);
    }

    if let Some(scale) = asked_lod_scale() {
        let changes = [Change {
            site: &LOD_SCALE,
            code: lod_scale_code(scale),
        }];
        // SAFETY: the caller's promise.
        let done = unsafe { crate::patch::apply_set(base, size, &changes) };
        record_status("VICTORY_ENGINE_STATUS_LOD_SCALE", done);
        let offset = format!("{:#x}", LOD_SCALE.at);
        let dist_m = scale.clamp(0.0, 1.0) * 950.0 + 50.0;
        match done {
            Patched::Done => tracing::info!(site = %offset, scale, dist_m, "engine: world LOD scale patched"),
            Patched::Differs => tracing::warn!(site = %offset, "engine: LOD scale not patched (bytes differ; another build?)"),
            Patched::Failed(why) => tracing::warn!(site = %offset, why, "engine: LOD scale not patched"),
        }
        println!("engine         {} ({scale:.2} -> {dist_m:.0}m) at title+{offset}: {done:?}", LOD_SCALE.what);
    }

    if let Some(threshold) = asked_small_cull() {
        let changes = [Change {
            site: &SMALL_CULL,
            code: small_cull_code(threshold),
        }];
        // SAFETY: the caller's promise.
        let done = unsafe { crate::patch::apply_set(base, size, &changes) };
        record_status("VICTORY_ENGINE_STATUS_SMALL_CULL", done);
        let offset = format!("{:#x}", SMALL_CULL.at);
        match done {
            Patched::Done => tracing::info!(site = %offset, threshold, "engine: small-object cull threshold patched"),
            Patched::Differs => tracing::warn!(site = %offset, "engine: small-object cull not patched (bytes differ; another build?)"),
            Patched::Failed(why) => tracing::warn!(site = %offset, why, "engine: small-object cull not patched"),
        }
        println!("engine         {} ({threshold:.2}) at title+{offset}: {done:?}", SMALL_CULL.what);
    }

    if let Some(bias) = asked_vehicle_lod_bias() {
        let changes = [Change {
            site: &VEHICLE_LOD_BIAS,
            code: vehicle_lod_bias_code(bias),
        }];
        // SAFETY: the caller's promise.
        let done = unsafe { crate::patch::apply_set(base, size, &changes) };
        record_status("VICTORY_ENGINE_STATUS_VEHICLE_LOD_BIAS", done);
        let offset = format!("{:#x}", VEHICLE_LOD_BIAS.at);
        match done {
            Patched::Done => tracing::info!(site = %offset, bias, "engine: vehicle LOD bias patched"),
            Patched::Differs => tracing::warn!(site = %offset, "engine: vehicle LOD bias not patched (bytes differ; another build?)"),
            Patched::Failed(why) => tracing::warn!(site = %offset, why, "engine: vehicle LOD bias not patched"),
        }
        println!("engine         {} ({bias:.2}) at title+{offset}: {done:?}", VEHICLE_LOD_BIAS.what);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn rip_target(site: &Site, disp_end: usize) -> usize {
        let disp = i32::from_le_bytes(site.bytes[disp_end - 4..disp_end].try_into().unwrap());
        ((site.at + disp_end) as i64 + i64::from(disp)) as usize
    }

    #[test]
    fn parse_shadow_presets() {
        assert_eq!(ShadowPreset::parse("0"), None);
        assert_eq!(ShadowPreset::parse("off"), None);
        assert_eq!(ShadowPreset::parse("1"), Some(ShadowPreset::Low1280));
        assert_eq!(ShadowPreset::parse("1280"), Some(ShadowPreset::Low1280));
        assert_eq!(ShadowPreset::parse("2"), Some(ShadowPreset::UltraLow1024));
        assert_eq!(ShadowPreset::parse("1024"), Some(ShadowPreset::UltraLow1024));
        assert_eq!(ShadowPreset::parse("1536"), Some(ShadowPreset::Med1536));
        assert_eq!(ShadowPreset::parse("2048"), Some(ShadowPreset::High2048));
        assert_eq!(ShadowPreset::parse("bogus"), None);
    }

    #[test]
    fn shadow_site_geometry_and_replacements() {
        assert_eq!(SHADOW_RES.bytes.len(), 35);
        // `ja +0x0d` at 15..17 lands at byte 30 (`mov rdi, [rsp+60h]`).
        assert_eq!(SHADOW_RES.bytes[15], 0x77);
        assert_eq!(17 + usize::from(SHADOW_RES.bytes[16]), 30);
        // `lea r10, [rip+disp]` at 19..26 points to SHADOW_TABLE_RVA.
        assert_eq!(&SHADOW_RES.bytes[19..22], &[0x4c, 0x8d, 0x15]);
        assert_eq!(rip_target(&SHADOW_RES, 26), SHADOW_TABLE_RVA);
        assert_eq!(&SHADOW_RES.bytes[30..35], &[0x48, 0x8b, 0x7c, 0x24, 0x60]);

        let low = shadow_res_code(ShadowPreset::Low1280);
        assert_eq!(low.len(), SHADOW_RES.bytes.len());
        assert_eq!(&low[..12], &SHADOW_RES.bytes[..12]);
        assert_eq!(&low[12..19], &[0xba, 0x01, 0x00, 0x00, 0x00, 0x89, 0xd0]);
        assert_eq!(&low[19..], &SHADOW_RES.bytes[19..]);

        let ultra = shadow_res_code(ShadowPreset::UltraLow1024);
        assert_eq!(ultra.len(), SHADOW_RES.bytes.len());
        assert_eq!(ultra[17], 0xeb);
        assert_eq!(19 + usize::from(ultra[18]), 30, "short jmp lands on mov rdi, [rsp+60h]");
        assert_eq!(&ultra[19..], &SHADOW_RES.bytes[19..]);
    }

    #[test]
    fn volumetric_site_geometry_and_replacement() {
        assert_eq!(VOLUMETRIC_RES.bytes.len(), 46);
        // `mov byte [rip+disp], 1` ends at byte 16, disp is 11..15, next rip is 16.
        let dirty_disp = i32::from_le_bytes(VOLUMETRIC_RES.bytes[11..15].try_into().unwrap());
        assert_eq!((VOLUMETRIC_RES.at + 16) as i64 + i64::from(dirty_disp), VOLUMETRIC_DIRTY_RVA as i64);
        assert_eq!(&VOLUMETRIC_RES.bytes[16..19], &[0x0f, 0x9f, 0xc0]);
        assert_eq!(rip_target(&VOLUMETRIC_RES, 30), VOLUMETRIC_TIER_RVA);
        assert_eq!(rip_target(&VOLUMETRIC_RES, 39), VOLUMETRIC_RES_RVA);
        assert_eq!(rip_target(&VOLUMETRIC_RES, 45), VOLUMETRIC_MIPS_RVA);
        assert_eq!(VOLUMETRIC_RES.bytes[45], 0xc3);

        let patched = low_volumetric_code();
        assert_eq!(patched.len(), VOLUMETRIC_RES.bytes.len());
        assert_eq!(&patched[..16], &VOLUMETRIC_RES.bytes[..16]);
        assert_eq!(&patched[16..19], &[0x90, 0x90, 0x90]);
        assert_eq!(&patched[19..], &VOLUMETRIC_RES.bytes[19..]);
    }

    #[test]
    fn stage14_lod_small_cull_and_vehicle_lod_sites() {
        assert_eq!(parse_lod_scale("0"), None);
        assert_eq!(parse_lod_scale("low"), Some(0.25));
        assert_eq!(parse_lod_scale("0.35"), Some(0.35));
        assert_eq!(parse_small_cull("stock"), None);
        assert_eq!(parse_small_cull("high"), Some(3.0));
        assert_eq!(parse_small_cull("2.5"), Some(2.5));
        assert_eq!(parse_vehicle_lod_bias("0"), None);
        assert_eq!(parse_vehicle_lod_bias("0.5"), Some(0.5));

        assert_eq!(LOD_SCALE.bytes.len(), 16);
        assert_eq!(rip_target(&LOD_SCALE, 16), LOD_SCALE_GLOBAL_RVA);
        let lod = lod_scale_code(0.35);
        assert_eq!(lod.len(), 16);
        assert_eq!(lod[0], 0xb8);
        assert_eq!(f32::from_bits(u32::from_le_bytes(lod[1..5].try_into().unwrap())), 0.35);
        let lod_disp = i32::from_le_bytes(lod[7..11].try_into().unwrap());
        assert_eq!((LOD_SCALE.at + 11) as i64 + i64::from(lod_disp), LOD_SCALE_GLOBAL_RVA as i64);

        assert_eq!(SMALL_CULL.bytes.len(), 16);
        assert_eq!(rip_target(&SMALL_CULL, 16), SMALL_CULL_GLOBAL_RVA);
        let sc = small_cull_code(2.5);
        assert_eq!(sc.len(), 16);
        let sc_disp = i32::from_le_bytes(sc[2..6].try_into().unwrap());
        assert_eq!((SMALL_CULL.at + 10) as i64 + i64::from(sc_disp), SMALL_CULL_GLOBAL_RVA as i64);
        assert_eq!(f32::from_bits(u32::from_le_bytes(sc[6..10].try_into().unwrap())), 2.5);

        assert_eq!(VEHICLE_LOD_BIAS.bytes.len(), 13);
        assert_eq!(rip_target(&VEHICLE_LOD_BIAS, 13), VEHICLE_LOD_BIAS_SETTER_RVA);
        let vb = vehicle_lod_bias_code(0.5);
        assert_eq!(vb.len(), 13);
        let vb_disp = i32::from_le_bytes(vb[2..6].try_into().unwrap());
        assert_eq!((VEHICLE_LOD_BIAS.at + 10) as i64 + i64::from(vb_disp), VEHICLE_LOD_BIAS_GLOBAL_RVA as i64);
        assert_eq!(f32::from_bits(u32::from_le_bytes(vb[6..10].try_into().unwrap())), 0.5);
    }

    /// Optional golden check against a real title PE when `VICTORY_TITLE_EXE`
    /// is set in the environment: confirms both sites match byte-for-byte.
    #[test]
    fn golden_title_exe_bytes_if_present() {
        let Ok(path) = std::env::var("VICTORY_TITLE_EXE") else {
            return;
        };
        let pe = std::fs::read(&path).expect("read VICTORY_TITLE_EXE");
        let pe_off = u32::from_le_bytes(pe[0x3c..0x40].try_into().unwrap()) as usize;
        let num_sec = u16::from_le_bytes(pe[pe_off + 6..pe_off + 8].try_into().unwrap()) as usize;
        let opt_size = u16::from_le_bytes(pe[pe_off + 20..pe_off + 22].try_into().unwrap()) as usize;
        let sec_base = pe_off + 24 + opt_size;
        let rva_to_off = |rva: usize| -> usize {
            for i in 0..num_sec {
                let s = sec_base + i * 40;
                let vs = u32::from_le_bytes(pe[s + 8..s + 12].try_into().unwrap()) as usize;
                let va = u32::from_le_bytes(pe[s + 12..s + 16].try_into().unwrap()) as usize;
                let raw = u32::from_le_bytes(pe[s + 20..s + 24].try_into().unwrap()) as usize;
                if rva >= va && rva < va + vs {
                    return raw + (rva - va);
                }
            }
            panic!("RVA {rva:#x} not found in PE sections");
        };

        for site in [&SHADOW_RES, &VOLUMETRIC_RES, &LOD_SCALE, &SMALL_CULL, &VEHICLE_LOD_BIAS] {
            let off = rva_to_off(site.at);
            assert_eq!(
                &pe[off..off + site.bytes.len()],
                site.bytes,
                "site {} at {:#x} mismatched real PE",
                site.what,
                site.at
            );
        }
        let tbl_off = rva_to_off(SHADOW_TABLE_RVA);
        let tiers: [u32; 4] = [
            u32::from_le_bytes(pe[tbl_off..tbl_off + 4].try_into().unwrap()),
            u32::from_le_bytes(pe[tbl_off + 4..tbl_off + 8].try_into().unwrap()),
            u32::from_le_bytes(pe[tbl_off + 8..tbl_off + 12].try_into().unwrap()),
            u32::from_le_bytes(pe[tbl_off + 12..tbl_off + 16].try_into().unwrap()),
        ];
        assert_eq!(tiers, [1536, 1280, 2048, 4096]);
    }
}
