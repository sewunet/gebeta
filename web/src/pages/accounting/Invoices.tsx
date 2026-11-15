import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type AccountDto, type InvoiceDto, type PaymentDto } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";
import { useParties } from "./Parties";
import { useBankAccounts } from "./Banking";

type Kind = "Sales" | "Purchase";
type Line = { description: string; quantity: string; rate: string; accountCode: string; isService: boolean; vatApplicable: boolean };
const emptyLine = (kind: Kind): Line => ({ description: "", quantity: "1", rate: "", accountCode: kind === "Sales" ? "4100" : "", isService: false, vatApplicable: true });

/** Sales and purchase invoices with Ethiopian VAT and withholding; submit, cancel and record payments. */
export function Invoices() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<Kind>("Sales");
  const [month, setMonth] = useState(currentMonth());
  const [open, setOpen] = useState<string | null>(null);
  const [paying, setPaying] = useState<string | null>(null);
  const [returning, setReturning] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [lines, setLines] = useState<Line[]>([emptyLine("Sales")]);
  const { start, end } = monthRange(month);
  const parties = useParties();
  const bankAccounts = useBankAccounts();
  const invoices = useQuery({
    queryKey: ["invoices", company?.id, kind, month],
    queryFn: () => api<InvoiceDto[]>(`/api/accounting/invoices?companyId=${company!.id}&kind=${kind}&from=${start}&to=${end}`),
    enabled: !!company,
  });
  const accounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company,
  });
  const refresh = () => {
    for (const key of ["invoices", "journals", "trial-balance", "tax-reports"]) void queryClient.invalidateQueries({ queryKey: [key] });
  };
  const create = useMutation({
    mutationFn: (body: { partyId: string; postingDate: string; dueDate: string; supplierInvoiceNumber: string; customerWithholds: boolean }) =>
      post<InvoiceDto>("/api/accounting/invoices", {
        kind, companyId: company!.id, ...body, dueDate: body.dueDate || null, supplierInvoiceNumber: body.supplierInvoiceNumber || null,
        lines: lines.filter((l) => l.description && l.accountCode).map((l) => ({ ...l, quantity: Number(l.quantity), rate: Number(l.rate || 0) })),
      }),
    onSuccess: (inv) => { setShowForm(false); setLines([emptyLine(kind)]); setOpen(inv.id); refresh(); },
  });
  const act = useMutation({ mutationFn: ({ id, action }: { id: string; action: "submit" | "cancel" }) => post<InvoiceDto>(`/api/accounting/invoices/${id}/${action}`), onSuccess: refresh });
  const payments = useQuery({
    queryKey: ["invoices", "payments", open],
    queryFn: () => api<PaymentDto[]>(`/api/accounting/invoices/${open}/payments`),
    enabled: !!open,
  });
  const cancelPayment = useMutation({ mutationFn: (id: string) => post<PaymentDto>(`/api/accounting/payments/${id}/cancel`), onSuccess: refresh });
  const makeReturn = useMutation({
    mutationFn: ({ inv, quantities, postingDate }: { inv: InvoiceDto; quantities: number[]; postingDate: string }) =>
      post<InvoiceDto>(`/api/accounting/invoices/${inv.id}/return`, {
        postingDate,
        lines: inv.lines.map((l, i) => ({ ...l, quantity: quantities[i] ?? 0 })).filter((l) => l.quantity > 0)
          .map(({ description, quantity, rate, accountCode, isService, vatApplicable }) => ({ description, quantity, rate, accountCode, isService, vatApplicable })),
      }),
    onSuccess: (note) => { setReturning(null); setOpen(note.id); refresh(); },
  });
  const pay = useMutation({
    mutationFn: (body: { invoiceId: string; amount: number; postingDate: string; reference: string; bankAccountCode: string | null }) => post<PaymentDto>("/api/accounting/payments", body),
    onSuccess: () => { setPaying(null); refresh(); },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";
  const sales = kind === "Sales";
  const partyList = parties.data?.filter((p) => p.type === "Both" || p.type === (sales ? "Customer" : "Supplier")) ?? [];
  const postable = accounts.data?.filter((a) => !a.isGroup && (sales ? a.rootType === "Income" : ["Expense", "Asset"].includes(a.rootType))) ?? [];
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const net = lines.reduce((a, l) => a + Number(l.quantity || 0) * Number(l.rate || 0), 0);
  const switchKind = (k: Kind) => { setKind(k); setLines([emptyLine(k)]); setShowForm(false); };
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    create.mutate({
      partyId: String(f.get("partyId")), postingDate: String(f.get("postingDate")), dueDate: String(f.get("dueDate") ?? ""),
      supplierInvoiceNumber: String(f.get("supplierInvoiceNumber") ?? ""), customerWithholds: f.get("customerWithholds") === "on",
    });
  };
  const onPay = (inv: InvoiceDto) => (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    pay.mutate({ invoiceId: inv.id, amount: Number(f.get("amount")), postingDate: String(f.get("postingDate")), reference: String(f.get("reference") ?? ""),
      bankAccountCode: String(f.get("bankAccountCode") ?? "") || null });
  };
  const error = act.error ?? pay.error ?? cancelPayment.error ?? makeReturn.error;
  const onReturn = (inv: InvoiceDto) => (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    makeReturn.mutate({ inv, postingDate: String(f.get("postingDate")), quantities: inv.lines.map((_, i) => Number(f.get(`q${i}`) || 0)) });
  };

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.invoices")}</h1>
        <div className="inline-fields">
          <div className="segmented" role="group">
            {(["Sales", "Purchase"] as const).map((k) => <button key={k} className={kind === k ? "on" : ""} onClick={() => switchKind(k)}>{t(`inv.kind.${k}`)}</button>)}
          </div>
          <label className="field-inline"><span>{t("common.month")}</span><input type="month" value={month} onChange={(e) => setMonth(e.target.value)} /></label>
          {can(me, "accounting.invoices.create") && <button onClick={() => setShowForm((s) => !s)}>{t(sales ? "inv.newSales" : "inv.newPurchase")}</button>}
        </div>
      </div>
      {showForm && (
        <form className="form-grid" onSubmit={onSubmit}>
          <label>{t(sales ? "inv.customer" : "inv.supplier")}
            <select name="partyId" required defaultValue="">
              <option value="" disabled>—</option>
              {partyList.map((p) => <option key={p.id} value={p.id}>{p.name}{p.tin ? ` · ${p.tin}` : ""}</option>)}
            </select>
          </label>
          <label>{t("attendance.date")}<input type="date" name="postingDate" defaultValue={todayIso()} required /></label>
          <label>{t("inv.dueDate")}<input type="date" name="dueDate" /></label>
          {sales
            ? <label className="check"><input type="checkbox" name="customerWithholds" /> {t("inv.customerWithholds")}</label>
            : <label>{t("inv.supplierInvoiceNumber")}<input name="supplierInvoiceNumber" /></label>}
          <div className="lines">
            {lines.map((l, i) => (
              <div className="line invoice-line" key={i}>
                <input placeholder={t("cash.description")} value={l.description} onChange={(e) => setLine(i, { description: e.target.value })} />
                <input placeholder={t("inv.qty")} type="number" step="0.001" min="0" value={l.quantity} onChange={(e) => setLine(i, { quantity: e.target.value })} />
                <input placeholder={t("inv.rate")} type="number" step="0.01" min="0" value={l.rate} onChange={(e) => setLine(i, { rate: e.target.value })} />
                <select value={l.accountCode} onChange={(e) => setLine(i, { accountCode: e.target.value })}>
                  <option value="">—</option>
                  {postable.map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
                </select>
                <label className="check"><input type="checkbox" checked={l.isService} onChange={(e) => setLine(i, { isService: e.target.checked })} /> {t("inv.service")}</label>
                <label className="check"><input type="checkbox" checked={l.vatApplicable} onChange={(e) => setLine(i, { vatApplicable: e.target.checked })} /> {t("inv.vat")}</label>
              </div>
            ))}
            <div className="line-foot">
              <button type="button" className="secondary" onClick={() => setLines((ls) => [...ls, emptyLine(kind)])}>{t("acc.addLine")}</button>
              <span className="muted">{t("inv.net")} {formatMoney(net)} · {t("inv.taxesOnSave")}</span>
            </div>
          </div>
          <div className="form-actions">
            <button type="submit" disabled={create.isPending || net <= 0}>{t("inv.saveDraft")}</button>
            {create.isError && <span className="error">{create.error.message}</span>}
          </div>
        </form>
      )}
      {error && <div className="error">{error.message}</div>}
      {invoices.data?.length ? (
        <table className="table">
          <thead><tr>
            <th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t(sales ? "inv.customer" : "inv.supplier")}</th>
            <th className="num">{t("inv.net")}</th><th className="num">{t("inv.vat")}</th><th className="num">{t("inv.withholding")}</th>
            <th className="num">{t("inv.payable")}</th><th className="num">{t("inv.outstanding")}</th><th>{t("fields.status")}</th><th />
          </tr></thead>
          <tbody>
            {invoices.data.flatMap((inv) => [
              <tr key={inv.id} onClick={() => setOpen(open === inv.id ? null : inv.id)} style={{ cursor: "pointer" }}>
                <td className="mono">{inv.number ?? "—"}
                  {inv.isReturn && <span className="chip small" title={inv.returnAgainstNumber}>{t(sales ? "inv.creditNote" : "inv.debitNote")} · {inv.returnAgainstNumber}</span>}
                  {inv.isOpening && <span className="chip small">{t("inv.opening")}</span>}</td>
                <td>{formatDate(inv.postingDate, me.calendar, am)}</td><td>{inv.partyName}</td>
                <td className="num">{formatMoney(inv.netTotal)}</td><td className="num">{formatMoney(inv.vatAmount)}</td>
                <td className="num">{inv.withholdingAmount ? formatMoney(inv.withholdingAmount) : ""}</td>
                <td className="num">{formatMoney(inv.payableAmount)}</td>
                <td className="num">{inv.status === "Submitted" ? formatMoney(inv.outstanding) : ""}</td>
                <td><span className={`badge ${inv.status}`}>{t(`status.${inv.status}`)}</span></td>
                <td className="actions" onClick={(e) => e.stopPropagation()}>
                  {inv.withholdingReceiptNumber &&
                    <Link to="/accounting/invoices/$id/receipt" params={{ id: inv.id }} className="button secondary" title={inv.withholdingReceiptNumber}>{t("receipt.short")}</Link>}
                  {inv.status === "Draft" && can(me, "accounting.invoices.submit") &&
                    <button onClick={() => act.mutate({ id: inv.id, action: "submit" })} disabled={act.isPending}>{t("slips.submit")}</button>}
                  {inv.status === "Submitted" && !inv.isReturn && inv.outstanding > 0 && can(me, "accounting.payments.create") &&
                    <button onClick={() => setPaying(paying === inv.id ? null : inv.id)}>{t(sales ? "inv.receive" : "inv.pay")}</button>}
                  {inv.status === "Submitted" && !inv.isReturn && !inv.isOpening && inv.outstanding > 0 && can(me, "accounting.invoices.create") &&
                    <button className="secondary" onClick={() => setReturning(returning === inv.id ? null : inv.id)}>{t(sales ? "inv.creditNote" : "inv.debitNote")}</button>}
                  {inv.status === "Submitted" && (inv.isReturn || inv.outstanding === inv.payableAmount) && can(me, "accounting.invoices.submit") &&
                    <button className="secondary" onClick={() => act.mutate({ id: inv.id, action: "cancel" })} disabled={act.isPending}>{t("common.cancel")}</button>}
                </td>
              </tr>,
              paying === inv.id ? (
                <tr key={`${inv.id}-pay`} className="detail"><td colSpan={10}>
                  <form className="inline-fields" onSubmit={onPay(inv)}>
                    <label className="field-inline"><span>{t("cash.amount")}</span><input name="amount" type="number" step="0.01" min="0.01" max={inv.outstanding} defaultValue={inv.outstanding} required /></label>
                    <label className="field-inline"><span>{t("attendance.date")}</span><input name="postingDate" type="date" defaultValue={todayIso()} required /></label>
                    <label className="field-inline"><span>{t("inv.reference")}</span><input name="reference" /></label>
                    {!!bankAccounts.data?.length && (
                      <label className="field-inline"><span>{t("bank.bank")}</span>
                        <select name="bankAccountCode" defaultValue={bankAccounts.data[0]!.ledgerAccountCode}>
                          {bankAccounts.data.map((b) => <option key={b.id} value={b.ledgerAccountCode}>{b.bankName} · {b.name}</option>)}
                        </select>
                      </label>
                    )}
                    <button type="submit" disabled={pay.isPending}>{t("common.save")}</button>
                  </form>
                </td></tr>
              ) : null,
              returning === inv.id ? (
                <tr key={`${inv.id}-return`} className="detail"><td colSpan={10}>
                  <form onSubmit={onReturn(inv)}>
                    <p className="muted small">{t("inv.returnHint", { amount: formatMoney(inv.outstanding) })}</p>
                    <table className="table inner"><tbody>
                      {inv.lines.map((l, i) => (
                        <tr key={i}><td>{l.description}</td><td className="num">{formatMoney(l.rate)}</td>
                          <td className="num"><input name={`q${i}`} type="number" step="0.001" min="0" max={l.quantity} defaultValue={l.quantity} style={{ width: 100 }} /> / {l.quantity}</td></tr>
                      ))}
                    </tbody></table>
                    <div className="inline-fields">
                      <label className="field-inline"><span>{t("attendance.date")}</span><input name="postingDate" type="date" defaultValue={todayIso()} min={inv.postingDate} required /></label>
                      <button type="submit" disabled={makeReturn.isPending}>{t("inv.saveDraft")}</button>
                    </div>
                  </form>
                </td></tr>
              ) : null,
              open === inv.id ? (
                <tr key={`${inv.id}-lines`} className="detail"><td colSpan={10}>
                  <table className="table inner">
                    <tbody>
                      {inv.lines.map((l, i) => (
                        <tr key={i}><td>{l.description}</td><td className="num">{l.quantity} × {formatMoney(l.rate)}</td><td className="num">{formatMoney(l.amount)}</td>
                          <td className="mono">{l.accountCode}</td><td className="muted">{l.isService ? t("inv.service") : t("inv.goods")}{l.vatApplicable ? "" : ` · ${t("inv.exempt")}`}</td></tr>
                      ))}
                      <tr><td colSpan={2}>{t("inv.grandTotal")}</td><td className="num">{formatMoney(inv.grandTotal)}</td><td colSpan={2} className="muted">
                        {inv.partyTin ? `${t("fields.tin")} ${inv.partyTin}` : ""}{inv.supplierInvoiceNumber ? ` · ${inv.supplierInvoiceNumber}` : ""} · {t("inv.dueDate")} {formatDate(inv.dueDate, me.calendar, am)}
                      </td></tr>
                    </tbody>
                  </table>
                  {!!payments.data?.length && (
                    <table className="table inner">
                      <tbody>{payments.data.map((p) => (
                        <tr key={p.id} className={p.status === "Cancelled" ? "muted" : ""}>
                          <td className="mono">{p.number}</td><td>{formatDate(p.postingDate, me.calendar, am)}</td><td>{p.reference}</td>
                          <td className="num">{formatMoney(p.amount)}</td><td><span className={`badge ${p.status}`}>{t(`status.${p.status}`)}</span></td>
                          <td className="actions">{p.status === "Submitted" && can(me, "accounting.payments.create") &&
                            <button className="secondary" onClick={() => cancelPayment.mutate(p.id)} disabled={cancelPayment.isPending}>{t("inv.cancelPayment")}</button>}</td>
                        </tr>
                      ))}</tbody>
                    </table>
                  )}
                </td></tr>
              ) : null,
            ])}
          </tbody>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </>
  );
}
