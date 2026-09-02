# Magic Capture Desktop 3.5.0

## Design Tools

- Added an on-demand floating screen color picker with a 15×15 live sample, magnified pixel preview, physical screen coordinates and bounded 32-color history.
- Added persisted saved swatches, HSV/CMYK/CSS/C#/C++ formats, average/dominant region colors, palette extraction and WCAG contrast checking.
- Added an on-demand full-desktop measurement overlay with physical-pixel ruler, horizontal/vertical deltas, crosshair, relative coordinates, inches/cm/pixels and protractor angle.
- Added custom DPI entry plus explicit DPI calibration from a measured pixel length and known physical length.
- Added Screen Focus and local whiteboard/draw-on-desktop modes on a frozen desktop surface.
- The live color-sampling timer now stops when Design Tools loses activation, and history UI refresh is throttled to avoid unnecessary ListView churn.

All Design Tools remain local and on-demand; no tray-idle polling or new native/runtime dependency is introduced.
