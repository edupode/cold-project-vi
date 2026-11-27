//! A title's wave64 compute program run two ways with the same inputs: as one
//! console wave of 64 lanes (`WaveSize(64)`, on WARP, which runs waves of up
//! to 128) and as two host waves of 32 (the form NVIDIA cards get), to find
//! where the second departs from the first.
//!
//! `VICTORY_DIFF_CONTAINER=<file.cdxil>` (a container as the title has it,
//! from `victory translate` with `VICTORY_DUMP_MATCH`); skipped without it.
//! Game-derived files stay outside the repository.

use super::{HostShader, translate};

fn both(container: &[u8]) -> (Result<HostShader, String>, Result<HostShader, String>) {
    let at = |lanes| {
        let was = super::lower::TEST_HOST_WAVE.replace(Some(lanes));
        let r = translate(container).map_err(|e| e.to_string());
        super::lower::TEST_HOST_WAVE.set(was);
        r
    };
    (at(64), at(32))
}

#[test]
fn a_title_program_in_two_host_waves_matches_one_console_wave() {
    let Some(path) = std::env::var_os("VICTORY_DIFF_CONTAINER") else {
        eprintln!("no VICTORY_DIFF_CONTAINER; skipped");
        return;
    };
    let bytes = std::fs::read(&path).expect("container");
    {
        let c = super::Container::parse(&bytes).expect("container");
        let part = c.part(super::FourCC::DXIL).or(c.part(super::FourCC::ACCC)).expect("part");
        let native = crate::native_programs(part.data).expect("programs").into_iter().next().expect("program");
        eprintln!("io: {:?}, wave64 {}, vgprs {}", native.io, native.wave64, native.vgprs);
        let p = crate::spill::build(&native).expect("ir");
        let st = crate::structure::structure(&p).ok();
        let res = crate::resource::track(&p, &native.layout, native.stage, st.as_ref()).expect("resources");
        for d in &res.descriptors {
            eprintln!("  descriptor {d:?}");
        }
    }
    let (wide, split) = both(&bytes);
    for (name, s) in [("wave64", &wide), ("2x32", &split)] {
        match s {
            Ok(s) => {
                eprintln!("{name}: {} bytes of DXIL", s.dxil.len());
                eprintln!("  user data: {:?}", s.user_data);
                eprintln!("  samplers: {}, root cbvs {:?}, srvs {:?}, uavs {:?}", s.samplers.len(), s.root_cbvs, s.root_srvs, s.root_uavs);
                eprintln!("  indexed: {:?}", s.root_indexed_views);
            }
            Err(e) => eprintln!("{name}: {e}"),
        }
    }
}

/// The tile-bounds part of `CS_TransmissionVolumetrics_Tiled`, its own words (memory loads
/// replaced by thread-index values): thread (0,0) sets the tile's bounds in LDS, every thread
/// on screen (`t < s0`) puts its key in with `ds_min`/`ds_max`, all read them back; then a
/// sum over every lane the group started with, the console's way (DPP rows, `v_permlanex16`,
/// `v_readlane` 32 and 0). Each thread stores max, min and the sum. Run as one wave of 64 on
/// WARP and as two host waves of 32 on the hardware device.
fn tile_bounds_program() -> crate::native::NativeProgram {
    use crate::native::{ComputeIo, HwStage, NativeProgram, ResourceType, RootParam, RootType, StageIo, UsedResource};
    let body: Vec<u32> = vec![
        0x3826_0101, // v_or_b32 v19, v1, v0
        0xBE8A_047E, // s_mov_b64 s[10:11], exec
        0x7DA4_2680, // v_cmpx_eq_u32 exec, 0, v19
        0x7E26_0280, // v_mov_b32 v19, 0
        0x7E28_02C1, // v_mov_b32 v20, -1
        0xD838_0100, 0x0014_1313, // ds_write2_b32 v19, v19, v20 offset1:1
        0xBEFE_040A, // s_mov_b64 exec, s[10:11]
        0xD76F_000F, 0x0401_1101, // v_lshl_or_b32 v15, v1, 8, v0
        0x161A_3CA5, // v_mul_u32_u24 v13, 37, v30
        0x361A_1AFF, 0x0000_00FF, // v_and_b32 v13, 0xff, v13
        0x341A_1A90, // v_lshlrev_b32 v13, 16, v13
        0x381A_1B0F, // v_or_b32 v13, v15, v13
        0x7E20_0280, // v_mov_b32 v16, 0
        0xD81C_0004, 0x0000_0D10, // ds_min_u32 v16, v13 offset:4
        0xD820_0000, 0x0000_0D10, // ds_max_u32 v16, v13
        0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
        0x7E2C_0280, // v_mov_b32 v22, 0
        0xD8DC_0100, 0x1300_0016, // ds_read2_b32 v[19:20], v22 offset1:1
        0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
        0x7E2C_0D1E, // v_cvt_f32_u32 v22, v30
        0xBE86_25C1, // s_or_saveexec_b64 s[6:7], -1
        0x8788_064A, // s_and_b64 s[8:9], s[74:75], s[6:7]
        0xD501_0002, 0x0022_2C80, // v_cndmask_b32 v2, 0, v22, s[8:9]
        0x0604_04E9, 0xDE54_C102, // v_add_f32 v2, v2, v2 dpp8:[1,0,3,2,5,4,7,6]
        0x0604_04E9, 0x9360_1202, // dpp8:[2,2,0,0,6,6,4,4]
        0x0604_04E9, 0x0009_2402, // dpp8:[4,4,4,4,0,0,0,0]
        0x0604_04FA, 0xFF09_4002, // v_add_f32 v2, v2, v2 row_mirror
        0xD778_0003, 0x0201_0102, // v_permlanex16_b32 v3, v2, 0, 0
        0x0604_0503, // v_add_f32 v2, v3, v2
        0xD760_0008, 0x0001_4102, // v_readlane_b32 s8, v2, 32
        0x0604_0408, // v_add_f32 v2, s8, v2
        0xD760_000E, 0x0001_0102, // v_readlane_b32 s14, v2, 0
        0xBEFE_0406, // s_mov_b64 exec, s[6:7]
        0x3450_3C84, // v_lshlrev_b32 v40, 4, v30
        0xE070_1000, 0x8005_1328, // buffer_store_dword v19, v40, s[20:23] offen
        0xE070_1004, 0x8005_1428, // buffer_store_dword v20, v40, s[20:23] offen offset:4
        0x7E2A_020E, // v_mov_b32 v21, s14
        0xE070_1008, 0x8005_1528, // buffer_store_dword v21, v40, s[20:23] offen offset:8
    ];
    let mut words = vec![
        0xBE85_0380, // s_mov_b32 s5, 0
        0x3D << 26 | 0x02 << 18 | 8 << 6 | 2,
        125 << 25,   // s_load_dwordx4 s[8:11], s[4:5], 0
        0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
        0xBE94_0408, // s_mov_b64 s[20:21], s[8:9]
        0xBE96_040A, // s_mov_b64 s[22:23], s[10:11]
        0x7E3C_0300, // v_mov_b32 v30, v0
        0x3600_3C87, // v_and_b32 v0, 7, v30
        0x2C02_3C83, // v_lshrrev_b32 v1, 3, v30
        0xBECA_047E, // s_mov_b64 s[74:75], exec
        0x7D88_3C00, // v_cmp_gt_u32 vcc, s0, v30
        0xBE80_246A, // s_and_saveexec_b64 s[0:1], vcc
        0xBF88_0000 | body.len() as u32, // s_cbranch_execz past the body
    ];
    words.extend_from_slice(&body);
    words.push(0xBEFE_0400); // s_mov_b64 exec, s[0:1]
    words.push(0xBF81_0000); // s_endpgm
    NativeProgram {
        stage: HwStage::Cs,
        wave64: true,
        vgprs: 48,
        layout: vec![
            RootParam { root: RootType::Constants, table_offset_dwords: 0, sgprs: 4, base_register: 0, user_data: 0, slot: 0 },
            RootParam { root: RootType::Table, table_offset_dwords: 0, sgprs: 1, base_register: 0, user_data: 4, slot: 1 },
        ],
        used: vec![UsedResource {
            kind: ResourceType::Uav,
            root: RootType::Table,
            table_offset_dwords: 0,
            lower_bound: 0,
            stride: 0,
            dwords: 4,
            user_data: 4,
            slot: 1,
        }],
        // 5 user SGPRs, 128 dwords of LDS
        io: StageIo::Cs(ComputeIo { rsrc2: 5 << 1 | 1 << 15, threads: [64, 1, 1] }),
        code: words.iter().flat_map(|w| w.to_le_bytes()).collect(),
    }
}

/// The program as the runtime translates it for a host of `lanes`-lane waves, the wave-wide
/// form allowed (`VICTORY_WAVE_WIDE=1`, as the digest list gives `_Tiled`).
fn runtime_form(native: &crate::native::NativeProgram, lanes: u32) -> Result<HostShader, String> {
    let p = crate::spill::build(native).map_err(|e| e.to_string())?;
    let st = crate::structure::structure(&p).map_err(|e| e.to_string())?;
    let res = crate::resource::track(&p, &native.layout, native.stage, Some(&st)).map_err(|e| e.to_string())?;
    let io = crate::io::raise(&p, native).map_err(|e| e.to_string())?;
    let c = crate::dxbc::Container { digest: [0; 16], version: (1, 0), parts: Vec::new() };
    let was = super::lower::TEST_HOST_WAVE.replace(Some(lanes));
    // SAFETY: test-only; the other tests that read it do not run in this binary's threads at once
    // in a way that matters (they set it themselves or leave it unset).
    unsafe { std::env::set_var("VICTORY_WAVE_WIDE", "1") };
    let r = super::emit(&c, native, &p, &st, &io, &res).map_err(|e| e.to_string());
    unsafe { std::env::remove_var("VICTORY_WAVE_WIDE") };
    super::lower::TEST_HOST_WAVE.set(was);
    r
}

#[test]
fn tiled_bounds_two_host_waves_match_one_console_wave() {
    use super::host::{Dispatch, Host, Root, View};
    let native = tile_bounds_program();
    let run = |host: &Host, s: &HostShader, active: u32| -> Result<Vec<u32>, String> {
        let (root, pso) = host.compute_pipeline(s)?;
        let mut ud = vec![0u32; s.user_data_dwords().max(1) as usize];
        for u in &s.user_data {
            if u.root == crate::native::RootType::Constants && u.dword == 0 {
                ud[u.index as usize] = active;
            }
        }
        let out = host.dispatch(&root, &pso, &Dispatch {
            roots: vec![(0, Root::Constants(ud))],
            views: &[View::RawUav(256)],
            samplers: &[],
            groups: [1, 1, 1],
        })?;
        Ok(out[0].clone())
    };
    let wide = runtime_form(&native, 64).expect("wave64 form");
    let split = runtime_form(&native, 32).expect("two-wave form");
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(warp) = Host::new(true) else {
        eprintln!("no WARP; skipped");
        return;
    };
    // Not every WARP runs a fixed wave of 64 (the CI runner's refuses the
    // pipeline: "The parameter is incorrect."), as in `diff_tests`.
    if let Err(e) = warp.compute_pipeline(&wide) {
        eprintln!("WARP runs no wave of 64 here ({e}); skipped");
        return;
    }
    let hw = Host::new(false).ok();
    for active in [64u32, 40, 33, 32, 20, 1] {
        // what the console computes
        let key = |t: u32| ((37 * t) & 0xFF) << 16 | (t >> 3) << 8 | (t & 7);
        let (mx, mn) = (0..active).map(key).fold((0u32, u32::MAX), |(a, b), k| (a.max(k), b.min(k)));
        let sum = 2016f32.to_bits();
        let reference = run(&warp, &wide, active).expect("WARP wave64");
        let mut bad = 0;
        for t in 0..active as usize {
            let got = &reference[t * 4..t * 4 + 3];
            if got != [mx, mn, sum] {
                bad += 1;
                if bad < 4 {
                    eprintln!("WARP wave64 active={active} t={t}: {got:x?}, want {:x?}", [mx, mn, sum]);
                }
            }
        }
        eprintln!("active={active}: WARP wave64 differs from the console in {bad} threads");
        if let Some(hw) = &hw {
            match run(hw, &split, active) {
                Ok(out) => {
                    let mut diff = 0;
                    for t in 0..64usize {
                        if out[t * 4..t * 4 + 4] != reference[t * 4..t * 4 + 4] {
                            diff += 1;
                            if diff < 6 {
                                eprintln!("  2x32 active={active} t={t}: {:x?} vs wave64 {:x?}", &out[t * 4..t * 4 + 3], &reference[t * 4..t * 4 + 3]);
                            }
                        }
                    }
                    eprintln!("active={active}: hardware 2x32 differs from wave64 in {diff} threads");
                }
                Err(e) => eprintln!("active={active}: hardware 2x32 did not run: {e}"),
            }
        }
    }
}

/// The slice loop of `CS_TransmissionVolumetrics_Tiled` (its 0x1e60..0x1ee0), small: every
/// thread leaves the loop at its own step (`v_cmpx` then `s_andn2_b64` of the lanes left and
/// `s_cbranch_scc0` once none is), and inside it a value the same in every running lane is
/// read with `v_readfirstlane_b32`. Thread t leaves at step t >> 3, so the first half of the
/// console wave (t < 32) has left by step 4 while the second half still runs: there the first
/// running lane is in the second half, and the first half's own lanes hold an older value.
/// Each thread stores the sum of the steps it ran, 0 + 1 + ... + (n - 1).
#[test]
fn a_loop_each_lane_leaves_at_its_own_step_in_two_host_waves() {
    use super::host::{Dispatch, Host, Root, View};
    let body = [
        0x2C02_0083, // v_lshrrev_b32 v1, 3, v0          n = t >> 3
        0x7E04_0280, // v_mov_b32 v2, 0                   the sum
        0xBE94_0380, // s_mov_b32 s20, 0                  the step
        0xBE9A_047E, // s_mov_b64 s[26:27], exec
        0xBE96_047E, // s_mov_b64 s[22:23], exec          lanes still in the loop
        // L:
        0xBE82_0314, // s_mov_b32 s2, s20
        0x8114_8114, // s_add_i32 s20, s20, 1
        0x7E06_0A02, // v_cvt_f32_i32 v3, s2
        0x7E30_0503, // v_readfirstlane_b32 s24, v3
        0xBE9C_047E, // s_mov_b64 s[28:29], exec
        0x7DAC_0202, // v_cmpx_ge_u32 exec, s2, v1        the lanes whose step has come
        0x8A96_7E16, // s_andn2_b64 s[22:23], s[22:23], exec
        0xBF84_0003, // s_cbranch_scc0 END
        0x87FE_161C, // s_and_b64 exec, s[28:29], s[22:23]
        0x0604_0418, // v_add_f32 v2, s24, v2
        0xBF82_FFF5, // s_branch L
        // END:
        0xBEFE_041A, // s_mov_b64 exec, s[26:27]
        0x3408_0082, // v_lshlrev_b32 v4, 2, v0
        0xE070_1000, 0x8002_0204, // buffer_store_dword v2, v4, s[8:11], 0 offen
    ];
    let native = super::wave64_tests::native_of(&body, true, 64);
    let split = match runtime_form(&native, 32) {
        Ok(s) => s,
        Err(e) => panic!("two-wave form: {e}"),
    };
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(hw) = Host::new(false) else {
        eprintln!("no GPU; skipped");
        return;
    };
    let (root, pso) = hw.compute_pipeline(&split).expect("pipeline");
    let ud = vec![0u32; split.user_data_dwords().max(1) as usize];
    let out = hw
        .dispatch(&root, &pso, &Dispatch { roots: vec![(0, Root::Constants(ud))], views: &[View::RawUav(64)], samplers: &[], groups: [1, 1, 1] })
        .expect("dispatch");
    let mut bad = 0;
    for t in 0..64u32 {
        let n = t >> 3;
        let want = (n * n.saturating_sub(1) / 2) as f32;
        let got = f32::from_bits(out[0][t as usize]);
        if got != want {
            bad += 1;
            eprintln!("t={t} n={n}: {got}, want {want}");
        }
    }
    eprintln!("{bad} of 64 threads differ");
    assert_eq!(bad, 0);
}

/// The same loop with `_Tiled`'s mask read (its 0x53c/0x54c and 0x2d94): before the loop, two
/// numbers are put in lanes 0 and 1 of v100 (`v_writelane_b32`); inside it, after lanes 0..7
/// have left, every running lane reads them back (`v_readlane_b32 v100, 0` and `1`). The
/// console reads a lane whether it still runs or not.
#[test]
fn a_lane_read_inside_the_loop_sees_lanes_that_have_left() {
    use super::host::{Dispatch, Host, Root, View};
    let body = [
        0x2C02_0083, // v_lshrrev_b32 v1, 3, v0
        0x7E04_0280, // v_mov_b32 v2, 0
        0x7E0A_0280, // v_mov_b32 v5, 0
        0x7E0C_0280, // v_mov_b32 v6, 0
        0xBE9E_03FF, 0x0001_2345, // s_mov_b32 s30, 0x12345
        0xBE9F_03FF, 0x0000_6789, // s_mov_b32 s31, 0x6789
        0xD761_0064, 0x0001_001E, // v_writelane_b32 v100, s30, 0
        0xD761_0064, 0x0001_021F, // v_writelane_b32 v100, s31, 1
        0xBE94_0380, // s_mov_b32 s20, 0
        0xBE9A_047E, // s_mov_b64 s[26:27], exec
        0xBE96_047E, // s_mov_b64 s[22:23], exec
        // L:
        0xBE82_0314, // s_mov_b32 s2, s20
        0x8114_8114, // s_add_i32 s20, s20, 1
        0x7E06_0A02, // v_cvt_f32_i32 v3, s2
        0x7E30_0503, // v_readfirstlane_b32 s24, v3
        0xBE9C_047E, // s_mov_b64 s[28:29], exec
        0x7DAC_0202, // v_cmpx_ge_u32 exec, s2, v1
        0x8A96_7E16, // s_andn2_b64 s[22:23], s[22:23], exec
        0xBF84_0009, // s_cbranch_scc0 END
        0x87FE_161C, // s_and_b64 exec, s[28:29], s[22:23]
        0x0604_0418, // v_add_f32 v2, s24, v2
        0xD760_0020, 0x0001_0164, // v_readlane_b32 s32, v100, 0
        0xD760_0021, 0x0001_0364, // v_readlane_b32 s33, v100, 1
        0x7E0A_0220, // v_mov_b32 v5, s32
        0x7E0C_0221, // v_mov_b32 v6, s33
        0xBF82_FFEF, // s_branch L
        // END:
        0xBEFE_041A, // s_mov_b64 exec, s[26:27]
        0x3408_0082, // v_lshlrev_b32 v4, 2, v0
        0xE070_1000, 0x8002_0204, // buffer_store_dword v2, v4, s[8:11], 0 offen
        0xE070_1100, 0x8002_0504, // buffer_store_dword v5, v4, s[8:11], 0 offen offset:256
        0xE070_1200, 0x8002_0604, // buffer_store_dword v6, v4, s[8:11], 0 offen offset:512
    ];
    let mut native = super::wave64_tests::native_of(&body, true, 64);
    native.vgprs = 104;
    let split = match runtime_form(&native, 32) {
        Ok(s) => s,
        Err(e) => panic!("two-wave form: {e}"),
    };
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(hw) = Host::new(false) else {
        eprintln!("no GPU; skipped");
        return;
    };
    let (root, pso) = hw.compute_pipeline(&split).expect("pipeline");
    let ud = vec![0u32; split.user_data_dwords().max(1) as usize];
    let out = hw
        .dispatch(&root, &pso, &Dispatch { roots: vec![(0, Root::Constants(ud))], views: &[View::RawUav(192)], samplers: &[], groups: [1, 1, 1] })
        .expect("dispatch");
    let mut bad = 0;
    for t in 0..64usize {
        let ran = t >= 8;
        let want = if ran { [0x12345, 0x6789] } else { [0, 0] };
        let got = [out[0][64 + t], out[0][128 + t]];
        if got != want {
            bad += 1;
            if bad < 10 {
                eprintln!("t={t}: lanes 0 and 1 read as {got:x?}, want {want:x?}");
            }
        }
    }
    eprintln!("{bad} of 64 threads differ");
    assert_eq!(bad, 0);
}

/// `_Tiled`'s own shape of it: what goes in lanes 0 and 1 of v100 is a lane mask (`v_cmp_eq_u32`
/// of the thread against the tile's chosen pixel, s0 here), and inside the loop it is read back
/// and made EXEC (`s_and_saveexec_b64`): the chosen lane alone writes (there, the tile's column
/// of the 3D light volume). Each thread stores whether it was the one.
#[test]
fn a_lane_mask_kept_in_lanes_0_and_1_picks_its_lane_inside_the_loop() {
    use super::host::{Dispatch, Host, Root, View};
    let body = [
        0x2C02_0083, // v_lshrrev_b32 v1, 3, v0
        0x7E04_0280, // v_mov_b32 v2, 0
        0x7E0A_0280, // v_mov_b32 v5, 0
        0xD4C2_0006, 0x0002_0000, // v_cmp_eq_u32 s[6:7], s0, v0
        0xD761_0064, 0x0001_0006, // v_writelane_b32 v100, s6, 0
        0xD761_0064, 0x0001_0207, // v_writelane_b32 v100, s7, 1
        0xBE86_0480, // s_mov_b64 s[6:7], 0
        0xBE94_0380, // s_mov_b32 s20, 0
        0xBE9A_047E, // s_mov_b64 s[26:27], exec
        0xBE96_047E, // s_mov_b64 s[22:23], exec
        // L:
        0xBE82_0314, // s_mov_b32 s2, s20
        0x8114_8114, // s_add_i32 s20, s20, 1
        0x7E06_0A02, // v_cvt_f32_i32 v3, s2
        0x7E30_0503, // v_readfirstlane_b32 s24, v3
        0xBE9C_047E, // s_mov_b64 s[28:29], exec
        0x7DAC_0202, // v_cmpx_ge_u32 exec, s2, v1
        0x8A96_7E16, // s_andn2_b64 s[22:23], s[22:23], exec
        0xBF84_000A, // s_cbranch_scc0 END
        0x87FE_161C, // s_and_b64 exec, s[28:29], s[22:23]
        0x0604_0418, // v_add_f32 v2, s24, v2
        0xD760_0020, 0x0001_0164, // v_readlane_b32 s32, v100, 0
        0xD760_0021, 0x0001_0364, // v_readlane_b32 s33, v100, 1
        0xBEA2_2420, // s_and_saveexec_b64 s[34:35], s[32:33]
        0x7E0A_0281, // v_mov_b32 v5, 1
        0xBEFE_0422, // s_mov_b64 exec, s[34:35]
        0xBF82_FFEE, // s_branch L
        // END:
        0xBEFE_041A, // s_mov_b64 exec, s[26:27]
        0x3408_0082, // v_lshlrev_b32 v4, 2, v0
        0xE070_1000, 0x8002_0204, // buffer_store_dword v2, v4, s[8:11], 0 offen
        0xE070_1100, 0x8002_0504, // buffer_store_dword v5, v4, s[8:11], 0 offen offset:256
    ];
    let mut native = super::wave64_tests::native_of(&body, true, 64);
    native.vgprs = 104;
    let split = match runtime_form(&native, 32) {
        Ok(s) => s,
        Err(e) => panic!("two-wave form: {e}"),
    };
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(hw) = Host::new(false) else {
        eprintln!("no GPU; skipped");
        return;
    };
    let (root, pso) = hw.compute_pipeline(&split).expect("pipeline");
    let mut bad_runs = 0;
    for chosen in [8u32, 9, 20, 31, 32, 33, 40, 63] {
        let mut ud = vec![0u32; split.user_data_dwords().max(1) as usize];
        for u in &split.user_data {
            if u.root == crate::native::RootType::Constants && u.dword == 0 {
                ud[u.index as usize] = chosen;
            }
        }
        let out = hw
            .dispatch(&root, &pso, &Dispatch { roots: vec![(0, Root::Constants(ud))], views: &[View::RawUav(128)], samplers: &[], groups: [1, 1, 1] })
            .expect("dispatch");
        let picked: Vec<usize> = (0..64).filter(|&t| out[0][64 + t] != 0).collect();
        let ok = picked == [chosen as usize];
        if !ok {
            bad_runs += 1;
        }
        eprintln!("chosen lane {chosen}: the lanes that wrote {picked:?}{}", if ok { "" } else { "   <- wrong" });
    }
    assert_eq!(bad_runs, 0);
}

/// As above, the mask put in lanes 0 and 1 inside `if (t >= s1)` (in `_Tiled`, the branch at
/// 0x474 of the pixels that have work): with s1 >= 32 no lane of the first half enters, and the
/// console still writes lanes 0 and 1 (`v_writelane_b32` does not look at EXEC).
#[test]
fn a_lane_mask_written_where_only_the_second_half_runs() {
    use super::host::{Dispatch, Host, Root, View};
    let body = [
        0x2C02_0083, // v_lshrrev_b32 v1, 3, v0
        0x7E04_0280, // v_mov_b32 v2, 0
        0x7E0A_0280, // v_mov_b32 v5, 0
        0xD4C2_0006, 0x0002_0000, // v_cmp_eq_u32 s[6:7], s0, v0
        0xBEA8_047E, // s_mov_b64 s[40:41], exec
        0x7DA6_0001, // v_cmpx_le_u32 exec, s1, v0
        0xBF88_001B, // s_cbranch_execz OUT
        0xD761_0064, 0x0001_0006, // v_writelane_b32 v100, s6, 0
        0xD761_0064, 0x0001_0207, // v_writelane_b32 v100, s7, 1
        0xBE86_0480, // s_mov_b64 s[6:7], 0
        0xBE94_0380, // s_mov_b32 s20, 0
        0xBE9A_047E, // s_mov_b64 s[26:27], exec
        0xBE96_047E, // s_mov_b64 s[22:23], exec
        // L:
        0xBE82_0314, // s_mov_b32 s2, s20
        0x8114_8114, // s_add_i32 s20, s20, 1
        0x7E06_0A02, // v_cvt_f32_i32 v3, s2
        0x7E30_0503, // v_readfirstlane_b32 s24, v3
        0xBE9C_047E, // s_mov_b64 s[28:29], exec
        0x7DAC_0202, // v_cmpx_ge_u32 exec, s2, v1
        0x8A96_7E16, // s_andn2_b64 s[22:23], s[22:23], exec
        0xBF84_000A, // s_cbranch_scc0 END
        0x87FE_161C, // s_and_b64 exec, s[28:29], s[22:23]
        0x0604_0418, // v_add_f32 v2, s24, v2
        0xD760_0020, 0x0001_0164, // v_readlane_b32 s32, v100, 0
        0xD760_0021, 0x0001_0364, // v_readlane_b32 s33, v100, 1
        0xBEA2_2420, // s_and_saveexec_b64 s[34:35], s[32:33]
        0x7E0A_0281, // v_mov_b32 v5, 1
        0xBEFE_0422, // s_mov_b64 exec, s[34:35]
        0xBF82_FFEE, // s_branch L
        // END:
        0xBEFE_041A, // s_mov_b64 exec, s[26:27]
        // OUT:
        0xBEFE_0428, // s_mov_b64 exec, s[40:41]
        0x3408_0082, // v_lshlrev_b32 v4, 2, v0
        0xE070_1000, 0x8002_0204, // buffer_store_dword v2, v4, s[8:11], 0 offen
        0xE070_1100, 0x8002_0504, // buffer_store_dword v5, v4, s[8:11], 0 offen offset:256
    ];
    let mut native = super::wave64_tests::native_of(&body, true, 64);
    native.vgprs = 104;
    let split = match runtime_form(&native, 32) {
        Ok(s) => s,
        Err(e) => panic!("two-wave form: {e}"),
    };
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(hw) = Host::new(false) else {
        eprintln!("no GPU; skipped");
        return;
    };
    let (root, pso) = hw.compute_pipeline(&split).expect("pipeline");
    let mut bad_runs = 0;
    for (first, chosen) in [(20u32, 40u32), (20, 25), (32, 40), (32, 63), (40, 50), (8, 9)] {
        let mut ud = vec![0u32; split.user_data_dwords().max(1) as usize];
        for u in &split.user_data {
            if u.root == crate::native::RootType::Constants && u.dword < 2 {
                ud[u.index as usize] = if u.dword == 0 { chosen } else { first };
            }
        }
        let out = hw
            .dispatch(&root, &pso, &Dispatch { roots: vec![(0, Root::Constants(ud))], views: &[View::RawUav(128)], samplers: &[], groups: [1, 1, 1] })
            .expect("dispatch");
        let picked: Vec<usize> = (0..64).filter(|&t| out[0][64 + t] != 0).collect();
        let ok = picked == [chosen as usize];
        if !ok {
            bad_runs += 1;
        }
        eprintln!("lanes from {first} run, chosen lane {chosen}: the lanes that wrote {picked:?}{}", if ok { "" } else { "   <- wrong" });
    }
    assert_eq!(bad_runs, 0);
}
