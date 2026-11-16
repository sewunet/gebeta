import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type AccountDto, type InvoiceDto, type JournalEntryDto, type PeriodClosingDto, type PeriodClosingsDto } from "../../lib/types";
import { Dialog, MessageStrip, ObjectPage, ObjectStatus } from "../../ui/fiori";
import { useParties } from "../../lib/queries";

type Line = { accountCode: string; debit: string; credit: string };
const emptyLine = (): Line => ({ accountCode: "", debit: "", credit: "" });

/** Month-end close and reopen, year-end closing, and opening balances at cut-over. */
export function Periods() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [notice, setNotice] = useState<string | null>(null);
  const [showOpening, setShowOpening] = useState(false);
  const [dialog, setDialog] = useState<"month" | "reopen" | "year" | null>(null);
  const [lines, setLines] = useState<Line[]>([emptyLine(), emptyLine()]);
  const periods = useQuery({
    queryKey: ["periods", company?.id],
    queryFn: () => api<PeriodClosingsDto>(`/api/accounting/periods?companyId=${company!.id}`),
    enabled: !!company,
  });
  const accounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company && showOpening,
  });
  const refresh = () => {
    for (const key of ["periods", "journals", "trial-balance", "financial"]) void queryClient.invalidateQueries({ queryKey: [key] });
  };
  const closeMonth = useMutation({
    mutationFn: (date: string) => post<PeriodClosingDto>("/api/accounting/periods/close", { companyId: company!.id, date }),
    onSuccess: (c) => { setDialog(null); setNotice(t("periods.closed", { label: c.label })); refresh(); },
  });
  const reopen = useMutation({
    mutationFn: (reason: string) => post<PeriodClosingDto>("/api/accounting/periods/reopen", { companyId: company!.id, reason }),
    onSuccess: (c) => { setDialog(null); setNotice(t("periods.reopened", { label: c.label })); refresh(); },
  });
  const closeYear = useMutation({
    mutationFn: (date: string) => post<JournalEntryDto>("/api/accounting/years/close", { companyId: company!.id, date }),
    onSuccess: (j) => { setDialog(null); setNotice(t("periods.yearClosed", { label: j.sourceNumber, number: j.number })); refresh(); },
  });
  const parties = useParties();
  const openingInvoice = useMutation({
    mutationFn: (body: Record<string, string>) => post<InvoiceDto>("/api/accounting/invoices/opening", {
      kind: body.kind, companyId: company!.id, partyId: body.partyId, postingDate: body.postingDate, dueDate: body.dueDate || null,
      amount: Number(body.amount), reference: body.reference || null,
    }),
    onSuccess: (inv) => { setNotice(t("periods.openingInvoicePosted", { number: inv.number, party: inv.partyName })); void queryClient.invalidateQueries({ queryKey: ["invoices"] }); },
  });
  const opening = useMutation({
    mutationFn: (date: string) => post<JournalEntryDto>("/api/accounting/opening-balances", {
      companyId: company!.id, date,
      lines: lines.filter((l) => l.accountCode).map((l) => ({ accountCode: l.accountCode, debit: Number(l.debit || 0), credit: Number(l.credit || 0) })),
    }),
    onSuccess: (j) => { setNotice(t("periods.openingPosted", { number: j.number })); setShowOpening(false); setLines([emptyLine(), emptyLine()]); refresh(); },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";
  const date = (iso: string) => formatDate(iso, me.calendar, am);
  const manage = can(me, "accounting.periods.close");
  const latest = periods.data?.closings.find((c) => c.status === "Closed");
  const error = opening.error ?? openingInvoice.error; // the close and reopen dialogs show their own errors
  const field = (e: FormEvent<HTMLFormElement>, name: string) => { e.preventDefault(); return String(new FormData(e.currentTarget).get(name)); };
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const debit = lines.reduce((a, l) => a + Number(l.debit || 0), 0);
  const credit = lines.reduce((a, l) => a + Number(l.credit || 0), 0);

  const lastClosed = periods.data?.closings.find((c) => c.status === "Closed");
  const dialogFooter = (form: string, pending: boolean, label: string) => (
    <><button type="submit" form={form} disabled={pending}>{label}</button><button className="secondary" onClick={() => setDialog(null)}>{t("common.cancel")}</button></>
  );

  return (
    <>
      <ObjectPage
        title={t("nav.periods")}
        subtitle={company.name}
        status={periods.data && <ObjectStatus tone={periods.data.lockDate ? "positive" : "information"}>
          {periods.data.lockDate ? t("periods.lockedShort", { date: date(periods.data.lockDate) }) : t("periods.noneClosed")}</ObjectStatus>}
        kpis={[
          { label: t("periods.lockDate"), value: periods.data?.lockDate ? date(periods.data.lockDate) : "—" },
          { label: t("periods.lastClosed"), value: lastClosed?.label ?? "—" },
          { label: t("periods.profit"), value: lastClosed ? formatMoney(lastClosed.netProfit) : "—", tone: lastClosed && lastClosed.netProfit < 0 ? "negative" : undefined },
          { label: t("periods.history"), value: String(periods.data?.closings.filter((c) => c.status === "Closed").length ?? 0) },
        ]}
        actions={manage && <>
          <button onClick={() => setDialog("month")}>{t("periods.closeMonth")}</button>
          {latest && <button className="secondary" onClick={() => setDialog("reopen")}>{t("periods.reopen", { label: latest.label })}</button>}
          <button className="secondary" onClick={() => setDialog("year")}>{t("periods.closeYear")}</button>
        </>}
        messages={<>
          {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
          {error && <MessageStrip tone="negative">{error.message}</MessageStrip>}
          {periods.data?.lockDate && <MessageStrip tone="information">{t("periods.lockedUntil", { date: date(periods.data.lockDate) })}</MessageStrip>}
        </>}
        sections={[
          { id: "history", title: t("periods.history"), content: periods.data?.closings.length ? (
            <table className="table">
              <thead><tr><th>{t("fields.period")}</th><th>{t("periods.from")}</th><th>{t("periods.to")}</th><th className="num">{t("periods.tbTotal")}</th>
                <th className="num">{t("periods.profit")}</th><th>{t("periods.closedBy")}</th><th>{t("fields.status")}</th></tr></thead>
              <tbody>
                {periods.data.closings.map((c) => (
                  <tr key={c.id} className={c.status === "Reopened" ? "muted" : ""}>
                    <td>{c.label}</td><td>{date(c.start)}</td><td>{date(c.end)}</td><td className="num">{formatMoney(c.totalDebit)}</td>
                    <td className={`num ${c.netProfit < 0 ? "neg" : ""}`}>{formatMoney(c.netProfit)}</td>
                    <td>{c.closedBy} · {date(c.closedAt.slice(0, 10))}</td>
                    <td><ObjectStatus tone={c.status === "Closed" ? "positive" : "critical"}>
                      {c.status === "Closed" ? t("periods.statusClosed") : `${t("periods.statusReopened")}${c.reopenReason ? ` · ${c.reopenReason}` : ""}`}</ObjectStatus></td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : <div className="empty">{t("common.empty")}</div> },
          { id: "opening", title: t("periods.opening"), hidden: !manage, content: (
            <>
              <p className="muted small">{t("periods.openingHint")}</p>
              {!showOpening && <button className="secondary" onClick={() => setShowOpening(true)}>{t("periods.enterOpening")}</button>}
              {showOpening && (
                <form onSubmit={(e) => opening.mutate(field(e, "date"))}>
                  <div className="form-columns"><label>{t("attendance.date")}<span className="required" aria-hidden>*</span><input type="date" name="date" required /></label></div>
                  <table className="table edit-table">
                    <thead><tr><th>{t("acc.code")}</th><th className="num">{t("acc.debit")}</th><th className="num">{t("acc.credit")}</th></tr></thead>
                    <tbody>{lines.map((l, i) => (
                      <tr key={i}>
                        <td><select aria-label={t("acc.code")} value={l.accountCode} onChange={(e) => setLine(i, { accountCode: e.target.value })}>
                          <option value="">—</option>
                          {accounts.data?.filter((a) => !a.isGroup).map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
                        </select></td>
                        <td><input aria-label={t("acc.debit")} className="num" type="number" step="0.01" min="0" value={l.debit} onChange={(e) => setLine(i, { debit: e.target.value, credit: "" })} /></td>
                        <td><input aria-label={t("acc.credit")} className="num" type="number" step="0.01" min="0" value={l.credit} onChange={(e) => setLine(i, { credit: e.target.value, debit: "" })} /></td>
                      </tr>
                    ))}</tbody>
                    <tfoot><tr><td><button type="button" className="secondary" onClick={() => setLines((ls) => [...ls, emptyLine()])}>{t("acc.addLine")}</button></td>
                      <td className="num">{formatMoney(debit)}</td><td className="num">{formatMoney(credit)}</td></tr></tfoot>
                  </table>
                  <div className="inline-fields toolbar">
                    <button type="submit" disabled={opening.isPending || debit !== credit || debit === 0}>{t("periods.postOpening")}</button>
                    <button type="button" className="secondary" onClick={() => setShowOpening(false)}>{t("common.cancel")}</button>
                    {debit !== credit && <span className="error">{t("acc.notBalanced", { diff: formatMoney(Math.abs(debit - credit)) })}</span>}
                  </div>
                </form>
              )}
            </>
          ) },
          { id: "opening-invoices", title: t("periods.openingInvoices"), hidden: !manage, content: (
            <>
              <p className="muted small">{t("periods.openingInvoicesHint")}</p>
              <form className="form-columns" onSubmit={(e) => { e.preventDefault(); openingInvoice.mutate(Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>); e.currentTarget.reset(); }}>
                <label>{t("fields.type")}
                  <select name="kind" defaultValue="Sales"><option value="Sales">{t("ageing.receivables")}</option><option value="Purchase">{t("ageing.payables")}</option></select>
                </label>
                <label>{t("inv.customer")} / {t("inv.supplier")}
                  <select name="partyId" required defaultValue=""><option value="" disabled>—</option>{parties.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select>
                </label>
                <label>{t("attendance.date")}<input type="date" name="postingDate" required /></label>
                <label>{t("inv.dueDate")}<input type="date" name="dueDate" /></label>
                <label>{t("inv.outstanding")}<input type="number" name="amount" step="0.01" min="0.01" required /></label>
                <label>{t("inv.oldNumber")}<input name="reference" /></label>
                <div><button type="submit" disabled={openingInvoice.isPending}>{t("periods.addOpeningInvoice")}</button></div>
              </form>
            </>
          ) },
        ]}
      />

      <Dialog open={dialog === "month"} title={t("periods.closeMonth")} onClose={() => setDialog(null)} footer={dialogFooter("close-month", closeMonth.isPending, t("periods.close"))}>
        <form id="close-month" className="dialog-form" onSubmit={(e) => closeMonth.mutate(field(e, "date"))}>
          <p className="muted small">{t("periods.closeMonthHint")}</p>
          <label>{t("attendance.date")}<input type="date" name="date" defaultValue={todayIso()} required /></label>
          {closeMonth.isError && <MessageStrip tone="negative">{closeMonth.error.message}</MessageStrip>}
        </form>
      </Dialog>
      <Dialog open={dialog === "reopen"} title={latest ? t("periods.reopen", { label: latest.label }) : ""} onClose={() => setDialog(null)} footer={dialogFooter("reopen-month", reopen.isPending, t("periods.reopenButton"))}>
        <form id="reopen-month" className="dialog-form" onSubmit={(e) => reopen.mutate(field(e, "reason"))}>
          <p className="muted small">{t("periods.reopenHint")}</p>
          <label>{t("periods.reason")}<input name="reason" required /></label>
          {reopen.isError && <MessageStrip tone="negative">{reopen.error.message}</MessageStrip>}
        </form>
      </Dialog>
      <Dialog open={dialog === "year"} title={t("periods.closeYear")} onClose={() => setDialog(null)} footer={dialogFooter("close-year", closeYear.isPending, t("periods.close"))}>
        <form id="close-year" className="dialog-form" onSubmit={(e) => closeYear.mutate(field(e, "date"))}>
          <p className="muted small">{t("periods.closeYearHint")}</p>
          <label>{t("attendance.date")}<input type="date" name="date" defaultValue={todayIso()} required /></label>
          {closeYear.isError && <MessageStrip tone="negative">{closeYear.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </>
  );
}
