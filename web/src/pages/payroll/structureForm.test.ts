import { describe, expect, it } from "vitest";
import { emptyRow, formatValues, parseVariables, toComponent } from "./structureForm";

describe("salary structure form", () => {
  it("reads pay value names typed with commas or spaces", () => {
    expect(parseVariables(" ctc, fuel_payment  bonus ,")).toEqual(["ctc", "fuel_payment", "bonus"]);
    expect(parseVariables("")).toEqual([]);
  });

  it("sends a formula or a fixed amount, never both", () => {
    expect(toComponent({ ...emptyRow(), name: " Basic ", abbreviation: "BS ", formula: " base ", amount: "100" }))
      .toMatchObject({ name: "Basic", abbreviation: "BS", formula: "base", amount: null, condition: null });
    expect(toComponent({ ...emptyRow(), name: "Transport", abbreviation: "TA", amount: "600" })).toMatchObject({ formula: null, amount: 600 });
    expect(toComponent({ ...emptyRow(), name: "Placeholder", abbreviation: "OT" })).toMatchObject({ formula: null, amount: null });
  });

  it("shows pay values as name and amount", () => {
    expect(formatValues({ ctc: 12000, rate: 0.07 })).toBe("ctc 12,000 · rate 0.07");
    expect(formatValues(undefined)).toBe("");
  });
});
