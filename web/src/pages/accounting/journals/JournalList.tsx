import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../../app/context";
import { api } from "../../../lib/api";
import { formatDate, formatMoney } from "../../../lib/ethiopian";
import { can, type JournalEntryDto } from "../../../lib/types";
import { FilterBar, FilterField, ListReport, ObjectStatus, statusTone } from "../../../ui/fiori";
import { currentMonth, monthRange } from "../../payroll/month";

const SOURCES = ["Manual", "Payroll", "Cash", "SalesInvoice", "PurchaseInvoice", "Payment", "Opening", "YearEnd", "Bank"];

/** List report of journal entries; a row opens the journal's object page. */
export function JournalList() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const month = monthRange(currentMonth());
  const [from, setFrom] = useState(month.start);
  const [to, setTo] = useState(month.end);
  const [source, setSource] = useState("");
  const [status, setStatus] = useState("");
  const [search, setSearch] = useState("");
  const journals = useQuery({
    queryKey: ["journals", company?.id, from, to],
    queryFn: () => api<JournalEntryDto[]>(`/api/accounting/journals?companyId=${company!.id}&from=${from}&to=${to}`),
    enabled: !!company && from <= to,
  });
  const rows = useMemo(() => (journals.data ?? []).filter((j) => {
    if (source && j.source !== source) return false;
    if (status && j.status !== status) return false;
    const q = search.trim().toLowerCase();
    return !q || [j.number, j.remark, j.sourceNumber, ...j.lines.map((l) => l.accountCode)].some((v) => v?.toLowerCase().includes(q));
  }), [journals.data, source, status, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";

  return (
    <ListReport
      title={t("nav.journals")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("acc.journalSearchHint")} /></FilterField>
          <FilterField label={t("acc.source")}>
            <select value={source} onChange={(e) => setSource(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {SOURCES.map((s) => <option key={s} value={s}>{t(`acc.sources.${s}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {["Draft", "Submitted", "Cancelled"].map((s) => <option key={s} value={s}>{t(`status.${s}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("periods.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>
          <FilterField label={t("periods.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.journals")}
      count={rows.length}
      toolbar={can(me, "accounting.journals.create") && <button onClick={() => void navigate({ to: "/accounting/journals/new" })}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("acc.remark")}</th><th>{t("acc.source")}</th><th className="num">{t("cash.amount")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {rows.map((j) => (
              <tr key={j.id} className="navigable" onClick={() => void navigate({ to: "/accounting/journals/$id", params: { id: j.id } })}>
                <td data-label={t("fields.number")}><span className="mono strong-link">{j.number}</span></td>
                <td data-label={t("attendance.date")}>{formatDate(j.postingDate, me.calendar, am)}</td>
                <td data-label={t("acc.remark")}>{j.remark}</td>
                <td data-label={t("acc.source")}>{t(`acc.sources.${j.source}`)}{j.sourceNumber && <div className="muted small mono">{j.sourceNumber}</div>}</td>
                <td data-label={t("cash.amount")} className="num">{formatMoney(j.totalDebit)}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={statusTone(j.status)}>{t(`status.${j.status}`)}</ObjectStatus></td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{journals.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}
