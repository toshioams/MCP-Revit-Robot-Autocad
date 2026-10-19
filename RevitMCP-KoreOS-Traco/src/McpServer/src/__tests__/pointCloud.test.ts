import { describe, it, expect } from "vitest";
import { extractSegments, detectDuplicateShift, dropShiftedCopy, levelsFromHistogram, gridDedupe2D, segmentsToCurves } from "../pointCloud.js";
import { planWallsFromCad, type Pt } from "../cadWalls.js";

// noisy points along a segment, both faces of a wall
function face(a: Pt, b: Pt, step = 0.01, noise = 0.003, seed = 1): Pt[] {
  let s = seed; const r = () => ((s = (s * 16807) % 2147483647) / 2147483647 - 0.5) * 2 * noise;
  const L = Math.hypot(b[0] - a[0], b[1] - a[1]), n = Math.round(L / step);
  return Array.from({ length: n + 1 }, (_, i) => [a[0] + (b[0] - a[0]) * i / n + r(), a[1] + (b[1] - a[1]) * i / n + r()] as Pt);
}
function room(dx = 0, dy = 0): Pt[] {
  const t = 0.15, W = 5, H = 4, sh = (p: Pt): Pt => [p[0] + dx, p[1] + dy];
  return [
    ...face(sh([0, 0]), sh([W, 0])), ...face(sh([t, t]), sh([W - t, t])),
    ...face(sh([W, 0]), sh([W, H])), ...face(sh([W - t, t]), sh([W - t, H - t])),
    ...face(sh([W, H]), sh([0, H])), ...face(sh([W - t, H - t]), sh([t, H - t])),
    ...face(sh([0, H]), sh([0, 0])), ...face(sh([t, H - t]), sh([t, t])),
  ];
}

describe("point cloud planners", () => {
  it("dedupes superimposed copies to one point per cell", () => {
    const pts = room();
    expect(gridDedupe2D([...pts, ...pts], 0.02).length).toBe(gridDedupe2D(pts, 0.02).length);
  });

  it("extracts the 8 faces of a scanned 15 cm room and the wall planner turns them into 4 walls", () => {
    const segs = extractSegments(room(), { seed: 3 });
    expect(segs.length).toBe(8);
    const plan = planWallsFromCad(segmentsToCurves(segs), { snapThicknesses: [0.15], snapTolerance: 0.02 });
    expect(plan.walls).toHaveLength(4);
    expect(plan.walls.every((w) => w.thickness === 0.15)).toBe(true);
  });

  it("detects a cloud duplicated with an offset and keeps one copy", () => {
    const segs = extractSegments([...room(), ...room(0.42, 0.31)], { seed: 5 });
    const d = detectDuplicateShift(segs);
    expect(d.duplicated).toBe(true);
    const shift = d.shift!;
    expect(Math.abs(Math.abs(shift[0]) - 0.42)).toBeLessThan(0.03);
    expect(Math.abs(Math.abs(shift[1]) - 0.31)).toBeLessThan(0.03);
    const kept = dropShiftedCopy(segs, shift);
    expect(kept.length).toBe(8);
  });

  it("finds floor and ceiling from a z histogram", () => {
    const counts = Array.from({ length: 160 }, (_, i) => (i === 3 ? 9000 : i === 138 ? 6000 : 200 + (i % 7)));
    const levels = levelsFromHistogram({ fromMeters: -0.05, binMeters: 0.02, counts });
    expect(levels.map((l) => l.z)).toEqual([0.02, 2.72]);
  });
});
