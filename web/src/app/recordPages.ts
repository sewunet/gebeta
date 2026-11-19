/**
 * Where each kind of record opens, by the kind names the API uses for related documents and for notification links. A kind without a page is shown
 * without a link. A page of a module that is not installed simply never gets a record of that kind.
 */
export const RECORD_PAGE: Record<string, string> = {
  Invoice: "/accounting/invoices/$id",
  Payment: "/accounting/payments/$id/voucher",
  JournalEntry: "/accounting/journals/$id",
  SalarySlip: "/payroll/slips/$id",
  EmployeeLoan: "/payroll/loans/$id",
  AdditionalSalary: "/payroll/additional-salary/$id",
  PurchaseOrder: "/buying/orders/$id",
  BlanketAgreement: "/buying/agreements/$id",
  SalesOrder: "/selling/orders/$id",
  StockEntry: "/stock/entries/$id",
};

/** The path of a record's page, or null when this kind has none. */
export function recordPath(kind: string | null | undefined, id: string | null | undefined): string | null {
  const page = kind ? RECORD_PAGE[kind] : undefined;
  return page && id ? page.replace("$id", encodeURIComponent(id)) : null;
}
