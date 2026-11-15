import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type AccountDto, type InvoiceDto, type JournalEntryDto, type PeriodClosingDto, type PeriodClosingsDto } from "../../lib/types";
import { useParties } from "./Parties";

type Line = { accountCode: string; debit: string; credit: string };
const emptyLine = (): Line => ({ accountCode: "", debit: "", credit: "" });

/** Month-end close and reopen, year-end closing, and opening balances at cut-over. */
export function Periods() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [notice, setNotice] = useState<string | null>(null);
  const [showOpening, setShowOpening] = useState(false);
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
    onSuccess: (c) => { setNotice(t("periods.closed", { label: c.label })); refresh(); },
  });
  const reopen = useMutation({
    mutationFn: (reason: string) => post<PeriodClosingDto>("/api/accounting/periods/reopen", { companyId: company!.id, reason }),
    onSuccess: (c) => { setNotice(t("periods.reopened", { label: c.label })); refresh(); },
  });
  const closeYear = useMutation({
    mutationFn: (date: string) => post<JournalEntryDto>("/api/accounting/years/close", { companyId: company!.id, date }),
    onSuccess: (j) => { setNotice(t("periods.yearClosed", { label: j.sourceNumber, number: j.number })); refresh(); },
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
  const error = closeMonth.error ?? reopen.error ?? closeYear.error ?? opening.error ?? openingInvoice.error;
  const field = (e: FormEvent<HTMLFormElement>, name: string) => { e.preventDefault(); return String(new FormData(e.currentTarget).get(name)); };
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const debit = lines.reduce((a, l) => a + Number(l.debit || 0), 0);
  const credit = lines.reduce((a, l) => a + Number(l.credit || 0), 0);

  return (
    <>
      <div className="page-head"><h1>{t("nav.periods")}</h1></div>
      {periods.data?.lockDate
        ? <div className="lock">{t("periods.lockedUntil", { date: date(periods.data.lockDate) })}</div>
        : <p className="muted">{t("periods.noneClosed")}</p>}
      {notice && <div className="notice">{notice}</div>}
      {error && <div className="error">{error.message}</div>}

      {manage && (
        <div className="cards">
          <form className="card" onSubmit={(e) => closeMonth.mutate(field(e, "date"))}>
            <div className="card-label">{t("periods.closeMonth")}</div>
            <p className="muted small">{t("periods.closeMonthHint")}</p>
            <div className="inline-fields"><input type="date" name="date" defaultValue={todayIso()} required /><button disabled={closeMonth.isPending}>{t("periods.close")}</button></div>
          </form>
          {latest && (
            <form className="card" onSubmit={(e) => reopen.mutate(field(e, "reason"))}>
              <div className="card-label">{t("periods.reopen", { label: latest.label })}</div>
              <p className="muted small">{t("periods.reopenHint")}</p>
              <div className="inline-fields"><input name="reason" placeholder={t("periods.reason")} required /><button className="secondary" disabled={reopen.isPending}>{t("periods.reopenButton")}</button></div>
            </form>
          )}
          <form className="card" onSubmit={(e) => closeYear.mutate(field(e, "date"))}>
            <div className="card-label">{t("periods.closeYear")}</div>
            <p className="muted small">{t("periods.closeYearHint")}</p>
            <div className="inline-fields"><input type="date" name="date" defaultValue={todayIso()} required /><button disabled={closeYear.isPending}>{t("periods.close")}</button></div>
          </form>
        </div>
      )}

      <h3>{t("periods.history")}</h3>
      {periods.data?.closings.length ? (
        <table className="table">
          <thead><tr><th>{t("fields.period")}</th><th>{t("periods.from")}</th><th>{t("periods.to")}</th><th className="num">{t("periods.tbTotal")}</th>
            <th className="num">{t("periods.profit")}</th><th>{t("periods.closedBy")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {periods.data.closings.map((c) => (
              <tr key={c.id} className={c.status === "Reopened" ? "muted" : ""}>
                <td>{c.label}</td><td>{date(c.start)}</td><td>{date(c.end)}</td><td className="num">{formatMoney(c.totalDebit)}</td>
                <td className={`num ${c.netProfit < 0 ? "neg" : ""}`}>{formatMoney(c.netProfit)}</td>
                <td>{c.closedBy} · {date(c.closedAt.slice(0, 10))}</td>
                <td>{c.status === "Closed"
                  ? <span className="badge Submitted">{t("periods.statusClosed")}</span>
                  : <span className="badge" title={c.reopenReason}>{t("periods.statusReopened")}{c.reopenReason ? ` · ${c.reopenReason}` : ""}</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}

      {manage && (
        <>
          <h3>{t("periods.opening")}</h3>
          <p className="muted small">{t("periods.openingHint")}</p>
          {!showOpening && <button className="secondary" onClick={() => setShowOpening(true)}>{t("periods.enterOpening")}</button>}
          {showOpening && (
            <form className="form-grid" onSubmit={(e) => opening.mutate(field(e, "date"))}>
              <label>{t("attendance.date")}<input type="date" name="date" required /></label>
              <div className="lines">
                {lines.map((l, i) => (
                  <div className="line" key={i}>
                    <select value={l.accountCode} onChange={(e) => setLine(i, { accountCode: e.target.value })}>
                      <option value="">—</option>
                      {accounts.data?.filter((a) => !a.isGroup).map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
                    </select>
                    <input placeholder={t("acc.debit")} type="number" step="0.01" min="0" value={l.debit} onChange={(e) => setLine(i, { debit: e.target.value, credit: "" })} />
                    <input placeholder={t("acc.credit")} type="number" step="0.01" min="0" value={l.credit} onChange={(e) => setLine(i, { credit: e.target.value, debit: "" })} />
                  </div>
                ))}
                <div className="line-foot">
                  <button type="button" className="secondary" onClick={() => setLines((ls) => [...ls, emptyLine()])}>{t("acc.addLine")}</button>
                  <span className={debit === credit && debit > 0 ? "ok" : "error"}>{t("acc.debit")} {formatMoney(debit)} · {t("acc.credit")} {formatMoney(credit)}</span>
                </div>
              </div>
              <div className="form-actions">
                <button type="submit" disabled={opening.isPending || debit !== credit || debit === 0}>{t("periods.postOpening")}</button>
                <button type="button" className="secondary" onClick={() => setShowOpening(false)}>{t("common.cancel")}</button>
              </div>
            </form>
          )}
          <h3>{t("periods.openingInvoices")}</h3>
          <p className="muted small">{t("periods.openingInvoicesHint")}</p>
          <form className="form-grid" onSubmit={(e) => { e.preventDefault(); openingInvoice.mutate(Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>); e.currentTarget.reset(); }}>
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
            <div className="form-actions"><button type="submit" disabled={openingInvoice.isPending}>{t("periods.addOpeningInvoice")}</button></div>
          </form>
        </>
      )}
    </>
  );
}
