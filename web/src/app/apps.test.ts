import { describe, expect, it } from "vitest";
import { APPS } from "./apps";

// ADR 0006: a customer installs only the modules they need, so a screen of a business module must only show when that module is
// installed. Core screens (and Core data such as items, used by several modules) show everywhere.
describe("app registry", () => {
  it("ties every business module's screen to that module", () => {
    const loose = APPS.filter((a) => !a.permission.startsWith("core.")).filter((a) => a.module !== a.permission.split(".")[0]);
    expect(loose.map((a) => a.id)).toEqual([]);
  });

  it("gives every screen its own id and path", () => {
    expect(new Set(APPS.map((a) => a.id)).size).toBe(APPS.length);
    expect(new Set(APPS.map((a) => a.path)).size).toBe(APPS.length);
  });
});
