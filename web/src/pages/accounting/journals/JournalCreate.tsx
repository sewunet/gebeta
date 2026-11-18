import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../../app/context";
import { api, post } from "../../../lib/api";
import { formatMoney, todayIso } from "../../../lib/ethiopian";
import { useCostCenters } from "../../../lib/queries";
import type { AccountDto, JournalEntryDto } from "../../../lib/types";
import { DynamicPage, MessageStrip } from "../../../ui/fiori";

type Line = { accountCode: string; debit: string; credit: string; description: string; costCenterId: string };
const emptyLine = (): Line => ({ accountCode: "", debit: "", credit: "", description: "", costCenterId: "" });

/** A new balanced journal entry; saved and posted in one step from the footer bar. */
export function JournalCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [lines, setLines] = useState<Line[]>([emptyLine(), emptyLine()]);
  const centers = useCostCenters();
  // The column only shows once the company has more than its Main cost centre; a line without one is Main.
  const choosable = (centers.data ?? []).filter((c) => c.isActive && !c.isMain);
  const accounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company,
  });
  const create = useMutation({
    mutationFn: async (body: { postingDate: string; remark: string }) => {
      const draft = await post<JournalEntryDto>("/api/accounting/journals", {
        companyId: company!.id, ...body,
        lines: lines.filter((l) => l.accountCode).map((l) => ({
          accountCode: l.accountCode, debit: Number(l.debit || 0), credit: Number(l.credit || 0), description: l.description || null, costCenterId: l.costCenterId || null,
        })),
      });
      return post<JournalEntryDto>(`/api/accounting/journals/${draft.id}/submit`);
    },
    onSuccess: (j) => {
      for (const key of ["journals", "trial-balance", "financial"]) void queryClient.invalidateQueries({ queryKey: [key] });
      void navigate({ to: "/accounting/journals/$id", params: { id: j.id } });
    },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const debit = lines.reduce((a, l) => a + Number(l.debit || 0), 0);
  const credit = lines.reduce((a, l) => a + Number(l.credit || 0), 0);
  const balanced = debit === credit && debit > 0;
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    create.mutate({ postingDate: String(f.get("postingDate")), remark: String(f.get("remark")) });
  };

  return (
    <DynamicPage
      title={t("acc.newJournal")}
      breadcrumb={<Link to="/accounting/journals">{t("nav.journals")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        <span className={balanced ? "ok" : "muted"}>{t("acc.debit")} {formatMoney(debit)} · {t("acc.credit")} {formatMoney(credit)}</span>
        <button type="submit" form="journal-create" disabled={create.isPending || !balanced}>{t("acc.post")}</button>
        <button className="secondary" onClick={() => void navigate({ to: "/accounting/journals" })}>{t("common.cancel")}</button>
      </>}
    >
      <form id="journal-create" onSubmit={onSubmit}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t("attendance.date")}<span className="required" aria-hidden>*</span><input type="date" name="postingDate" defaultValue={todayIso()} required /></label>
            <label>{t("acc.remark")}<span className="required" aria-hidden>*</span><input name="remark" required /></label>
          </div>
        </section>
        <section className="object-section">
          <h2>{t("ui.lines")}</h2>
          <table className="table edit-table">
            <thead><tr><th>{t("acc.code")}</th><th className="num">{t("acc.debit")}</th><th className="num">{t("acc.credit")}</th><th>{t("cash.description")}</th>{choosable.length > 0 && <th>{t("nav.costCenters")}</th>}<th /></tr></thead>
            <tbody>
              {lines.map((l, i) => (
                <tr key={i}>
                  <td>
                    <select aria-label={t("acc.code")} value={l.accountCode} onChange={(e) => setLine(i, { accountCode: e.target.value })}>
                      <option value="">—</option>
                      {accounts.data?.filter((a) => !a.isGroup).map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
                    </select>
                  </td>
                  <td><input aria-label={t("acc.debit")} className="num" type="number" step="0.01" min="0" value={l.debit} onChange={(e) => setLine(i, { debit: e.target.value, credit: "" })} /></td>
                  <td><input aria-label={t("acc.credit")} className="num" type="number" step="0.01" min="0" value={l.credit} onChange={(e) => setLine(i, { credit: e.target.value, debit: "" })} /></td>
                  <td><input aria-label={t("cash.description")} value={l.description} onChange={(e) => setLine(i, { description: e.target.value })} /></td>
                  {choosable.length > 0 && <td>
                    <select aria-label={t("nav.costCenters")} value={l.costCenterId} onChange={(e) => setLine(i, { costCenterId: e.target.value })}>
                      <option value="">{t("payment.mainDefault")}</option>
                      {choosable.map((c) => <option key={c.id} value={c.id}>{c.code}</option>)}
                    </select>
                  </td>}
                  <td>{lines.length > 2 && <button type="button" className="link-button" aria-label={t("ui.remove")} onClick={() => setLines((ls) => ls.filter((_, j) => j !== i))}>×</button>}</td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td><button type="button" className="secondary" onClick={() => setLines((ls) => [...ls, emptyLine()])}>{t("acc.addLine")}</button></td>
              <td className="num">{formatMoney(debit)}</td><td className="num">{formatMoney(credit)}</td><td colSpan={choosable.length > 0 ? 3 : 2} /></tr></tfoot>
          </table>
          {!balanced && debit + credit > 0 && <MessageStrip tone="critical">{t("acc.notBalanced", { diff: formatMoney(Math.abs(debit - credit)) })}</MessageStrip>}
        </section>
      </form>
    </DynamicPage>
  );
}
