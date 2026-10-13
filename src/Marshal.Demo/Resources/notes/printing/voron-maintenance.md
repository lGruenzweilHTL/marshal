# Voron maintenance

## Weekly

- Clean the PEI sheet with isopropyl alcohol; dish soap once a month.
- Check the belt tension: both belts should read about 110 Hz.
- Empty the purge bucket.

## Every 200 print hours

- Lubricate the linear rails with a drop of light oil per carriage.
- Check the nozzle for wear; the 0.4 mm brass nozzle was replaced in March.
- Run `QUAD_GANTRY_LEVEL`, then `BED_MESH_CALIBRATE` and save the mesh.

## Known issues

- The Z endstop pin sticks when the enclosure is above 55 °C. Let it cool before homing.
