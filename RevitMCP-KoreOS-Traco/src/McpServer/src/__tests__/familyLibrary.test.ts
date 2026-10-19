import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, it, expect } from "vitest";
import { searchFamilyLibrary } from "../familyLibrary.js";

describe("family library", () => {
  it("finds nested RFA files with accent-insensitive terms and paginates", async () => {
    const root = await mkdtemp(join(tmpdir(), "revit-library-"));
    try {
      await mkdir(join(root, "nested"));
      await writeFile(join(root, "Alarme emergência.RFA"), "");
      await writeFile(join(root, "nested", "Alarme emergência PCD.rfa"), "");
      await writeFile(join(root, "Alarme emergência.txt"), "");
      const first = await searchFamilyLibrary("ALARME emergencia", 0, 1, root);
      expect(first.total).toBe(2);
      expect(first.nextOffset).toBe(1);
      const second = await searchFamilyLibrary("alarme emergencia", 1, 1, root);
      expect(second.hasMore).toBe(false);
      expect(second.files[0].path).not.toBe(first.files[0].path);
      expect((await searchFamilyLibrary("inexistente", 0, 10, root)).total).toBe(0);
    } finally { await rm(root, { recursive: true, force: true }); }
  });
  it("reports a missing library instead of pretending it is empty", async () => {
    const root = await mkdtemp(join(tmpdir(), "revit-library-"));
    try { await expect(searchFamilyLibrary("", 0, 10, join(root, "missing"))).rejects.toThrow(); }
    finally { await rm(root, { recursive: true, force: true }); }
  });
});
