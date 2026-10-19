/**
 * Pure point cloud planners (Node side, tunable without restarting Revit). Input comes from
 * sample_point_cloud outFile ("x y z" meters). Slices become wall face segments for planWallsFromCad;
 * duplicated clouds (every wall seen twice at a constant offset) are detected as a dominant shift.
 */
import { readFile } from "node:fs/promises";
import type { CadCurve, Pt } from "./cadWalls.js";

export type P3 = [number, number, number];

export async function readXyz(path: string): Promise<P3[]> {
  const text = await readFile(path, "utf8");
  const out: P3[] = [];
  for (const line of text.split("\n")) {
    if (!line) continue;
    const [x, y, z] = line.split(" ").map(Number);
    if (Number.isFinite(x) && Number.isFinite(y) && Number.isFinite(z)) out.push([x, y, z]);
  }
  return out;
}

/**
 * One point per grid cell (plan): the mean of the real points in the cell, the cheapest dedupe for
 * superimposed copies and dense scans. Snapping to cell centres split a face lying on a cell border into
 * two parallel lines one cell apart.
 */
export function gridDedupe2D(points: Pt[], cell: number): Pt[] {
  const acc = new Map<string, [number, number, number]>();
  for (const p of points) {
    const k = `${Math.floor(p[0] / cell)},${Math.floor(p[1] / cell)}`;
    const a = acc.get(k);
    if (a) { a[0] += p[0]; a[1] += p[1]; a[2]++; } else acc.set(k, [p[0], p[1], 1]);
  }
  return [...acc.values()].map(([x, y, n]) => [x / n, y / n] as Pt);
}

function rng(seed: number) { let s = seed >>> 0; return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296); }

export interface SegmentOptions { cell?: number; inlierTol?: number; minLength?: number; maxGap?: number; iterations?: number; maxSegments?: number; seed?: number; minPoints?: number }
export interface Segment { a: Pt; b: Pt; length: number; points: number; angleDeg: number }

/**
 * Sequential RANSAC on a thin horizontal slice: best line by inlier count, split its inliers into runs
 * (gaps > maxGap break a run), keep runs >= minLength, remove them, repeat. Deterministic by seed.
 */
export function extractSegments(slice: Pt[], options: SegmentOptions = {}): Segment[] {
  const cell = options.cell ?? 0.02, tol = options.inlierTol ?? 0.015, minLength = options.minLength ?? 0.3,
    maxGap = options.maxGap ?? 0.12, iterations = options.iterations ?? 400, maxSegments = options.maxSegments ?? 2000,
    minPoints = options.minPoints ?? Math.max(8, Math.round(minLength / cell / 2));
  const rand = rng(options.seed ?? 7);
  let pts = gridDedupe2D(slice, cell);
  const segments: Segment[] = [];
  let misses = 0;
  while (pts.length >= minPoints && segments.length < maxSegments && misses < 30) {
    let best: { u: Pt; o: Pt; inliers: number[] } | null = null;
    for (let it = 0; it < iterations; it++) {
      const i = Math.floor(rand() * pts.length);
      // second sample near the first: walls are local, far pairs rarely lie on one face
      let j = -1;
      for (let t = 0; t < 12; t++) {
        const k = Math.floor(rand() * pts.length);
        const d = Math.hypot(pts[k][0] - pts[i][0], pts[k][1] - pts[i][1]);
        if (k !== i && d > 3 * cell && d < 1.5) { j = k; break; }
      }
      if (j < 0) continue;
      const dx = pts[j][0] - pts[i][0], dy = pts[j][1] - pts[i][1], L = Math.hypot(dx, dy);
      const u: Pt = [dx / L, dy / L], n: Pt = [-u[1], u[0]], o = pts[i];
      const inliers: number[] = [];
      for (let k = 0; k < pts.length; k++)
        if (Math.abs((pts[k][0] - o[0]) * n[0] + (pts[k][1] - o[1]) * n[1]) <= tol) inliers.push(k);
      if (!best || inliers.length > best.inliers.length) best = { u, o, inliers };
    }
    if (!best || best.inliers.length < minPoints) { misses++; continue; }
    // refit direction by PCA on inliers
    const ins = best.inliers.map((k) => pts[k]);
    const cx = ins.reduce((s, q) => s + q[0], 0) / ins.length, cy = ins.reduce((s, q) => s + q[1], 0) / ins.length;
    let sxx = 0, syy = 0, sxy = 0;
    for (const q of ins) { sxx += (q[0] - cx) ** 2; syy += (q[1] - cy) ** 2; sxy += (q[0] - cx) * (q[1] - cy); }
    const ang = 0.5 * Math.atan2(2 * sxy, sxx - syy);
    const u: Pt = [Math.cos(ang), Math.sin(ang)], n: Pt = [-u[1], u[0]];
    const keepIdx = best.inliers.filter((k) => Math.abs((pts[k][0] - cx) * n[0] + (pts[k][1] - cy) * n[1]) <= tol);
    const along = keepIdx.map((k) => ({ k, s: (pts[k][0] - cx) * u[0] + (pts[k][1] - cy) * u[1] })).sort((a, b) => a.s - b.s);
    const used = new Set<number>();
    let runStart = 0, found = false;
    for (let r = 1; r <= along.length; r++) {
      if (r === along.length || along[r].s - along[r - 1].s > maxGap) {
        const run = along.slice(runStart, r);
        const len = run.length ? run[run.length - 1].s - run[0].s : 0;
        if (len >= minLength && run.length >= minPoints) {
          const s0 = run[0].s, s1 = run[run.length - 1].s;
          const off = run.reduce((s, q) => s + ((pts[q.k][0] - cx) * n[0] + (pts[q.k][1] - cy) * n[1]), 0) / run.length;
          const a: Pt = [cx + u[0] * s0 + n[0] * off, cy + u[1] * s0 + n[1] * off];
          const b: Pt = [cx + u[0] * s1 + n[0] * off, cy + u[1] * s1 + n[1] * off];
          segments.push({ a: [r3(a[0]), r3(a[1])], b: [r3(b[0]), r3(b[1])], length: r3(len), points: run.length, angleDeg: r3(((Math.atan2(u[1], u[0]) * 180) / Math.PI + 180) % 180) });
          found = true;
        }
        for (const q of run) used.add(q.k);
        runStart = r;
      }
    }
    if (!found) misses++; else misses = 0;
    pts = pts.filter((_, k) => !used.has(k));
  }
  return segments;
}

const r3 = (v: number) => Math.round(v * 1000) / 1000 + 0;

export const segmentsToCurves = (segments: Segment[], layer = "POINTCLOUD"): CadCurve[] =>
  segments.map((s) => ({ t: "line", l: layer, p: [s.a, s.b] }));

/**
 * A cloud duplicated with an offset shows every face twice: pairs of parallel segments with (nearly) the
 * same length and the same translation. Returns the dominant translation if enough segments share it.
 */
export function detectDuplicateShift(segments: Segment[], options: { lengthTol?: number; angleTolDeg?: number; bin?: number; minShare?: number; minShift?: number } = {}) {
  const lengthTol = options.lengthTol ?? 0.05, angTol = options.angleTolDeg ?? 1.5, bin = options.bin ?? 0.03,
    minShare = options.minShare ?? 0.25, minShift = options.minShift ?? 0.05;
  const votes = new Map<string, { dx: number; dy: number; n: number }>();
  const mid = (s: Segment): Pt => [(s.a[0] + s.b[0]) / 2, (s.a[1] + s.b[1]) / 2];
  const long = segments.filter((s) => s.length >= 0.5);
  for (let i = 0; i < long.length; i++)
    for (let j = 0; j < long.length; j++) {
      if (i === j) continue;
      const A = long[i], B = long[j];
      let da = Math.abs(A.angleDeg - B.angleDeg); da = Math.min(da, 180 - da);
      if (da > angTol || Math.abs(A.length - B.length) > lengthTol * Math.max(A.length, 1)) continue;
      const ma = mid(A), mb = mid(B), dx = mb[0] - ma[0], dy = mb[1] - ma[1];
      if (Math.hypot(dx, dy) < minShift) continue;
      const k = `${Math.round(dx / bin)},${Math.round(dy / bin)}`;
      const v = votes.get(k) ?? { dx: 0, dy: 0, n: 0 };
      v.dx += dx; v.dy += dy; v.n++; votes.set(k, v);
    }
  const top = [...votes.values()].sort((a, b) => b.n - a.n)[0];
  if (!top || long.length === 0) return { duplicated: false, share: 0, shift: null as Pt | null, segmentsConsidered: long.length };
  const share = top.n / long.length;
  return { duplicated: share >= minShare, share: r3(share), shift: [r3(top.dx / top.n), r3(top.dy / top.n)] as Pt, segmentsConsidered: long.length };
}

/** Keep the copy on one side of a known shift: drop segments that have a twin at +shift. */
export function dropShiftedCopy(segments: Segment[], shift: Pt, tol = 0.06): Segment[] {
  const mid = (s: Segment): Pt => [(s.a[0] + s.b[0]) / 2, (s.a[1] + s.b[1]) / 2];
  return segments.filter((s) => {
    const m = mid(s);
    const twinBehind = segments.some((o) => o !== s && Math.hypot(mid(o)[0] + shift[0] - m[0], mid(o)[1] + shift[1] - m[1]) <= tol
      && Math.abs(o.length - s.length) <= Math.max(0.1, 0.05 * s.length));
    return !twinBehind; // a segment that is some other segment moved by +shift is the copy
  });
}

/**
 * Horizontal surfaces from a z histogram (sample_point_cloud zHistogram): local maxima above
 * `minShare` of the tallest bin, merged within `merge` meters. Floors and ceilings are the spikes.
 */
export function levelsFromHistogram(h: { fromMeters: number; binMeters: number; counts: number[] }, options: { minShare?: number; merge?: number } = {}) {
  const minShare = options.minShare ?? 0.15, merge = options.merge ?? 0.15;
  const maxC = Math.max(...h.counts, 1);
  const peaks: { z: number; count: number }[] = [];
  h.counts.forEach((c, i) => {
    if (c < minShare * maxC) return;
    if ((i > 0 && h.counts[i - 1] > c) || (i < h.counts.length - 1 && h.counts[i + 1] > c)) return;
    const z = h.fromMeters + (i + 0.5) * h.binMeters;
    const last = peaks[peaks.length - 1];
    if (last && z - last.z <= merge) { if (c > last.count) { last.z = z; last.count = c; } }
    else peaks.push({ z, count: c });
  });
  return peaks.map((p) => ({ z: r3(p.z), count: p.count, share: r3(p.count / maxC) }));
}
