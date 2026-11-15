import { describe, expect, it } from "vitest";
import { formatEthiopian, formatMoney, toEthiopian } from "./ethiopian";

// Same reference dates as the C# tests (tests/Gebeta.SharedKernel.Tests/EthiopiaTests.cs).
describe("Ethiopian calendar", () => {
  it.each([
    ["2023-09-12", 2016, 1, 1],
    ["2023-09-11", 2015, 13, 6],
    ["2024-09-11", 2017, 1, 1],
    ["2025-03-02", 2017, 6, 23],
    ["2025-07-08", 2017, 11, 1],
    ["2026-07-07", 2018, 10, 30],
    ["2026-11-14", 2019, 3, 5],
  ])("%s is %i-%i-%i", (iso, year, month, day) => {
    expect(toEthiopian(iso)).toEqual({ year, month, day });
  });

  it("formats in English and Amharic", () => {
    expect(formatEthiopian("2026-11-14")).toBe("Hidar 5, 2019");
    expect(formatEthiopian("2026-11-14", true)).toBe("ኅዳር 5 ቀን 2019");
  });

  it("formats birr with santim", () => {
    expect(formatMoney(13000)).toBe("ETB 13,000.00");
    expect(formatMoney(2016.5)).toBe("ETB 2,016.50");
  });
});
