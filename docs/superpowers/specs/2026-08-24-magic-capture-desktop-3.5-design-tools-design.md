# Magic Capture Desktop 3.5 Design Tools Design

## Goal
Complete the local design-utility backlog without adding resident work: color intelligence, measurement, focus and whiteboard tools open only on explicit user action.

## Architecture
`ColorValue`, `ColorContrast`, `ColorPaletteExtractor` and `ScreenMeasurement` remain pure Core logic. `DesignToolsWindow` owns the live 15×15 sampler and persisted bounded history/swatches. `MeasurementOverlayWindow` captures a frozen virtual desktop once, then performs all ruler/focus/whiteboard interaction in-memory until closed.

## Performance and safety
- Live picker samples every 100 ms only while the Design Tools window is active.
- History is bounded to 32 colors, swatches to 24, palette sampling to 250,000 pixels and 16 colors.
- Measurement uses physical-pixel conversion and DPI bounded to 10–2000.
- Whiteboard strokes are bounded to 8,192 points each.
- No background service, account, cloud call or native dependency is added.

## UX
One Design Tools window groups color formats, palette, WCAG and measurement launchers. Ruler reports X/Y, ΔX/ΔY, distance, physical units and angle. DPI calibration derives DPI from a known physical reference. Esc closes full-screen overlays.

## Release criteria
Repository, structural and lexical gates must be clean; IDs #335–#361 are promoted only where end-to-end UI and runtime paths exist. Windows manual validation remains required for mixed-DPI/negative-coordinate behavior.
