import { readdir } from "node:fs/promises";
import { resolve, join } from "node:path";
import { fileURLToPath } from "node:url";

export const defaultLibraryRoot = process.env.REVIT_MCP_FAMILY_LIBRARY
  ?? fileURLToPath(new URL("../../../familias/", import.meta.url));
const normalize = (s: string) => s.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();

/** Filename discovery only: RFA contents require the Revit API. Symlinks are skipped. */
export async function searchFamilyLibrary(query = "", offset = 0, limit = 50, root = defaultLibraryRoot) {
  const base = resolve(root);
  const terms = normalize(query).split(/\s+/).filter(Boolean);
  const found: { name: string; path: string }[] = [];
  async function walk(directory: string) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isSymbolicLink()) continue;
      if (entry.isDirectory()) await walk(path);
      else if (entry.isFile() && /\.rfa$/i.test(entry.name)
        && terms.every(term => normalize(entry.name).includes(term)))
        found.push({ name: entry.name, path });
    }
  }
  await walk(base);
  found.sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
  const files = found.slice(offset, offset + limit);
  const hasMore = offset + files.length < found.length;
  return { root: base, total: found.length, count: files.length, offset,
    hasMore, nextOffset: hasMore ? offset + files.length : null, files,
    inspection: "filenames_only" };
}
