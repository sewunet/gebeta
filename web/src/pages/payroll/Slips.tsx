import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import type { SalarySlipDto } from "../../lib/types";
import { FilterBar, FilterField, ListReport, ObjectStatus, statusTone } from "../../ui/fiori";
import { currentMonth, monthRange } from "./month";

/** List report of salary slips for a month; a row opens the slip. */
export function Slips() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const [month, setMonth] = useState(currentMonth());
  const [status, setStatus] = useState("");
  const [search, setSearch] = useState("");
  const { start, end } = monthRange(month);
  const slips = useQuery({
    queryKey: ["slips", company?.id, month],
    queryFn: () => api<SalarySlipDto[]>(`/api/payroll/salary-slips?companyId=${company!.id}&from=${start}&to=${end}`),
    enabled: !!company,
  });
  const rows = useMemo(() => (slips.data ?? []).filter((s) => (!status || s.status === status)
    && (!search.trim() || [s.number, s.employeeName, s.employeeNumber].some((v) => v?.toLowerCase().includes(search.trim().toLowerCase())))), [slips.data, status, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const active = rows.filter((s) => s.status !== "Cancelled");

  return (
    <ListReport
      title={t("nav.salarySlips")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("slips.searchHint")} /></FilterField>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {["Draft", "Submitted", "Cancelled"].map((s) => <option key={s} value={s}>{t(`status.${s}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("common.month")}><input type="month" value={month} onChange={(e) => setMonth(e.target.value)} /></FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.salarySlips")}
      count={rows.length}
      toolbar={<button className="secondary" onClick={() => void navigate({ to: "/payroll/run" })}>{t("nav.payrollRun")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr>
            <th>{t("fields.number")}</th><th>{t("fields.employee")}</th><th>{t("fields.period")}</th>
            <th className="num">{t("fields.gross")}</th><th className="num">{t("fields.deductions")}</th><th className="num">{t("fields.net")}</th><th>{t("fields.status")}</th>
          </tr></thead>
          <tbody>
            {rows.map((s) => (
              <tr key={s.id} className="navigable" onClick={() => void navigate({ to: "/payroll/slips/$id", params: { id: s.id } })}>
                <td data-label={t("fields.number")}><span className="mono strong-link">{s.number}</span></td>
                <td data-label={t("fields.employee")}>{s.employeeName}<div className="muted small mono">{s.employeeNumber}</div></td>
                <td data-label={t("fields.period")}>{s.ethiopianPeriod}</td>
                <td data-label={t("fields.gross")} className="num">{formatMoney(s.grossPay)}</td>
                <td data-label={t("fields.deductions")} className="num">{formatMoney(s.totalDeductions)}</td>
                <td data-label={t("fields.net")} className="num strong">{formatMoney(s.netPay)}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={statusTone(s.status)}>{t(`status.${s.status}`)}</ObjectStatus></td>
              </tr>
            ))}
          </tbody>
          <tfoot><tr>
            <td colSpan={3}>{t("common.total")} ({active.length})</td>
            <td className="num">{formatMoney(active.reduce((a, s) => a + s.grossPay, 0))}</td>
            <td className="num">{formatMoney(active.reduce((a, s) => a + s.totalDeductions, 0))}</td>
            <td className="num strong">{formatMoney(active.reduce((a, s) => a + s.netPay, 0))}</td><td />
          </tr></tfoot>
        </table>
      ) : <div className="empty">{slips.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}
