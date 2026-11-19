import { describe, expect, it } from "vitest";
import { conditionKey, parseNames } from "./alertModel";

describe("parseNames", () => {
  it("splits on commas, semicolons and lines, trims, and drops blanks and repeats without regard to case", () => {
    expect(parseNames(" ann, Bob;\nann ,, Cashier ")).toEqual(["ann", "Bob", "Cashier"]);
    expect(parseNames("  ")).toEqual([]);
  });
});

describe("conditionKey", () => {
  it("turns a condition id into a translation key", () => {
    expect(conditionKey("accounting.overdue-sales-invoices")).toBe("accounting_overdue_sales_invoices");
  });
});
