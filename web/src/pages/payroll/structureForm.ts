// The salary structure form's rules, kept apart from the screen so they can be tested on their own.
import type { SalaryComponentDto } from "../../lib/types";

export const TYPES = ["Earning", "Deduction", "EmployerContribution"] as const;

/** Pay value names as typed ("ctc, fuel_payment"): trimmed, empty ones dropped. */
export const parseVariables = (text: string) => text.split(/[,\s]+/).map((v) => v.trim()).filter(Boolean);

/** A formula or a fixed amount, as the API takes it: a formula wins; an empty amount is no amount. */
export function toComponent(row: Row): SalaryComponentDto {
  const formula = row.formula.trim();
  return {
    name: row.name.trim(), abbreviation: row.abbreviation.trim(), type: row.type,
    formula: formula || null, amount: formula || row.amount === "" ? null : Number(row.amount),
    condition: row.condition.trim() || null, isTaxable: row.isTaxable, dependsOnPaymentDays: row.dependsOnPaymentDays,
  };
}

export type Row = { name: string; abbreviation: string; type: SalaryComponentDto["type"]; formula: string; amount: string; condition: string; isTaxable: boolean; dependsOnPaymentDays: boolean };
export const emptyRow = (): Row => ({ name: "", abbreviation: "", type: "Earning", formula: "", amount: "", condition: "", isTaxable: true, dependsOnPaymentDays: false });

/** "ctc 12,000 · rate 0.07": plain numbers, since a pay value is not always money. */
export const formatValues = (values: Record<string, number> | undefined) =>
  Object.entries(values ?? {}).map(([name, amount]) => `${name} ${amount.toLocaleString("en-US", { maximumFractionDigits: 4 })}`).join(" · ");
