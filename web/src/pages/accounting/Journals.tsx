import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type AccountDto, type JournalEntryDto } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";

type Line = { accountCode: string; debit: string; credit: string; description: string };
const emptyLine = (): Line => ({ accountCode: "", debit: "", credit: "", description: "" });

export function Journals() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [month, setMonth] = useState(currentMonth());
  const [open, setOpen] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [lines, setLines] = useState<Line[]>([emptyLine(), emptyLine()]);
  const { start, end } = monthRange(month);
  const journals = useQuery({
    queryKey: ["journals", company?.id, month],
    queryFn: () => api<JournalEntryDto[]>(`/api/accounting/journals?companyId=${company!.id}&from=${start}&to=${end}`),
    enabled: !!company,
  });
  const accounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company,
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ["journals"] });
    void queryClient.invalidateQueries({ queryKey: ["trial-balance"] });
  };
  const create = useMutation({
    mutationFn: async (body: { postingDate: string; remark: string }) => {
      const draft = await post<JournalEntryDto>("/api/accounting/journals", {
        companyId: company!.id, ...body,
        lines: lines.filter((l) => l.accountCode).map((l) => ({ accountCode: l.accountCode, debit: Number(l.debit || 0), credit: Number(l.credit || 0), description: l.description || null })),
      });
      return post<JournalEntryDto>(`/api/accounting/journals/${draft.id}/submit`);
    },
    onSuccess: () => { setShowForm(false); setLines([emptyLine(), emptyLine()]); refresh(); },
  });
  const cancel = useMutation({ mutationFn: (id: string) => post(`/api/accounting/journals/${id}/cancel`), onSuccess: refresh });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";
  const debit = lines.reduce((a, l) => a + Number(l.debit || 0), 0);
  const credit = lines.reduce((a, l) => a + Number(l.credit || 0), 0);
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    create.mutate({ postingDate: String(f.get("postingDate")), remark: String(f.get("remark")) });
  };
  const leaves = accounts.data?.filter((a) => !a.isGroup) ?? [];

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.journals")}</h1>
        <div className="inline-fields">
          <label className="field-inline"><span>{t("common.month")}</span><input type="month" value={month} onChange={(e) => setMonth(e.target.value)} /></label>
          {can(me, "accounting.journals.create") && <button onClick={() => setShowForm((s) => !s)}>{t("acc.newJournal")}</button>}
        </div>
      </div>
      {showForm && (
        <form className="form-grid" onSubmit={onSubmit}>
          <label>{t("attendance.date")}<input type="date" name="postingDate" defaultValue={todayIso()} required /></label>
          <label>{t("acc.remark")}<input name="remark" required /></label>
          <div className="lines">
            {lines.map((l, i) => (
              <div className="line" key={i}>
                <select value={l.accountCode} onChange={(e) => setLine(i, { accountCode: e.target.value })}>
                  <option value="">—</option>
                  {leaves.map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
                </select>
                <input placeholder={t("acc.debit")} type="number" step="0.01" min="0" value={l.debit} onChange={(e) => setLine(i, { debit: e.target.value, credit: "" })} />
                <input placeholder={t("acc.credit")} type="number" step="0.01" min="0" value={l.credit} onChange={(e) => setLine(i, { credit: e.target.value, debit: "" })} />
                <input placeholder={t("cash.description")} value={l.description} onChange={(e) => setLine(i, { description: e.target.value })} />
              </div>
            ))}
            <div className="line-foot">
              <button type="button" className="secondary" onClick={() => setLines((ls) => [...ls, emptyLine()])}>{t("acc.addLine")}</button>
              <span className={debit === credit && debit > 0 ? "ok" : "error"}>{t("acc.debit")} {formatMoney(debit)} · {t("acc.credit")} {formatMoney(credit)}</span>
            </div>
          </div>
          <div className="form-actions">
            <button type="submit" disabled={create.isPending || debit !== credit || debit === 0}>{t("slips.submit")}</button>
            {create.isError && <span className="error">{create.error.message}</span>}
          </div>
        </form>
      )}
      {cancel.isError && <div className="error">{cancel.error.message}</div>}
      {journals.data?.length ? (
        <table className="table">
          <thead><tr><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("acc.remark")}</th><th>{t("acc.source")}</th><th className="num">{t("cash.amount")}</th><th>{t("fields.status")}</th><th /></tr></thead>
          <tbody>
            {journals.data.flatMap((j) => [
              <tr key={j.id} onClick={() => setOpen(open === j.id ? null : j.id)} style={{ cursor: "pointer" }}>
                <td className="mono">{j.number}</td><td>{formatDate(j.postingDate, me.calendar, am)}</td><td>{j.remark}</td>
                <td>{j.source}{j.sourceNumber ? ` · ${j.sourceNumber}` : ""}</td><td className="num">{formatMoney(j.totalDebit)}</td>
                <td><span className={`badge ${j.status}`}>{t(`status.${j.status}`)}</span></td>
                <td>{j.status === "Submitted" && ["Manual", "Opening", "YearEnd"].includes(j.source) && can(me, "accounting.journals.cancel") &&
                  <button className="secondary" onClick={(e) => { e.stopPropagation(); cancel.mutate(j.id); }}>{t("common.cancel")}</button>}</td>
              </tr>,
              open === j.id ? (
                <tr key={`${j.id}-lines`} className="detail"><td colSpan={7}>
                  <table className="table inner">
                    <tbody>{j.lines.map((l, i) => (
                      <tr key={i}><td className="mono">{l.accountCode}</td><td>{l.accountName}</td><td className="num">{l.debit ? formatMoney(l.debit) : ""}</td><td className="num">{l.credit ? formatMoney(l.credit) : ""}</td><td className="muted">{l.description}</td></tr>
                    ))}</tbody>
                  </table>
                </td></tr>
              ) : null,
            ])}
          </tbody>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </>
  );
}
