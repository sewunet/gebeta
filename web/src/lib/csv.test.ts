import { describe, expect, it } from "vitest";
import { parseAmount, parseCsv, parseDate } from "./csv";

describe("bank statement CSV", () => {
  it("splits quoted fields and either separator", () => {
    expect(parseCsv('﻿Date,Description,Amount\r\n14/11/2026,"Transfer, ""TRF1""",-2500\r\n\r\n')).toEqual([
      ["Date", "Description", "Amount"],
      ["14/11/2026", 'Transfer, "TRF1"', "-2500"],
    ]);
    expect(parseCsv("a;b;c\n1;2;3")).toEqual([["a", "b", "c"], ["1", "2", "3"]]);
  });

  it("reads the date formats banks print, day first", () => {
    expect(parseDate("2026-11-14")).toBe("2026-11-14");
    expect(parseDate("14/11/2026")).toBe("2026-11-14");
    expect(parseDate("14.11.26")).toBe("2026-11-14");
    expect(parseDate("14 Nov 2026")).toBe("2026-11-14");
    expect(parseDate("31/13/2026")).toBeNull();
  });

  it("reads amounts with thousands separators and brackets", () => {
    expect(parseAmount("1,234.50")).toBe(1234.5);
    expect(parseAmount("(8,000.00)")).toBe(-8000);
    expect(parseAmount("150.00-")).toBe(-150);
    expect(parseAmount("1 234,50")).toBe(1234.5);
    expect(parseAmount("")).toBe(0);
  });
});
