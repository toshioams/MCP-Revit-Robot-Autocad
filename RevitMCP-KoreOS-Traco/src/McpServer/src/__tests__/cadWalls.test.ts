import { describe, it, expect } from "vitest";
import { planWallsFromCad, planSingleLineWalls, planDoorsFromCad, planWallAlignment, planSlidingDoorsFromCad, planParkingFromCad, type CadCurve, type Pt } from "../cadWalls.js";

const line = (a: Pt, b: Pt, l = "PAREDE", b_?: string): CadCurve => ({ t: "line", l, p: [a, b], ...(b_ ? { b: b_ } : {}) });
const poly = (pts: Pt[], l = "PAREDE"): CadCurve => ({ t: "poly", l, p: [...pts, pts[0]] });
const near = (a: Pt, b: Pt, tol = 1e-3) => Math.hypot(a[0] - b[0], a[1] - b[1]) <= tol;
const hasWall = (walls: { start: Pt; end: Pt }[], a: Pt, b: Pt) =>
  walls.some((w) => (near(w.start, a) && near(w.end, b)) || (near(w.start, b) && near(w.end, a)));

describe("planWallsFromCad", () => {
  it("turns a 15 cm room outline into 4 centerline walls meeting at the corners", () => {
    const plan = planWallsFromCad([poly([[0, 0], [4, 0], [4, 3], [0, 3]]), poly([[0.15, 0.15], [3.85, 0.15], [3.85, 2.85], [0.15, 2.85]])]);
    expect(plan.walls).toHaveLength(4);
    expect(plan.walls.every((w) => w.thickness === 0.15)).toBe(true);
    expect(hasWall(plan.walls, [0.075, 0.075], [3.925, 0.075])).toBe(true);
    expect(hasWall(plan.walls, [0.075, 0.075], [0.075, 2.925])).toBe(true);
    expect(hasWall(plan.walls, [3.925, 0.075], [3.925, 2.925])).toBe(true);
    expect(plan.thicknesses).toEqual([{ thickness: 0.15, count: 4, totalLength: expect.any(Number) }]);
    expect(plan.unpaired).toHaveLength(0);
  });

  it("bridges a door gap into one wall and reports the door from its swing arc", () => {
    const curves: CadCurve[] = [
      // south wall with a 0.80 m gap from x=1 to 1.8
      line([0, 0], [1, 0]), line([1.8, 0], [4, 0]), line([0, 0.15], [1, 0.15]), line([1.8, 0.15], [4, 0.15]),
      { t: "arc", l: "PORTA", p: [[1.8, 0.15], [1.23, 0.72], [1, 0.95]], c: [1, 0.15], r: 0.8 },
    ];
    const plan = planWallsFromCad(curves);
    expect(plan.walls).toHaveLength(1);
    expect(plan.walls[0].length).toBeCloseTo(4, 6);
    expect(plan.openings).toHaveLength(1);
    expect(plan.openings[0]).toMatchObject({ wallId: "W1", width: 0.8, kind: "door", center: [1.4, 0.075] });
  });

  it("does not report a T junction as an opening and stops the stem at the through-wall centerline", () => {
    const curves: CadCurve[] = [
      // through wall along y=0..0.15, broken on the north face where the stem (x 2..2.15) arrives
      line([0, 0], [4, 0]), line([0, 0.15], [2, 0.15]), line([2.15, 0.15], [4, 0.15]),
      line([2, 0.15], [2, 3]), line([2.15, 0.15], [2.15, 3]),
    ];
    const plan = planWallsFromCad(curves);
    expect(plan.walls).toHaveLength(2);
    expect(plan.openings).toHaveLength(0);
    expect(hasWall(plan.walls, [0, 0.075], [4, 0.075])).toBe(true);
    expect(hasWall(plan.walls, [2.075, 0.075], [2.075, 3])).toBe(true);
  });

  it("keeps the outer faces when finish lines are drawn inside the wall", () => {
    const curves: CadCurve[] = [line([0, 0], [5, 0]), line([0, 0.025], [5, 0.025]), line([0, 0.125], [5, 0.125]), line([0, 0.15], [5, 0.15])];
    const plan = planWallsFromCad(curves);
    expect(plan.walls).toHaveLength(1);
    expect(plan.walls[0].thickness).toBe(0.15);
  });

  it("snaps drawn thickness, ignores block geometry and splits walls across wide gaps", () => {
    const curves: CadCurve[] = [
      line([0, 0], [3, 0]), line([0, 0.142], [3, 0.142]),
      line([6.5, 0], [9, 0]), line([6.5, 0.142], [9, 0.142]),
      line([0, 1], [3, 1], "MOBILIARIO", "SOFA"), line([0, 1.1], [3, 1.1], "MOBILIARIO", "SOFA"),
    ];
    const plan = planWallsFromCad(curves, { snapThicknesses: [0.15, 0.25] });
    expect(plan.walls).toHaveLength(2);
    expect(plan.walls.every((w) => w.thickness === 0.15 && w.drawnThickness === 0.142)).toBe(true);
  });

  it("handles rotated walls", () => {
    const c = Math.cos(Math.PI / 6), s = Math.sin(Math.PI / 6);
    const rot = (x: number, y: number): Pt => [x * c - y * s, x * s + y * c];
    const plan = planWallsFromCad([line(rot(0, 0), rot(5, 0)), line(rot(0, 0.2), rot(5, 0.2))]);
    expect(plan.walls).toHaveLength(1);
    expect(plan.walls[0].thickness).toBe(0.2);
    expect(Math.abs(plan.walls[0].angleDeg)).toBeCloseTo(30, 3);
  });

  it("reports short runs as pieces and drops walls inside excluded regions", () => {
    const curves: CadCurve[] = [
      line([0, 0], [0.25, 0]), line([0, 0.25], [0.25, 0.25]),            // 25 cm column
      line([2, 0], [6, 0]), line([2, 0.15], [6, 0.15]),                    // wall
      line([10, 0], [14, 0]), line([10, 0.1], [14, 0.1]),                  // parking outline, excluded
    ];
    const plan = planWallsFromCad(curves, { excludeRegions: [{ minX: 9, minY: -1, maxX: 15, maxY: 1 }] });
    expect(plan.walls).toHaveLength(1);
    expect(plan.shortPieces).toEqual([{ center: [0.125, 0.125], length: 0.25, thickness: 0.25, angleDeg: 0 }]);
    expect(plan.excluded).toBe(1);
  });

  it("merges drywall drawn with four lines into one wall", () => {
    // faces 0 and 0.10, inner board lines at 0.03 and 0.07: pairs (0, 0.07) and (0.03, 0.10)
    const curves: CadCurve[] = [line([0, 0], [4, 0]), line([0, 0.03], [4, 0.03]), line([0, 0.07], [4, 0.07]), line([0, 0.1], [4, 0.1])];
    const plan = planWallsFromCad(curves, { snapThicknesses: [0.1] });
    expect(plan.walls).toHaveLength(1);
    expect(plan.walls[0]).toMatchObject({ thickness: 0.1, drawnThickness: 0.1 });
    expect(hasWall(plan.walls, [0, 0.05], [4, 0.05])).toBe(true);
  });

  it("finds single-line partitions inside a region, ignoring face remnants of planned walls", () => {
    const curves: CadCurve[] = [
      line([0, 0], [6, 0]), line([0, 0.15], [6, 0.15]), line([0, 0.3], [2, 0.3]),   // wall + a remnant line hugging it
      line([3, 0.15], [3, 3]),                                                        // single-line partition
      line([20, 0], [20, 3]),                                                         // outside the region
      line([5, 1], [6.5, 1]), line([5, 1.03], [6.5, 1.03]),                           // 3 cm board drawn with two lines
    ];
    const plan = planWallsFromCad(curves, { unpairedLimit: 100 });
    const singles = planSingleLineWalls(plan, { regions: [{ minX: -1, minY: -1, maxX: 10, maxY: 10 }], minLength: 1 });
    expect(singles).toHaveLength(2);
    expect(singles[0]).toMatchObject({ id: "S1", start: [3, 0.15], end: [3, 3] });
    expect(singles[1].length).toBe(1.5);
  });

  it("places a door from a quarter swing arc on its host wall", () => {
    const wall = { id: "W1", start: [0, 0] as Pt, end: [5, 0] as Pt, thickness: 0.15 };
    // hinge at x=2 on the axis, closed leaf reaching x=2.8, opening toward +y
    const arc: CadCurve = { t: "arc", l: "PORTA", c: [2, 0], r: 0.8, p: [[2.8, 0], [2 + 0.8 * Math.SQRT1_2, 0.8 * Math.SQRT1_2], [2, 0.8]] };
    const car: CadCurve = { t: "arc", l: "CARRO", c: [2, 3], r: 0.6, p: [[2.6, 3], [2.59, 3.1], [2.58, 3.2]] };
    const { doors, unmatched } = planDoorsFromCad([arc, car], [wall]);
    expect(doors).toEqual([{ hostId: "W1", center: [2.4, 0], width: 0.8, leaf: 0.8, hinge: [2, 0], openSide: [0, 1], layer: "PORTA" }]);
    expect(unmatched).toHaveLength(0);
  });

  it("trims single lines where they overlap a planned wall or a partially overlapping twin", () => {
    const curves: CadCurve[] = [
      line([0, 0], [4, 0]), line([0, 0.1], [4, 0.1]),        // planned 10 cm wall x 0..4
      line([3.9, 0.07], [8, 0.07]),                          // collinear continuation overlapping 10 cm
      line([10, 1], [14, 1]), line([12, 1.05], [16, 1.05]),  // twins overlapping half way
    ];
    const plan = planWallsFromCad(curves, { unpairedLimit: 100 });
    const singles = planSingleLineWalls(plan, { regions: [{ minX: -1, minY: -1, maxX: 20, maxY: 5 }], minLength: 1 });
    expect(singles.map((x) => [x.start, x.end])).toEqual([
      [[4, 0.07], [8, 0.07]], [[10, 1], [14, 1]], [[14, 1.05], [16, 1.05]],   // longest first
    ]);
  });

  it("aligns touching walls a few centimetres off the longest wall's line and leaves real offsets alone", () => {
    const moves = planWallAlignment([
      { id: "A", axis: "y", offset: 45.3705, s0: 0.55, s1: 3.5, thickness: 0.1 },
      { id: "B", axis: "y", offset: 45.3505, s0: 3.5, s1: 4.73, thickness: 0.1 },
      { id: "C", axis: "y", offset: 45.3705, s0: 4.73, s1: 6.49, thickness: 0.1 },
      { id: "D", axis: "y", offset: 45.60, s0: 0, s1: 6, thickness: 0.1 },     // 23 cm away: a different wall
      { id: "E", axis: "y", offset: 45.36, s0: 20, s1: 25, thickness: 0.1 },   // same line but far away
    ]);
    expect(moves).toEqual([{ id: "B", axis: "y", from: 45.3505, to: 45.3705, delta: 0.02, lineOf: "A" }]);
  });

  it("groups two offset leaf strips into one sliding door on its wall", () => {
    const strip = (x0: number, x1: number, y: number): CadCurve => ({ t: "poly", l: "A-DOOR-GLAZ", p: [[x0, y], [x1, y], [x1, y + 0.005], [x0, y + 0.005], [x0, y]] });
    const doors = planSlidingDoorsFromCad(
      [strip(1.0, 2.18, 0.02), strip(2.23, 3.41, 0.0), strip(8, 9.18, 0.01)],
      [{ id: "W1", start: [0, 0.01], end: [6, 0.01], thickness: 0.1 }]);
    expect(doors).toHaveLength(2);
    expect(doors[0]).toMatchObject({ hostId: "W1", leaves: 2, width: 2.51 });
    expect(doors[0].center[0]).toBeCloseTo(2.205, 3);
    expect(doors[1]).toMatchObject({ hostId: null, leaves: 1 });
  });

  it("collapses double-drawn stall outlines and classifies them by size", () => {
    const rect = (cx: number, cy: number, L: number, W: number): CadCurve => ({ t: "poly", l: "AR-VAG",
      p: [[cx - W / 2, cy - L / 2], [cx - W / 2, cy + L / 2], [cx + W / 2, cy + L / 2], [cx + W / 2, cy - L / 2], [cx - W / 2, cy - L / 2]] });
    const stalls = planParkingFromCad([rect(0, 0, 5.2, 2.7), rect(0, 0, 5.0, 2.5), rect(3, 0, 5.0, 2.5), rect(3, 0, 4.8, 2.3), rect(9, 9, 1, 1)],
      [{ name: "apoio", length: 5.2, width: 2.7 }, { name: "cliente", length: 5.0, width: 2.5 }]);
    expect(stalls.map((s) => [s.center, s.kind, Math.abs(s.angleDeg)])).toEqual([[[0, 0], "apoio", 90], [[3, 0], "cliente", 90]]);
  });
});
