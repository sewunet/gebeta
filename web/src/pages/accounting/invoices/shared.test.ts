import { describe, expect, it } from "vitest";
import type { InvoiceDto } from "../../../lib/types";
import { paymentState } from "./shared";

const invoice = (patch: Partial<InvoiceDto>): InvoiceDto => ({
  id: "1", kind: "Sales", companyId: "c", partyId: "p", partyName: "Customer", postingDate: "2026-11-01", dueDate: "2026-11-30", status: "Submitted",
  lines: [], netTotal: 1000, vatAmount: 0, grandTotal: 1000, withholdingAmount: 0, payableAmount: 1000, outstanding: 1000, isReturn: false, isOpening: false,
  ...patch,
});

describe("invoice payment state", () => {
  const today = "2026-12-05";
  it("shows drafts, paid, part-paid, due and overdue invoices", () => {
    expect(paymentState(invoice({ status: "Draft" }), today)).toEqual({ key: "status.Draft", tone: "critical" });
    expect(paymentState(invoice({ outstanding: 0 }), today)).toEqual({ key: "inv.paid", tone: "positive" });
    expect(paymentState(invoice({ dueDate: "2026-12-31", outstanding: 400 }), today)).toEqual({ key: "inv.partlyPaid", tone: "information" });
    expect(paymentState(invoice({ dueDate: "2026-12-31" }), today)).toEqual({ key: "inv.due", tone: "information" });
    expect(paymentState(invoice({}), today)).toEqual({ key: "inv.overdue", tone: "negative" });
    expect(paymentState(invoice({ isReturn: true, outstanding: 0 }), today).tone).toBe("positive");
    expect(paymentState(invoice({ status: "Cancelled" }), today).tone).toBe("negative");
  });
});
