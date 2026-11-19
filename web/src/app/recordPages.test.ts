import { describe, expect, it } from "vitest";
import { recordPath } from "./recordPages";

describe("recordPath", () => {
  it("opens a purchase order's page by its id", () => {
    expect(recordPath("PurchaseOrder", "0198-abc")).toBe("/buying/orders/0198-abc");
  });

  it("gives no link for an unknown kind or a missing record", () => {
    expect(recordPath("Mystery", "1")).toBeNull();
    expect(recordPath("PurchaseOrder", null)).toBeNull();
    expect(recordPath(null, "1")).toBeNull();
  });
});
