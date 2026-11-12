Track shapes in ../../layouts/ are derived from the track data of SIMRacingApps
(https://github.com/SIMRacingApps/SIMRacingAppsServer, Apache License 2.0, copyright notice in COPYRIGHT.TXT).

Changes made by Shiftline:
- GPS coordinates projected to metres (local tangent plane) and centred on the bounding box.
- Smoothed with a centripetal Catmull-Rom spline and resampled to evenly spaced points
  (percent-table shapes keep their original LapDistPct steps).
- Some layouts were corrected manually; those carry check = "corrected" and the reason in
  provenance.correction.
- Layouts that did not pass review are not included.
