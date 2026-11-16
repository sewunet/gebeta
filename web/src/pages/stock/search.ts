/** Search-parameter checks for stock routes (kept apart from the pages so routing does not load them). */
export type Purpose = "Receipt" | "Issue" | "Transfer";

export const validatePurpose = (s: Record<string, unknown>): { purpose: Purpose; po?: string; so?: string } =>
  ({ purpose: s.purpose === "Issue" || s.purpose === "Transfer" ? s.purpose : "Receipt", po: typeof s.po === "string" ? s.po : undefined,
    so: typeof s.so === "string" ? s.so : undefined });

export const validateItem = (s: Record<string, unknown>): { item?: string } => ({ item: typeof s.item === "string" ? s.item : undefined });
