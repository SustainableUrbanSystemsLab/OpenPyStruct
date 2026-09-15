# GUI — component chrome copied from Eddy3D

These files are the inline toggle/dropdown widgets, button, label, progress-bar and
background-run helpers from the Eddy3D Grasshopper plugins
(https://github.com/Eddy3D-Dev/Eddy3D, `GUI/`), copied so this plugin looks and behaves like
Eddy3D **without** referencing its assemblies. Each file carries an "Adapted from Eddy3D" header.

Edits on copy: namespace `GUI` → `OpenPyStruct.GH.GUI`; the parameter category is
`OpenPyStruct`; the triangle-picker widget, the Eto library browser dialog and Eddy3D's docs
link were removed (the docs menu item points at `Docs.cs` here).

## Licensing

Eddy3D is GPL-3.0-or-later and these files stay **GPL-3.0-or-later** (decision 2026-09-15). Because
the plugin assembly links them, `OpenPyStruct.gha` as a whole is distributed under GPL-3.0-or-later;
see `grasshopper/LICENSE`. The Python package, the Rhino-free `OpenPyStruct.Core` library and the
rest of the repository remain MIT. Nothing else depends on this folder: replace
`GH_BeautifulComponent` with a plain `GH_Component` base to build an MIT-only plugin.
