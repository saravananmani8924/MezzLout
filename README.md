# Mezz_L_Out

Tekla Structures plugin scaffold for generating a parametric mezzanine layout with:

- Main beam grid from X/Y spacing.
- Joists in user-selected direction with flexible spacing expressions (`n*spacing`, lists).
- UI-driven custom fin-plate connections (`-200000`) at both joist ends.
- Column insertion at mezzanine grid intersections.
- Default beam-to-column web/flange connections.

Main implementation file: `src/Mezz_L_Out.cs`.
