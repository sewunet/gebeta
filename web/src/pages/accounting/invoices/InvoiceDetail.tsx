import { useState, type FormEvent } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../../app/context";
import { api, post } from "../../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../../lib/ethiopian";
import { can, type InvoiceDto, type PaymentDto } from "../../../lib/types";
import { Dialog, Fields, MessageStrip, ObjectPage, ObjectStatus } from "../../../ui/fiori";
import { paymentState, useInvoiceRefresh } from "./shared";
import { useBankAccounts } from "../../../lib/queries";
import { historySection } from "../../../app/history";
import { relatedSection } from "../../../app/related";

/** Object page of one invoice: header with key figures and actions, then General, Lines, Taxes and Payments. */
export function InvoiceDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const navigate = useNavigate();
  const { id } = useParams({ from: "/accounting/invoices/$id" });
  const [dialog, setDialog] = useState<"pay" | "return" | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const refresh = useInvoiceRefresh();
  const bankAccounts = useBankAccounts();
  const today = todayIso();
  const invoice = useQuery({ queryKey: ["invoice", id], queryFn: () => api<InvoiceDto>(`/api/accounting/invoices/${id}`) });
  const payments = useQuery({ queryKey: ["invoice", id, "payments"], queryFn: () => api<PaymentDto[]>(`/api/accounting/invoices/${id}/payments`) });
  const done = (message: string) => { setDialog(null); setNotice(message); refresh(); };
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel") => post<InvoiceDto>(`/api/accounting/invoices/${id}/${action}`),
    onSuccess: (inv) => done(t(inv.status === "Cancelled" ? "inv.cancelled" : "inv.submitted", { number: inv.number })),
  });
  const pay = useMutation({ mutationFn: (body: object) => post<PaymentDto>("/api/accounting/payments", body),
    onSuccess: (p) => done(t("inv.paymentSaved", { number: p.number, amount: formatMoney(p.amount) })) });
  const cancelPayment = useMutation({ mutationFn: (paymentId: string) => post<PaymentDto>(`/api/accounting/payments/${paymentId}/cancel`),
    onSuccess: (p) => done(t("inv.paymentCancelled", { number: p.number })) });
  const makeReturn = useMutation({ mutationFn: (body: object) => post<InvoiceDto>(`/api/accounting/invoices/${id}/return`, body),
    onSuccess: (note) => { refresh(); void navigate({ to: "/accounting/invoices/$id", params: { id: note.id } }); setDialog(null); } });

  if (invoice.isError) return <MessageStrip tone="negative">{invoice.error.message}</MessageStrip>;
  if (!invoice.data) return <p className="muted">{t("common.loading")}</p>;
  const inv = invoice.data;
  const am = i18n.language === "am";
  const sales = inv.kind === "Sales";
  const date = (iso: string) => formatDate(iso, me.calendar, am);
  const state = paymentState(inv, today);
  const kind = inv.isReturn ? t(sales ? "inv.creditNote" : "inv.debitNote") : t(sales ? "inv.salesInvoice" : "inv.purchaseInvoice");
  const payable = inv.status === "Submitted" && !inv.isReturn && inv.outstanding > 0;
  const error = act.error ?? cancelPayment.error;

  const onPay = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    pay.mutate({ invoiceId: inv.id, amount: Number(f.get("amount")), postingDate: f.get("postingDate"), reference: f.get("reference") || null,
      bankAccountCode: f.get("bankAccountCode") || null });
  };
  const onReturn = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    makeReturn.mutate({
      postingDate: f.get("postingDate"),
      lines: inv.lines.map((l, i) => ({ description: l.description, quantity: Number(f.get(`q${i}`) || 0), rate: l.rate, accountCode: l.accountCode,
        isService: l.isService, vatApplicable: l.vatApplicable })).filter((l) => l.quantity > 0),
    });
  };

  return (
    <>
      <ObjectPage
        breadcrumb={<Link to="/accounting/invoices" search={{ kind: inv.kind }}>{t("nav.invoices")}</Link>}
        title={inv.number ?? t("status.Draft")}
        subtitle={<>{kind} · {inv.partyName}</>}
        status={<ObjectStatus tone={state.tone}>{t(state.key)}</ObjectStatus>}
        kpis={[
          { label: t("inv.grandTotal"), value: formatMoney(inv.grandTotal) },
          { label: t("inv.payable"), value: formatMoney(inv.payableAmount) },
          ...(inv.isReturn ? [] : [{ label: t("inv.outstanding"), value: formatMoney(inv.outstanding), tone: state.tone === "negative" ? "negative" as const : undefined }]),
          { label: t("inv.dueDate"), value: date(inv.dueDate) },
        ]}
        actions={<>
          {inv.status === "Draft" && can(me, "accounting.invoices.submit") && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>}
          {payable && can(me, "accounting.payments.create") && <button onClick={() => setDialog("pay")}>{t(sales ? "inv.receive" : "inv.pay")}</button>}
          {payable && !inv.isOpening && can(me, "accounting.invoices.create") && <button className="secondary" onClick={() => setDialog("return")}>{t(sales ? "inv.creditNote" : "inv.debitNote")}</button>}
          {inv.withholdingReceiptNumber && <Link to="/accounting/invoices/$id/receipt" params={{ id: inv.id }} className="button secondary">{t("receipt.short")}</Link>}
          {inv.status === "Submitted" && (inv.isReturn || inv.outstanding === inv.payableAmount) && can(me, "accounting.invoices.submit") &&
            <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("common.cancel")}</button>}
        </>}
        messages={<>
          {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
          {error && <MessageStrip tone="negative">{error.message}</MessageStrip>}
          {inv.status === "Draft" && <MessageStrip tone="critical">{t("inv.draftHint")}</MessageStrip>}
        </>}
        sections={[
          { id: "general", title: t("ui.general"), content: (
            <Fields items={[
              { label: t(sales ? "inv.customer" : "inv.supplier"), value: inv.partyName },
              { label: t("fields.tin"), value: inv.partyTin && <span className="mono">{inv.partyTin}</span> },
              { label: t("attendance.date"), value: date(inv.postingDate) },
              { label: t("inv.dueDate"), value: date(inv.dueDate) },
              { label: t("inv.supplierInvoiceNumber"), value: inv.supplierInvoiceNumber, hidden: sales },
              { label: t(sales ? "sell.order" : "buy.order"), value: inv.orderReference && <span className="mono">{inv.orderReference}</span>, hidden: !inv.orderReference },
              { label: t("inv.returnOf"), value: inv.returnAgainstNumber, hidden: !inv.isReturn },
              { label: t("inv.opening"), value: t("inv.openingHint"), hidden: !inv.isOpening },
              { label: t("receipt.short"), value: inv.withholdingReceiptNumber && <span className="mono">{inv.withholdingReceiptNumber}</span>, hidden: !inv.withholdingReceiptNumber },
            ]} />
          ) },
          { id: "lines", title: t("ui.lines"), content: (
            <table className="table">
              <thead><tr><th>{t("cash.description")}</th><th className="num">{t("inv.qty")}</th><th className="num">{t("inv.rate")}</th><th>{t("acc.code")}</th><th /><th className="num">{t("cash.amount")}</th></tr></thead>
              <tbody>{inv.lines.map((l, i) => (
                <tr key={i}><td>{l.itemCode && <span className="mono muted">{l.itemCode} · </span>}{l.description}</td><td className="num">{l.quantity}</td><td className="num">{formatMoney(l.rate)}</td><td className="mono">{l.accountCode}</td>
                  <td className="muted small">{l.isService ? t("inv.service") : t("inv.goods")}{l.vatApplicable ? "" : ` · ${t("inv.exempt")}`}</td>
                  <td className="num">{formatMoney(l.amount)}</td></tr>
              ))}</tbody>
              <tfoot><tr><td colSpan={5}>{t("inv.net")}</td><td className="num">{formatMoney(inv.netTotal)}</td></tr></tfoot>
            </table>
          ) },
          { id: "taxes", title: t("ui.taxes"), content: (
            <Fields items={[
              { label: t("inv.net"), value: formatMoney(inv.netTotal) },
              { label: t("inv.vat"), value: formatMoney(inv.vatAmount) },
              { label: t("inv.grandTotal"), value: formatMoney(inv.grandTotal) },
              { label: t("inv.withholding"), value: formatMoney(inv.withholdingAmount) },
              { label: t("inv.payable"), value: <strong>{formatMoney(inv.payableAmount)}</strong> },
            ]} />
          ) },
          { id: "payments", title: t("ui.payments"), hidden: inv.isReturn, content: payments.data?.length ? (
            <table className="table">
              <thead><tr><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("inv.reference")}</th><th className="num">{t("cash.amount")}</th><th>{t("fields.status")}</th><th /></tr></thead>
              <tbody>{payments.data.map((p) => {
                const share = p.allocations?.find((a) => a.invoiceId === inv.id)?.amount ?? p.amount;
                return (
                  <tr key={p.id} className={p.status === "Cancelled" ? "muted" : ""}>
                    <td className="mono">{p.number}</td><td>{date(p.postingDate)}</td><td>{p.reference}</td>
                    <td className="num">{formatMoney(share)}{p.allocations && p.allocations.length > 1 && <div className="muted small">{t("inv.ofPayment")} {formatMoney(p.amount)}</div>}</td>
                    <td><ObjectStatus tone={p.status === "Submitted" ? "positive" : "negative"}>{t(`status.${p.status}`)}</ObjectStatus></td>
                    <td className="actions">
                      <Link to="/accounting/payments/$id/voucher" params={{ id: p.id }} className="link-button">{t("voucher.print")}</Link>{" "}
                      {p.status === "Submitted" && can(me, "accounting.payments.create") &&
                      <button className="link-button" onClick={() => cancelPayment.mutate(p.id)} disabled={cancelPayment.isPending}>{t("inv.cancelPayment")}</button>}</td>
                  </tr>
                );
              })}</tbody>
            </table>
          ) : <div className="empty">{t("inv.noPayments")}</div> },
        ...relatedSection(t, id, inv.number),
        ...historySection(t, me, id, "Invoice")
      ]}
      />

      <Dialog open={dialog === "pay"} title={t(sales ? "inv.receive" : "inv.pay")} onClose={() => setDialog(null)}
        footer={<><button type="submit" form="pay-one" disabled={pay.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setDialog(null)}>{t("common.cancel")}</button></>}>
        <form id="pay-one" onSubmit={onPay} className="dialog-form">
          <label>{t("cash.amount")}<input name="amount" type="number" step="0.01" min="0.01" max={inv.outstanding} defaultValue={inv.outstanding} required /></label>
          <label>{t("attendance.date")}<input name="postingDate" type="date" defaultValue={today} required /></label>
          <label>{t("inv.reference")}<input name="reference" placeholder="CHQ / TRF" /></label>
          {!!bankAccounts.data?.length && (
            <label>{t("bank.bank")}
              <select name="bankAccountCode" defaultValue={bankAccounts.data[0]!.ledgerAccountCode}>
                {bankAccounts.data.map((b) => <option key={b.id} value={b.ledgerAccountCode}>{b.bankName} · {b.name}</option>)}
              </select>
            </label>
          )}
          {pay.isError && <MessageStrip tone="negative">{pay.error.message}</MessageStrip>}
        </form>
      </Dialog>

      <Dialog open={dialog === "return"} title={t(sales ? "inv.creditNote" : "inv.debitNote")} onClose={() => setDialog(null)}
        footer={<><button type="submit" form="return-form" disabled={makeReturn.isPending}>{t("inv.saveDraft")}</button><button className="secondary" onClick={() => setDialog(null)}>{t("common.cancel")}</button></>}>
        <form id="return-form" onSubmit={onReturn} className="dialog-form">
          <p className="muted small">{t("inv.returnHint", { amount: formatMoney(inv.outstanding) })}</p>
          <table className="table inner"><tbody>
            {inv.lines.map((l, i) => (
              <tr key={i}><td>{l.description}</td><td className="num">{formatMoney(l.rate)}</td>
                <td className="num"><input name={`q${i}`} type="number" step="0.001" min="0" max={l.quantity} defaultValue={l.quantity} aria-label={l.description} /> / {l.quantity}</td></tr>
            ))}
          </tbody></table>
          <label>{t("attendance.date")}<input name="postingDate" type="date" defaultValue={today} min={inv.postingDate} required /></label>
          {makeReturn.isError && <MessageStrip tone="negative">{makeReturn.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </>
  );
}
