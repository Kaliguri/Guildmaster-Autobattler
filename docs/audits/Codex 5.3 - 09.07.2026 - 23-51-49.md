# Guildmaster Autobattler Audit

**Author:** Codex 5.3  
**Date:** 09.07.2026 23:51:49  
**Scope:** `Assets/_Project/Scripts`, `Assets/_Project/Tests`, `scripts/run-tests.ps1`, architecture docs from `guildmaster-wiki`.

---

## Executive Summary

Overall architecture is strong: the project keeps a clear separation between simulation and presentation, has healthy assembly boundaries, and good deterministic combat test coverage.

Main risks are concentrated in infrastructure and multiplayer transition seams:
- local test pipeline is currently broken by outdated Unity path;
- battle seed + command relay still contain lockstep-era behavior that conflicts with the selected host-authoritative model;
- several gaps remain around runtime/loop/network test coverage.

---

## Findings (by severity)

## High

### 1) Local test entrypoint is broken (`scripts/run-tests.ps1`)
**What I found**
- Test script hardcodes `Unity.exe` path to `6000.0.23f1`.
- Project version is `6000.4.8f1` (`ProjectSettings/ProjectVersion.txt`).
- Actual run fails immediately with `Unity not found`.

**Impact**
- Team cannot rely on standard local test command.
- Higher risk of shipping regressions because the "official" test path silently rots.

**Recommendation**
- Make Unity path configurable (`$env:UNITY_EXE` / argument), then fallback to Hub-detected install.
- Add a quick preflight check in CI docs and in dev onboarding.

---

### 2) Seed contract for multiplayer is not finalized
**What I found**
- `CombatLifetimeScope` generates battle seed via `DateTime.UtcNow` + `UnityEngine.Random`.
- File already contains TODO that seed must come from host for MP.

**Impact**
- For host-authoritative this is acceptable short-term, but blocks clean replay/debug and creates hidden divergence risk when MP flow evolves.

**Recommendation**
- Introduce explicit `BattleStartContext` DTO (seed, start tick, setup payload) produced by host and consumed by `CombatLifetimeScope`.
- Make dev command for seed (`gm_rng_seed`) actually feed this pipeline, not just log.

---

## Medium

### 3) `NetworkCommandRelay` still mixes host-authoritative and lockstep assumptions
**What I found**
- Relay uses `ServerRpc` and then broadcasts command+tick to all clients (`ClientRpc`), enqueueing command on each peer.
- Comments acknowledge this as transitional behavior.

**Impact**
- Architectural ambiguity: easy to accidentally re-enable client-side simulation mutation later.
- Harder reasoning about authority boundaries and future netcode refactors.

**Recommendation**
- Split concerns explicitly:
  - `IntentRelay` (client -> host only),
  - `StateReplication` (host -> clients state/result stream).
- Keep command queue authoritative on host only.

---

### 4) Interpolation alpha in `CombatPresenter` is frame-based, not simulation-phase based
**What I found**
- `alpha = Time.deltaTime / TickDelta` in `CombatPresenter.Update`.
- This ignores accumulator remainder from `CombatLoopService`.

**Impact**
- During catch-up ticks or unstable frame time, interpolation can jitter/overshoot visually.
- Not a gameplay correctness bug, but degrades perceived smoothness and debugging readability.

**Recommendation**
- Expose normalized interpolation phase from loop (`accumulator / TickDelta`) and use it in presenter.
- Keep fallback clamp for safety.

---

### 5) Test coverage strong for combat logic, but weak for runtime orchestration seams
**What I found**
- Rich EditMode coverage for combat systems/effects/determinism.
- No direct tests found for:
  - `CombatLoopService` (accumulator + anti-spiral behavior),
  - network relay behavior (`NetworkCommandRelay`),
  - scene/game flow orchestration.

**Impact**
- Critical integration behavior can regress while core combat tests stay green.

**Recommendation**
- Add focused tests for:
  - catch-up cap behavior and pause/resume progression in loop service,
  - host-only enqueue contract in net relay,
  - minimal flow smoke (`Boot -> Battle -> End`) with mocked scene loader.

---

## Low

### 6) Avoidable GC pressure in `Stats.RebuildCache`
**What I found**
- Each cache rebuild allocates three new arrays (`flat`, `percentAdd`, `multAccum`).

**Impact**
- In heavy buff/debuff scenarios this may generate avoidable allocations and spikes.

**Recommendation**
- Reuse preallocated work arrays per `Stats` instance.
- Keep current logic but eliminate per-rebuild array creation.

---

## Strong Points (keep as is)

- Clean asmdef layering (`Core -> Data -> Combat` and upward composition in `Game`).
- Deterministic discipline in combat loop (fixed ticks, ordered systems, event queue cap).
- Effect system maturity: stacking, dispel, pre-damage hooks, and reactive event dispatch are thoughtfully implemented.
- Test suite quality for combat domain is already above average and documents intended behavior well.

---

## Suggested Prioritized Plan

1. **Fix test script portability first** (quick win, immediate team benefit).  
2. **Finalize host-provided battle start contract** (seed + start context).  
3. **Refactor network relay into explicit host-authoritative responsibilities**.  
4. **Patch interpolation phase API** for smoother presentation correctness.  
5. **Add 3-5 integration tests on loop/network/flow seams**.  
6. **Apply `Stats` allocation micro-optimization** if profiler confirms pressure.

---

## Final Verdict

The codebase is in a good architectural state for current single-host deterministic combat. Most risks are not "bad core design", but **transition debt** at boundaries (tooling, multiplayer authority seam, orchestration tests). Fixing those will significantly improve consistency and reduce future rework.
