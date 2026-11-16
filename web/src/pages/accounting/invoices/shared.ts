import { useQueryClient } from "@tanstack/react-query";
import type { InvoiceDto } from "../../../lib/types";
import type { Tone } from "../../../ui/fiori";

export type Kind = "Sales" | "Purchase";

export const validateKind = (search: Record<string, unknown>): { kind: Kind; po?: string } =>
  ({ kind: search.kind === "Purchase" ? "Purchase" : "Sales", po: typeof search.po === "string" ? search.po : undefined });

/** Everything an invoice action can change. */
export function useInvoiceRefresh() {
  const queryClient = useQueryClient();
  return () => {
    for (const key of ["invoices", "invoice", "journals", "trial-balance", "tax-reports", "bank-rec"]) void queryClient.invalidateQueries({ queryKey: [key] });
  };
}

export const isOverdue = (inv: InvoiceDto, today: string) => inv.status === "Submitted" && !inv.isReturn && inv.outstanding > 0 && inv.dueDate < today;

/** Paid, overdue or due, as the list and the object page show it. */
export function paymentState(inv: InvoiceDto, today: string): { key: string; tone: Tone } {
  if (inv.status !== "Submitted") return { key: `status.${inv.status}`, tone: inv.status === "Draft" ? "critical" : "negative" };
  if (inv.isReturn) return { key: "status.Submitted", tone: "positive" };
  if (inv.outstanding === 0) return { key: "inv.paid", tone: "positive" };
  if (isOverdue(inv, today)) return { key: "inv.overdue", tone: "negative" };
  return { key: inv.outstanding < inv.payableAmount ? "inv.partlyPaid" : "inv.due", tone: "information" };
}
