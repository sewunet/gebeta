import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import type { SalarySlipDto } from "../../lib/types";
import { currentMonth, monthRange } from "./month";

export function Slips() {
  const { t } = useTranslation();
  const { company } = useSession();
  const [month, setMonth] = useState(currentMonth());
  const { start, end } = monthRange(month);
  const slips = useQuery({
    queryKey: ["slips", company?.id, month],
    queryFn: () => api<SalarySlipDto[]>(`/api/payroll/salary-slips?companyId=${company!.id}&from=${start}&to=${end}`),
    enabled: !!company,
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const active = (slips.data ?? []).filter((s) => s.status !== "Cancelled");

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.salarySlips")}</h1>
        <label className="field-inline"><span>{t("common.month")}</span><input type="month" value={month} onChange={(e) => setMonth(e.target.value)} /></label>
      </div>
      {slips.isPending ? <p>{t("common.loading")}</p> : slips.data?.length ? (
        <table className="table">
          <thead>
            <tr>
              <th>{t("fields.number")}</th><th>{t("fields.employee")}</th><th>{t("fields.period")}</th>
              <th className="num">{t("fields.gross")}</th><th className="num">{t("fields.deductions")}</th><th className="num">{t("fields.net")}</th><th>{t("fields.status")}</th>
            </tr>
          </thead>
          <tbody>
            {slips.data.map((s) => (
              <tr key={s.id}>
                <td className="mono"><Link to="/payroll/slips/$id" params={{ id: s.id }}>{s.number}</Link></td>
                <td>{s.employeeName}</td><td>{s.ethiopianPeriod}</td>
                <td className="num">{formatMoney(s.grossPay)}</td><td className="num">{formatMoney(s.totalDeductions)}</td>
                <td className="num strong">{formatMoney(s.netPay)}</td>
                <td><span className={`badge ${s.status}`}>{t(`status.${s.status}`)}</span></td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td colSpan={3}>{t("common.total")} ({active.length})</td>
              <td className="num">{formatMoney(active.reduce((a, s) => a + s.grossPay, 0))}</td>
              <td className="num">{formatMoney(active.reduce((a, s) => a + s.totalDeductions, 0))}</td>
              <td className="num strong">{formatMoney(active.reduce((a, s) => a + s.netPay, 0))}</td><td />
            </tr>
          </tfoot>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </>
  );
}
