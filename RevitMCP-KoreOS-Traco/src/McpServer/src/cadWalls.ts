/**
 * Pure wall planner for CAD plans (DWG linked in Revit, read with inspect_cad "curves").
 * Finds pairs of parallel face lines at a wall-like distance, turns them into centerlines with a
 * thickness, bridges collinear pieces across junctions and openings, and extends wall ends to
 * meet at L/T corners. Output is a proposal to review, not a model: layers, block content and
 * drawing conventions vary per office. Meters, plan coordinates.
 */

export type Pt = [number, number];
export interface CadCurve { t: string; l: string; b?: string; p: Pt[]; c?: Pt; r?: number }

export interface WallPlanOptions {
  minThickness?: number;     // 0.07
  maxThickness?: number;     // 0.45
  angleTolDeg?: number;      // 1
  offsetTol?: number;        // 0.005 - lines closer than this are the same line
  mergeGap?: number;         // 0.02 - collinear gaps up to this are drawing noise
  minOverlap?: number;       // 0.15 - shortest face overlap accepted as a wall piece
  minOpening?: number;       // 0.40
  maxOpening?: number;       // 3.00 - wider gaps split the wall
  snapThicknesses?: number[];// e.g. [0.15, 0.25]
  snapTolerance?: number;    // 0.015
  includeBlocks?: boolean;   // false - block geometry (doors, furniture) is not wall faces
  minWallLength?: number;    // 0.5 - shorter runs are reported as shortPieces (often columns), not walls
  excludeRegions?: { minX: number; minY: number; maxX: number; maxY: number }[]; // walls whose midpoint falls inside are dropped
  unpairedLimit?: number;    // 50 - longest unpaired lines reported
}

export interface PlannedWall { id: string; start: Pt; end: Pt; thickness: number; length: number; angleDeg: number; drawnThickness: number }
export interface PlannedOpening { wallId: string; center: Pt; width: number; kind: "door" | "opening"; evidence: string[] }
export interface WallPlan {
  walls: PlannedWall[];
  shortPieces: { center: Pt; length: number; thickness: number; angleDeg: number }[];
  excluded: number;
  openings: PlannedOpening[];
  thicknesses: { thickness: number; count: number; totalLength: number }[];
  unpaired: { p: [Pt, Pt]; length: number }[];
  stats: Record<string, number>;
}

const D = { minThickness: 0.07, maxThickness: 0.45, angleTolDeg: 1, offsetTol: 0.005, mergeGap: 0.02,
  minOverlap: 0.15, minOpening: 0.4, maxOpening: 3.0, snapThicknesses: [] as number[], snapTolerance: 0.015, includeBlocks: false,
  minWallLength: 0.5, excludeRegions: [] as { minX: number; minY: number; maxX: number; maxY: number }[], unpairedLimit: 50 };

type Iv = [number, number];
interface Line { c: number; ivs: Iv[] }                    // merged collinear line: offset + intervals along u
interface Dir { u: Pt; n: Pt; lines: Line[] }
interface Piece { dir: number; c: number; s0: number; s1: number; t: number; drawn: number }

const r4 = (v: number) => Math.round(v * 1e4) / 1e4 + 0; // + 0 turns -0 into 0
const dot = (a: Pt, b: Pt) => a[0] * b[0] + a[1] * b[1];

function mergeIntervals(ivs: Iv[], gap: number): Iv[] {
  const s = [...ivs].sort((a, b) => a[0] - b[0]);
  const out: Iv[] = [];
  for (const iv of s) {
    const last = out[out.length - 1];
    if (last && iv[0] <= last[1] + gap) last[1] = Math.max(last[1], iv[1]);
    else out.push([iv[0], iv[1]]);
  }
  return out;
}

function subtract(ivs: Iv[], cut: Iv): Iv[] {
  const out: Iv[] = [];
  for (const [a, b] of ivs) {
    if (cut[1] <= a || cut[0] >= b) { out.push([a, b]); continue; }
    if (cut[0] > a) out.push([a, cut[0]]);
    if (cut[1] < b) out.push([cut[1], b]);
  }
  return out;
}

export function planWallsFromCad(curves: CadCurve[], options: WallPlanOptions = {}): WallPlan {
  const o = { ...D, ...options };
  const sinTol = Math.sin((o.angleTolDeg * Math.PI) / 180);

  // 1. segments
  const segs: [Pt, Pt][] = [];
  for (const cv of curves) {
    if (cv.b && !o.includeBlocks) continue;
    if (cv.t !== "line" && cv.t !== "poly") continue;
    for (let i = 1; i < cv.p.length; i++) {
      const a = cv.p[i - 1], b = cv.p[i];
      if (Math.hypot(b[0] - a[0], b[1] - a[1]) >= 0.02) segs.push([a, b]);
    }
  }

  // 2. direction clusters, 3. merge collinear lines
  const dirs: Dir[] = [];
  const raw: { dir: number; c: number; iv: Iv }[] = [];
  for (const [a, b] of [...segs].sort((x, y) => Math.hypot(y[1][0] - y[0][0], y[1][1] - y[0][1]) - Math.hypot(x[1][0] - x[0][0], x[1][1] - x[0][1]))) {
    const len = Math.hypot(b[0] - a[0], b[1] - a[1]);
    let u: Pt = [(b[0] - a[0]) / len, (b[1] - a[1]) / len];
    let k = dirs.findIndex((d) => Math.abs(d.u[0] * u[1] - d.u[1] * u[0]) < sinTol);
    if (k < 0) {
      if (u[0] < -1e-9 || (Math.abs(u[0]) <= 1e-9 && u[1] < 0)) u = [-u[0], -u[1]];
      dirs.push({ u, n: [-u[1], u[0]], lines: [] });
      k = dirs.length - 1;
    }
    const d = dirs[k];
    const s0 = dot(a, d.u), s1 = dot(b, d.u);
    raw.push({ dir: k, c: (dot(a, d.n) + dot(b, d.n)) / 2, iv: [Math.min(s0, s1), Math.max(s0, s1)] });
  }
  raw.sort((x, y) => x.dir - y.dir || x.c - y.c);
  for (const s of raw) {
    const lines = dirs[s.dir].lines;
    const last = lines[lines.length - 1];
    if (last && Math.abs(s.c - last.c) <= o.offsetTol) last.ivs.push(s.iv);
    else lines.push({ c: s.c, ivs: [s.iv] });
  }
  let lineCount = 0;
  for (const d of dirs) for (const l of d.lines) { l.ivs = mergeIntervals(l.ivs, o.mergeGap); lineCount++; }

  // 4. pair faces (closest first), consuming used portions
  const pieces: Piece[] = [];
  dirs.forEach((d, k) => {
    const cands: { i: number; j: number; dist: number }[] = [];
    for (let i = 0; i < d.lines.length; i++)
      for (let j = i + 1; j < d.lines.length; j++) {
        const dist = d.lines[j].c - d.lines[i].c;
        if (dist > o.maxThickness + o.offsetTol) break;
        if (dist >= o.minThickness - o.offsetTol) cands.push({ i, j, dist });
      }
    cands.sort((x, y) => x.dist - y.dist);
    for (const { i, j, dist } of cands) {
      const A = d.lines[i], B = d.lines[j];
      for (const ia of [...A.ivs]) for (const ib of [...B.ivs]) {
        const lo = Math.max(ia[0], ib[0]), hi = Math.min(ia[1], ib[1]);
        if (hi - lo < o.minOverlap) continue;
        // only still-free parts
        const freeA = A.ivs.filter((v) => v[1] > lo && v[0] < hi), freeB = B.ivs.filter((v) => v[1] > lo && v[0] < hi);
        for (const fa of freeA) for (const fb of freeB) {
          const l2 = Math.max(lo, fa[0], fb[0]), h2 = Math.min(hi, fa[1], fb[1]);
          if (h2 - l2 < o.minOverlap) continue;
          pieces.push({ dir: k, c: (A.c + B.c) / 2, s0: l2, s1: h2, t: dist, drawn: dist });
          A.ivs = subtract(A.ivs, [l2, h2]); B.ivs = subtract(B.ivs, [l2, h2]);
        }
      }
    }
  });

  // overlapping bands of the same direction are one wall: finish lines nested inside the faces, or
  // drywall drawn with four lines (two 7 cm pairs offset 3 cm = one 10 cm wall). Union of faces and extents.
  const groups: Piece[][] = [];
  for (const p of pieces) {
    const hits = groups.filter((g) => g.some((q) => q.dir === p.dir
      && Math.abs(q.c - p.c) < (q.drawn + p.drawn) / 2 - o.offsetTol
      && Math.min(q.s1, p.s1) - Math.max(q.s0, p.s0) >= o.minOverlap));
    const merged = hits.flat().concat(p);
    for (const h of hits) groups.splice(groups.indexOf(h), 1);
    groups.push(merged);
  }
  pieces.length = 0;
  for (const g of groups) {
    const lo = Math.min(...g.map((q) => q.c - q.drawn / 2)), hi = Math.max(...g.map((q) => q.c + q.drawn / 2));
    pieces.push({ dir: g[0].dir, c: (lo + hi) / 2, drawn: hi - lo, t: hi - lo,
      s0: Math.min(...g.map((q) => q.s0)), s1: Math.max(...g.map((q) => q.s1)) });
  }

  // thickness snap
  for (const p of pieces) {
    const snap = o.snapThicknesses.filter((t) => Math.abs(t - p.drawn) <= o.snapTolerance)
      .sort((x, y) => Math.abs(x - p.drawn) - Math.abs(y - p.drawn))[0];
    p.t = snap ?? Math.round(p.drawn * 200) / 200;
  }

  // 5. bridge collinear pieces of equal thickness (junction gaps and openings)
  const arcs = curves.filter((cv) => cv.t === "arc" && cv.r !== undefined && cv.r >= 0.5 && cv.r <= 1.3 && cv.c);
  const blockCurves = curves.filter((cv) => cv.b);
  interface Run { dir: number; c: number; t: number; drawn: number; s0: number; s1: number; gaps: Iv[] }
  const runs: Run[] = [];
  const sorted = [...pieces].sort((x, y) => x.dir - y.dir || x.t - y.t || x.s0 - y.s0);
  for (const p of sorted) {
    const run = runs.find((r) => r.dir === p.dir && Math.abs(r.t - p.t) < 1e-6 && Math.abs(r.c - p.c) <= 0.01 && p.s0 - r.s1 <= o.maxOpening && p.s1 > r.s0);
    if (run) {
      if (p.s0 - run.s1 > o.mergeGap) run.gaps.push([run.s1, p.s0]);
      run.s1 = Math.max(run.s1, p.s1);
    } else runs.push({ dir: p.dir, c: p.c, t: p.t, drawn: p.drawn, s0: p.s0, s1: p.s1, gaps: [] });
  }

  const toXY = (dir: number, c: number, s: number): Pt => {
    const d = dirs[dir];
    return [d.u[0] * s + d.n[0] * c, d.u[1] * s + d.n[1] * c];
  };

  // 6. extend ends to L/T junctions
  for (let i = 0; i < runs.length; i++) {
    const ri = runs[i], di = dirs[ri.dir];
    for (const side of ["s0", "s1"] as const) {
      let best: { s: number; move: number } | null = null;
      for (let j = 0; j < runs.length; j++) {
        if (j === i || runs[j].dir === ri.dir) continue;
        const rj = runs[j], dj = dirs[rj.dir];
        const den = di.u[0] * dj.n[0] + di.u[1] * dj.n[1];           // u_i · n_j
        if (Math.abs(den) < 1e-9) continue;
        const pi0 = toXY(ri.dir, ri.c, 0);
        const s = (rj.c - dot(pi0, dj.n)) / den;                      // param along i where it meets j's centerline
        const x = toXY(ri.dir, ri.c, s);
        const sj = dot(x, dj.u);
        const reach = rj.t / 2 + 0.02;
        if (Math.abs(s - ri[side]) > reach / Math.abs(den)) continue;
        if (sj < rj.s0 - ri.t / 2 - 0.02 || sj > rj.s1 + ri.t / 2 + 0.02) continue;
        const move = Math.abs(s - ri[side]);
        if (!best || move < best.move) best = { s, move };
      }
      if (best) ri[side] = best.s;
    }
  }

  // 7. openings on bridged gaps (skip gaps filled by a crossing wall)
  const walls: PlannedWall[] = [];
  const openings: PlannedOpening[] = [];
  const shortPieces: WallPlan["shortPieces"] = [];
  let excluded = 0;
  const angleOf = (dir: number) => r4((Math.atan2(dirs[dir].u[1], dirs[dir].u[0]) * 180) / Math.PI);
  runs.filter((r) => {
    const mid = toXY(r.dir, r.c, (r.s0 + r.s1) / 2);
    if (o.excludeRegions.some((b) => mid[0] >= b.minX && mid[0] <= b.maxX && mid[1] >= b.minY && mid[1] <= b.maxY)) { excluded++; return false; }
    if (r.s1 - r.s0 < o.minWallLength) {
      if (r.s1 - r.s0 >= 0.1) shortPieces.push({ center: [r4(mid[0]), r4(mid[1])], length: r4(r.s1 - r.s0), thickness: r4(r.t), angleDeg: angleOf(r.dir) });
      return false;
    }
    return true;
  }).forEach((r, idx) => {
    const id = `W${idx + 1}`;
    const start = toXY(r.dir, r.c, r.s0), end = toXY(r.dir, r.c, r.s1);
    walls.push({ id, start: [r4(start[0]), r4(start[1])], end: [r4(end[0]), r4(end[1])], thickness: r4(r.t), drawnThickness: r4(r.drawn),
      length: r4(r.s1 - r.s0), angleDeg: angleOf(r.dir) });
    for (const [g0, g1] of r.gaps) {
      const width = g1 - g0;
      const crossing = runs.some((q) => q !== r && q.dir !== r.dir && (() => {
        const mid = toXY(r.dir, r.c, (g0 + g1) / 2), dq = dirs[q.dir];
        const off = Math.abs(dot(mid, dq.n) - q.c), sq = dot(mid, dq.u);
        return off <= width / 2 + 0.02 && sq >= q.s0 - r.t && sq <= q.s1 + r.t;
      })());
      if (crossing || width < o.minOpening) continue;
      const center = toXY(r.dir, r.c, (g0 + g1) / 2);
      const near = (q: Pt) => Math.hypot(q[0] - center[0], q[1] - center[1]) <= width / 2 + r.t + 0.1;
      const evidence: string[] = [];
      const swing = arcs.find((a) => near(a.c!) && Math.abs(a.r! - width) <= 0.15);
      if (swing) evidence.push(`arc r=${r4(swing.r!)} on ${swing.l}`);
      const blocks = [...new Set(blockCurves.filter((cv) => cv.p.some(near)).map((cv) => cv.b!))];
      if (blocks.length) evidence.push(`blocks: ${blocks.slice(0, 5).join(", ")}`);
      const doorWord = blocks.some((b) => /porta|door|pta/i.test(b));
      openings.push({ wallId: id, center: [r4(center[0]), r4(center[1])], width: r4(width),
        kind: swing || doorWord ? "door" : "opening", evidence });
    }
  });

  const hist = new Map<number, { count: number; totalLength: number }>();
  for (const w of walls) {
    const h = hist.get(w.thickness) ?? { count: 0, totalLength: 0 };
    h.count++; h.totalLength += w.length; hist.set(w.thickness, h);
  }
  const unpaired: { p: [Pt, Pt]; length: number }[] = [];
  dirs.forEach((d, k) => d.lines.forEach((l) => l.ivs.forEach(([s0, s1]) => {
    if (s1 - s0 >= 0.3) {
      const a = toXY(k, l.c, s0), b = toXY(k, l.c, s1);
      unpaired.push({ p: [[r4(a[0]), r4(a[1])], [r4(b[0]), r4(b[1])]], length: r4(s1 - s0) });
    }
  })));
  unpaired.sort((a, b) => b.length - a.length);

  return {
    walls, openings, shortPieces, excluded,
    thicknesses: [...hist.entries()].sort((a, b) => b[1].totalLength - a[1].totalLength)
      .map(([thickness, h]) => ({ thickness, count: h.count, totalLength: r4(h.totalLength) })),
    unpaired: unpaired.slice(0, o.unpairedLimit),
    stats: { curves: curves.length, segments: segs.length, directions: dirs.length, mergedLines: lineCount,
      pieces: pieces.length, walls: walls.length, openings: openings.length, shortPieces: shortPieces.length, excluded,
      unpairedOver30cm: unpaired.length },
  };
}

export interface Box { minX: number; minY: number; maxX: number; maxY: number }
const inBox = (p: Pt, b: Box) => p[0] >= b.minX && p[0] <= b.maxX && p[1] >= b.minY && p[1] <= b.maxY;
const sub = (a: Pt, b: Pt): Pt => [a[0] - b[0], a[1] - b[1]];

/**
 * Partitions drawn as a single line (no second face): the unpaired lines of a wall plan that lie
 * inside the given regions, are long enough and do not run along an already planned wall
 * (a face remnant). The line becomes the centerline; thickness comes from the caller.
 */
export function planSingleLineWalls(plan: WallPlan, options: { regions: Box[]; minLength?: number; clearance?: number; idPrefix?: string; mergeOffset?: number }) {
  const minLength = options.minLength ?? 1.0, clearance = options.clearance ?? 0.2, prefix = options.idPrefix ?? "S";
  const mergeOffset = options.mergeOffset ?? 0.06;
  type Seg = { a: Pt; b: Pt; half: number };   // half = band half-width that blocks this line
  // Parts of line a-b not covered by parallel segments whose band contains it. Covered parts are a
  // planned wall's face remnant, a collinear overlap with a planned wall, or the twin line of a
  // board drawn with two lines. Overlaps would make Revit refuse the commit (walls overlap).
  const freeParts = (a: Pt, b: Pt, blockers: Seg[]): [Pt, Pt][] => {
    const v = sub(b, a), L = Math.hypot(v[0], v[1]), du: Pt = [v[0] / L, v[1] / L], n: Pt = [-du[1], du[0]];
    let ivs: Iv[] = [[0, L]];
    for (const k of blockers) {
      const w = sub(k.b, k.a), lw = Math.hypot(w[0], w[1]); if (lw < 1e-9) continue;
      if (Math.abs(du[0] * w[1] - du[1] * w[0]) / lw > 0.05) continue;
      const off = (q: Pt) => Math.abs(dot(sub(q, a), n));
      if (Math.max(off(k.a), off(k.b)) > k.half) continue;
      const s0 = dot(sub(k.a, a), du), s1 = dot(sub(k.b, a), du);
      ivs = subtract(ivs, [Math.min(s0, s1), Math.max(s0, s1)]);
    }
    return ivs.filter(([s0, s1]) => s1 - s0 >= minLength)
      .map(([s0, s1]) => [[r4(a[0] + du[0] * s0), r4(a[1] + du[1] * s0)], [r4(a[0] + du[0] * s1), r4(a[1] + du[1] * s1)]]);
  };
  const walls: Seg[] = plan.walls.map((w) => ({ a: w.start, b: w.end, half: w.thickness / 2 + clearance }));
  const kept: Seg[] = [];
  const out: { id: string; start: Pt; end: Pt; length: number }[] = [];
  for (const u of plan.unpaired.filter((x) => x.length >= minLength && options.regions.some((r) => inBox(x.p[0], r) && inBox(x.p[1], r)))) {
    for (const [a, b] of freeParts(u.p[0], u.p[1], [...walls, ...kept])) {
      kept.push({ a, b, half: mergeOffset });
      out.push({ id: `${prefix}${out.length + 1}`, start: a, end: b, length: r4(Math.hypot(b[0] - a[0], b[1] - a[1])) });
    }
  }
  return out;
}

export interface HostWall { id: string; start: Pt; end: Pt; thickness: number }
export interface PlannedDoor { hostId: string; center: Pt; width: number; leaf: number; hinge: Pt; openSide: Pt; layer: string }

/**
 * Doors from swing arcs: a quarter arc whose center (hinge) sits in a wall band and one of whose
 * ends lies on the wall axis (closed leaf). Opening = hinge..closed end along the wall.
 * openSide = unit normal pointing where the leaf swings (the open end).
 */
export function planDoorsFromCad(curves: CadCurve[], walls: HostWall[], options: { minRadius?: number; maxRadius?: number; sweepTolDeg?: number; bandTol?: number } = {}) {
  const minR = options.minRadius ?? 0.5, maxR = options.maxRadius ?? 1.3, tol = options.sweepTolDeg ?? 10, bandTol = options.bandTol ?? 0.12;
  const doors: PlannedDoor[] = [];
  const unmatched: { center: Pt; radius: number; layer: string }[] = [];
  for (const a of curves) {
    if (a.t !== "arc" || !a.c || a.r === undefined || a.r < minR || a.r > maxR || a.p.length < 3) continue;
    const ang = (q: Pt) => Math.atan2(q[1] - a.c![1], q[0] - a.c![0]);
    let sweep = Math.abs(ang(a.p[2]) - ang(a.p[0])) * 180 / Math.PI; if (sweep > 180) sweep = 360 - sweep;
    if (Math.abs(sweep - 90) > tol) continue;
    let best: { d: PlannedDoor; score: number } | null = null;
    for (const w of walls) {
      const u = sub(w.end, w.start), L = Math.hypot(u[0], u[1]); if (L < 1e-9) continue;
      const du: Pt = [u[0] / L, u[1] / L], n: Pt = [-du[1], du[0]];
      const band = w.thickness / 2 + bandTol;
      const offC = dot(sub(a.c, w.start), n);
      if (Math.abs(offC) > band) continue;
      for (const [closed, open] of [[a.p[0], a.p[2]], [a.p[2], a.p[0]]] as [Pt, Pt][]) {
        const offClosed = dot(sub(closed, w.start), n);
        if (Math.abs(offClosed) > band) continue;
        const sH = dot(sub(a.c, w.start), du), sC = dot(sub(closed, w.start), du);
        const lo = Math.min(sH, sC), hi = Math.max(sH, sC);
        if (lo < -0.05 || hi > L + 0.05) continue;
        const mid = (lo + hi) / 2;
        const side = Math.sign(dot(sub(open, w.start), n) - offC) || 1;
        const score = Math.abs(offC) + Math.abs(offClosed);
        const d: PlannedDoor = { hostId: w.id, width: r4(hi - lo), leaf: r4(a.r), layer: a.l,
          center: [r4(w.start[0] + du[0] * mid), r4(w.start[1] + du[1] * mid)],
          hinge: [r4(w.start[0] + du[0] * sH), r4(w.start[1] + du[1] * sH)],
          openSide: [r4(n[0] * side), r4(n[1] * side)] };
        if (!best || score < best.score) best = { d, score };
      }
    }
    if (best) doors.push(best.d); else unmatched.push({ center: a.c, radius: a.r, layer: a.l });
  }
  return { doors, unmatched };
}

export interface AxisWall { id: string; axis: "x" | "y"; offset: number; s0: number; s1: number; thickness: number }

/**
 * Straighten nearly collinear orthogonal walls: walls on the same axis whose centerlines differ by at
 * most `tolerance` and whose extents touch (gap <= maxGap) are one line. The line takes the offset of
 * its longest wall; the others get a perpendicular move. DWG lines 1-3 cm apart left visible steps.
 */
export function planWallAlignment(walls: AxisWall[], options: { tolerance?: number; maxGap?: number } = {}) {
  const tol = options.tolerance ?? 0.04, maxGap = options.maxGap ?? 0.3;
  const moves: { id: string; axis: "x" | "y"; from: number; to: number; delta: number; lineOf: string }[] = [];
  for (const axis of ["x", "y"] as const) {
    const list = walls.filter((w) => w.axis === axis).sort((a, b) => a.offset - b.offset);
    const groups: AxisWall[][] = [];
    for (const w of list) {
      const g = groups.find((gr) => gr.some((o) => Math.abs(o.offset - w.offset) <= tol
        && Math.max(o.s0, w.s0) - Math.min(o.s1, w.s1) <= maxGap));
      if (g) g.push(w); else groups.push([w]);
    }
    for (const g of groups) {
      if (g.length < 2) continue;
      const lead = [...g].sort((a, b) => (b.s1 - b.s0) - (a.s1 - a.s0))[0];
      for (const w of g) {
        const delta = lead.offset - w.offset;
        if (w !== lead && Math.abs(delta) > 1e-4)
          moves.push({ id: w.id, axis, from: r4(w.offset), to: r4(lead.offset), delta: r4(delta), lineOf: lead.id });
      }
    }
  }
  return moves;
}

/**
 * Sliding doors from leaf glass strips (thin closed polylines, e.g. layer A-DOOR-GLAZ): strips on the
 * same line that overlap or nearly touch are one opening (a two-leaf door draws two offset strips).
 * Opening = union of the strips plus `frame` at both ends; host = parallel wall whose band contains it.
 */
export function planSlidingDoorsFromCad(curves: CadCurve[], walls: HostWall[], options: { minLeaf?: number; maxLeaf?: number; lineTol?: number; gap?: number; frame?: number; bandTol?: number } = {}) {
  const minLeaf = options.minLeaf ?? 0.4, maxLeaf = options.maxLeaf ?? 2.5, lineTol = options.lineTol ?? 0.08,
    gapTol = options.gap ?? 0.2, frame = options.frame ?? 0.05, bandTol = options.bandTol ?? 0.12;
  type Strip = { a: Pt; b: Pt; u: Pt; n: Pt; c: number; s0: number; s1: number };
  const strips: Strip[] = [];
  for (const cv of curves) {
    if (cv.t !== "poly" || cv.p.length < 4) continue;
    const xs = cv.p.map((q) => q[0]), ys = cv.p.map((q) => q[1]);
    // oriented extent from the longest edge
    let best: [Pt, Pt] | null = null, bl = 0;
    for (let i = 1; i < cv.p.length; i++) { const L = Math.hypot(cv.p[i][0] - cv.p[i - 1][0], cv.p[i][1] - cv.p[i - 1][1]); if (L > bl) { bl = L; best = [cv.p[i - 1], cv.p[i]]; } }
    if (!best || bl < minLeaf || bl > maxLeaf) continue;
    const u: Pt = [(best[1][0] - best[0][0]) / bl, (best[1][1] - best[0][1]) / bl];
    const n: Pt = [-u[1], u[0]];
    const width = Math.max(...cv.p.map((q) => dot(q, n))) - Math.min(...cv.p.map((q) => dot(q, n)));
    if (width > 0.03) continue; // a glass strip, not a panel outline
    const cx = xs.reduce((s, v) => s + v, 0) / xs.length, cy = ys.reduce((s, v) => s + v, 0) / ys.length;
    const ss = cv.p.map((q) => dot(q, u));
    strips.push({ a: best[0], b: best[1], u, n, c: dot([cx, cy], n), s0: Math.min(...ss), s1: Math.max(...ss) });
  }
  const groups: Strip[][] = [];
  for (const st of strips) {
    const g = groups.find((gr) => gr.some((o) => Math.abs(o.u[0] * st.u[1] - o.u[1] * st.u[0]) < 0.02
      && Math.abs(dot([st.u[0] * st.s0 + st.n[0] * st.c, st.u[1] * st.s0 + st.n[1] * st.c], o.n) - o.c) <= lineTol
      && Math.max(o.s0, dot([st.u[0] * st.s0 + st.n[0] * st.c, st.u[1] * st.s0 + st.n[1] * st.c], o.u)) - Math.min(o.s1, dot([st.u[0] * st.s1 + st.n[0] * st.c, st.u[1] * st.s1 + st.n[1] * st.c], o.u)) <= gapTol));
    if (g) g.push(st); else groups.push([st]);
  }
  const doors: { hostId: string | null; center: Pt; width: number; leaves: number; angleDeg: number }[] = [];
  for (const g of groups) {
    const { u, n } = g[0];
    const s0 = Math.min(...g.map((x) => dot([x.u[0] * x.s0 + x.n[0] * x.c, x.u[1] * x.s0 + x.n[1] * x.c], u))) - frame;
    const s1 = Math.max(...g.map((x) => dot([x.u[0] * x.s1 + x.n[0] * x.c, x.u[1] * x.s1 + x.n[1] * x.c], u))) + frame;
    const c = g.reduce((s, x) => s + dot([x.u[0] * x.s0 + x.n[0] * x.c, x.u[1] * x.s0 + x.n[1] * x.c], n), 0) / g.length;
    const center: Pt = [u[0] * (s0 + s1) / 2 + n[0] * c, u[1] * (s0 + s1) / 2 + n[1] * c];
    const host = walls.find((w) => {
      const d = sub(w.end, w.start), L = Math.hypot(d[0], d[1]); if (L < 1e-9) return false;
      const du: Pt = [d[0] / L, d[1] / L], wn: Pt = [-du[1], du[0]];
      if (Math.abs(du[0] * u[1] - du[1] * u[0]) > 0.05) return false;
      const t = dot(sub(center, w.start), du);
      return Math.abs(dot(sub(center, w.start), wn)) <= w.thickness / 2 + bandTol && t >= 0 && t <= L;
    });
    doors.push({ hostId: host?.id ?? null, center: [r4(center[0]), r4(center[1])], width: r4(s1 - s0), leaves: g.length,
      angleDeg: r4(Math.atan2(u[1], u[0]) * 180 / Math.PI) });
  }
  return doors;
}

/**
 * Parking stalls from closed rectangles (stall outlines on a parking layer). Outlines drawn twice (outer
 * and inner line) collapse to the outer one per center. `kinds` classifies by length/width within 5 cm.
 * Returns centre, size, long-axis angle and kind; placement math is the caller's (family origin varies).
 */
export function planParkingFromCad(curves: CadCurve[], kinds: { name: string; length: number; width: number }[], options: { minLength?: number; maxLength?: number; tol?: number } = {}) {
  const minL = options.minLength ?? 3.5, maxL = options.maxLength ?? 8, tol = options.tol ?? 0.05;
  const rects: { center: Pt; length: number; width: number; angleDeg: number }[] = [];
  for (const cv of curves) {
    if (cv.t !== "poly") continue;
    let p = cv.p;
    if (p.length >= 5 && Math.hypot(p[0][0] - p[p.length - 1][0], p[0][1] - p[p.length - 1][1]) < 0.01) p = p.slice(0, -1);
    if (p.length !== 4) continue;
    const e = [0, 1, 2, 3].map((i) => Math.hypot(p[(i + 1) % 4][0] - p[i][0], p[(i + 1) % 4][1] - p[i][1]));
    if (Math.abs(e[0] - e[2]) > 0.03 || Math.abs(e[1] - e[3]) > 0.03) continue;
    const i = e[0] >= e[1] ? 0 : 1, L = e[i], W = e[1 - i];
    if (L < minL || L > maxL) continue;
    const center: Pt = [p.reduce((s, q) => s + q[0], 0) / 4, p.reduce((s, q) => s + q[1], 0) / 4];
    rects.push({ center, length: L, width: W, angleDeg: (Math.atan2(p[i + 1][1] - p[i][1], p[i + 1][0] - p[i][0]) * 180) / Math.PI });
  }
  const stalls: typeof rects = [];
  for (const r of rects) {
    const same = stalls.find((s) => Math.hypot(s.center[0] - r.center[0], s.center[1] - r.center[1]) < 0.1);
    if (!same) stalls.push({ ...r }); else if (r.length > same.length) Object.assign(same, r);
  }
  return stalls.map((s) => {
    const kind = kinds.find((k) => Math.abs(k.length - s.length) <= tol && Math.abs(k.width - s.width) <= tol);
    return { center: [r4(s.center[0]), r4(s.center[1])] as Pt, length: r4(s.length), width: r4(s.width), angleDeg: r4(s.angleDeg), kind: kind?.name ?? null };
  });
}
