# Magic Capture Desktop 3.5 Design Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship bounded local color and measurement utilities without increasing idle cost.

**Architecture:** Pure Core math/color primitives feed two on-demand WinUI windows. Persistent data reuses normalized `AppSettings`; full-screen measurement works on one frozen desktop capture.

**Tech Stack:** .NET 10, WinUI 3, System.Drawing image buffer services already in the app.

**Spec:** `docs/superpowers/specs/2026-08-24-magic-capture-desktop-3.5-design-tools-design.md`

## Global Constraints
- No background service or cloud dependency.
- Physical-pixel measurement must remain correct under mixed DPI.
- Lists, samples, strokes and DPI are bounded before allocation/use.

---

### Task 1: Color Core and persistence
- [x] Add HSV/CMYK/CSS/C#/C++ formatting and regression tests.
- [x] Add WCAG contrast and bounded palette extraction.
- [x] Add normalized 32-color history and 24 saved swatches.

### Task 2: Floating Design Tools window
- [x] Add live bounded screen sampler, magnifier and coordinate HUD.
- [x] Add palette/average/dominant analysis and copy formats.
- [x] Stop timer on deactivation and throttle history UI refresh.

### Task 3: Measurement overlay
- [x] Add physical-pixel ruler, H/V deltas, crosshair, relative coordinates and angle.
- [x] Add inches/cm/pixels, custom DPI and reference-length calibration.
- [x] Add Screen Focus and bounded Whiteboard modes.

### Task 4: Release evidence
- [x] Add repository contracts for 3.5 invariants.
- [ ] Run all three source gates after version bump.
- [ ] Verify ledger counts and deterministic archive twice.
