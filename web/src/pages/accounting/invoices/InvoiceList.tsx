import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../../app/context";
import { api, post } from "../../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../../lib/ethiopian";
import { can, type InvoiceDto, type PaymentDto } from "../../../lib/types";
import { Dialog, FilterBar, FilterField, ListReport, MessageStrip, ObjectStatus } from "../../../ui/fiori";
import { currentMonth, monthRange } from "../../payroll/month";
import { isOverdue, paymentState, useInvoiceRefresh, type Kind } from "./shared";
import { useBankAccounts, useParties } from "../../../lib/queries";

/** List report of sales or purchase invoices: filter bar, table toolbar, select several of one party to pay together. */
export function InvoiceList() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const { kind } = useSearch({ from: "/accounting/invoices" });
  const month = monthRange(currentMonth());
  const [from, setFrom] = useState(month.start);
  const [to, setTo] = useState(month.end);
  const [status, setStatus] = useState("");
  const [partyId, setPartyId] = useState("");
  const [search, setSearch] = useState("");
  const [picked, setPicked] = useState<string[]>([]);
  const [paying, setPaying] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const parties = useParties();
  const bankAccounts = useBankAccounts();
  const refresh = useInvoiceRefresh();
  const today = todayIso();
  const sales = kind === "Sales";

  const invoices = useQuery({
    queryKey: ["invoices", company?.id, kind, from, to],
    queryFn: () => api<InvoiceDto[]>(`/api/accounting/invoices?companyId=${company!.id}&kind=${kind}&from=${from}&to=${to}`),
    enabled: !!company && from <= to,
  });
  const rows = useMemo(() => (invoices.data ?? []).filter((i) => {
    if (partyId && i.partyId !== partyId) return false;
    if (status === "overdue") { if (!isOverdue(i, today)) return false; }
    else if (status === "open") { if (!(i.status === "Submitted" && !i.isReturn && i.outstanding > 0)) return false; }
    else if (status && i.status !== status) return false;
    const q = search.trim().toLowerCase();
    return !q || [i.number, i.partyName, i.partyTin, i.supplierInvoiceNumber].some((v) => v?.toLowerCase().includes(q));
  }), [invoices.data, partyId, status, search, today]);

  const payAll = useMutation({
    mutationFn: (body: object) => post<PaymentDto>("/api/accounting/payments", body),
    onSuccess: (p) => { setPicked([]); setPaying(false); setNotice(t("inv.paymentSaved", { number: p.number, amount: formatMoney(p.amount) })); refresh(); },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";
  const setKind = (k: Kind) => { setPicked([]); setPartyId(""); void navigate({ to: "/accounting/invoices", search: { kind: k } }); };
  const payable = (i: InvoiceDto) => i.status === "Submitted" && !i.isReturn && i.outstanding > 0;
  const pickedRows = (invoices.data ?? []).filter((i) => picked.includes(i.id));
  const pickParty = pickedRows[0]?.partyId;
  const partyList = parties.data?.filter((p) => p.type === "Both" || p.type === (sales ? "Customer" : "Supplier")) ?? [];
  const totals = rows.reduce((a, i) => ({ net: a.net + (i.isReturn ? -i.netTotal : i.netTotal), open: a.open + (payable(i) ? i.outstanding : 0) }), { net: 0, open: 0 });

  const onPay = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    payAll.mutate({
      postingDate: String(f.get("postingDate")), reference: String(f.get("reference") ?? "") || null, bankAccountCode: String(f.get("bankAccountCode") ?? "") || null,
      allocations: pickedRows.map((i) => ({ invoiceId: i.id, amount: Number(f.get(`a-${i.id}`) || 0) })).filter((a) => a.amount > 0),
    });
  };

  return (
    <ListReport
      title={t("nav.invoices")}
      actions={
        <div className="segmented" role="tablist">
          {(["Sales", "Purchase"] as const).map((k) => <button key={k} role="tab" aria-selected={kind === k} className={kind === k ? "on" : ""} onClick={() => setKind(k)}>{t(`inv.kind.${k}`)}</button>)}
        </div>
      }
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("inv.searchHint")} /></FilterField>
          <FilterField label={t(sales ? "inv.customer" : "inv.supplier")}>
            <select value={partyId} onChange={(e) => setPartyId(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {partyList.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              <option value="open">{t("inv.open")}</option>
              <option value="overdue">{t("inv.overdue")}</option>
              <option value="Draft">{t("status.Draft")}</option>
              <option value="Submitted">{t("status.Submitted")}</option>
              <option value="Cancelled">{t("status.Cancelled")}</option>
            </select>
          </FilterField>
          <FilterField label={t("periods.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>
          <FilterField label={t("periods.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
        </FilterBar>
      }
      tableTitle={t(sales ? "inv.salesInvoices" : "inv.purchaseInvoices")}
      count={rows.length}
      messages={<>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {invoices.isError && <MessageStrip tone="negative">{invoices.error.message}</MessageStrip>}
      </>}
      toolbar={<>
        {picked.length > 0 && can(me, "accounting.payments.create") &&
          <button onClick={() => setPaying(true)}>{t(sales ? "inv.receiveSelected" : "inv.paySelected")} ({picked.length})</button>}
        {can(me, "accounting.invoices.create") &&
          <button className={picked.length ? "secondary" : ""} onClick={() => void navigate({ to: "/accounting/invoices/new", search: { kind } })}>{t("ui.create")}</button>}
      </>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr>
            <th aria-label={t("inv.select")} />
            <th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t(sales ? "inv.customer" : "inv.supplier")}</th>
            <th className="num">{t("inv.net")}</th><th className="num">{t("inv.vat")}</th><th className="num">{t("inv.payable")}</th>
            <th className="num">{t("inv.outstanding")}</th><th>{t("fields.status")}</th>
          </tr></thead>
          <tbody>
            {rows.map((i) => {
              const state = paymentState(i, today);
              return (
                <tr key={i.id} className="navigable" onClick={() => void navigate({ to: "/accounting/invoices/$id", params: { id: i.id } })}>
                  <td onClick={(e) => e.stopPropagation()}>{payable(i) && can(me, "accounting.payments.create") && (
                    <input type="checkbox" aria-label={t("inv.select")} checked={picked.includes(i.id)} disabled={!!pickParty && pickParty !== i.partyId}
                      onChange={() => setPicked((p) => (p.includes(i.id) ? p.filter((x) => x !== i.id) : [...p, i.id]))} />
                  )}</td>
                  <td data-label={t("fields.number")}>
                    <span className="mono strong-link">{i.number ?? t("status.Draft")}</span>
                    {i.isReturn && <span className="chip small">{t(sales ? "inv.creditNote" : "inv.debitNote")}</span>}
                    {i.isOpening && <span className="chip small">{t("inv.opening")}</span>}
                  </td>
                  <td data-label={t("attendance.date")}>{formatDate(i.postingDate, me.calendar, am)}</td>
                  <td data-label={t(sales ? "inv.customer" : "inv.supplier")}>{i.partyName}{i.partyTin && <div className="muted small mono">{i.partyTin}</div>}</td>
                  <td className="num" data-label={t("inv.net")}>{formatMoney(i.isReturn ? -i.netTotal : i.netTotal)}</td>
                  <td className="num" data-label={t("inv.vat")}>{i.vatAmount ? formatMoney(i.isReturn ? -i.vatAmount : i.vatAmount) : ""}</td>
                  <td className="num" data-label={t("inv.payable")}>{formatMoney(i.payableAmount)}</td>
                  <td className="num" data-label={t("inv.outstanding")}>{payable(i) ? <ObjectStatus tone={state.tone === "negative" ? "negative" : "neutral"}>{formatMoney(i.outstanding)}</ObjectStatus> : ""}</td>
                  <td data-label={t("fields.status")}><ObjectStatus tone={state.tone}>{t(state.key)}</ObjectStatus></td>
                </tr>
              );
            })}
          </tbody>
          <tfoot><tr><td /><td colSpan={3}>{t("common.total")}</td><td className="num">{formatMoney(totals.net)}</td><td /><td /><td className="num">{formatMoney(totals.open)}</td><td /></tr></tfoot>
        </table>
      ) : <div className="empty">{invoices.isPending ? t("common.loading") : t("inv.noneFound")}</div>}

      <Dialog open={paying} title={t(sales ? "inv.receiveSelected" : "inv.paySelected")} onClose={() => setPaying(false)}
        footer={<><button type="submit" form="pay-many" disabled={payAll.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setPaying(false)}>{t("common.cancel")}</button></>}>
        <form id="pay-many" onSubmit={onPay} className="dialog-form">
          <p className="muted small">{pickedRows[0]?.partyName}</p>
          <table className="table inner"><tbody>
            {pickedRows.map((i) => (
              <tr key={i.id}><td className="mono">{i.number}</td><td className="num">{formatMoney(i.outstanding)}</td>
                <td className="num"><input name={`a-${i.id}`} type="number" step="0.01" min="0" max={i.outstanding} defaultValue={i.outstanding} aria-label={i.number} /></td></tr>
            ))}
          </tbody></table>
          <label>{t("attendance.date")}<input name="postingDate" type="date" defaultValue={today} required /></label>
          <label>{t("inv.reference")}<input name="reference" placeholder="CHQ / TRF" /></label>
          {!!bankAccounts.data?.length && (
            <label>{t("bank.bank")}
              <select name="bankAccountCode" defaultValue={bankAccounts.data[0]!.ledgerAccountCode}>
                {bankAccounts.data.map((b) => <option key={b.id} value={b.ledgerAccountCode}>{b.bankName} · {b.name}</option>)}
              </select>
            </label>
          )}
          {payAll.isError && <MessageStrip tone="negative">{payAll.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}
