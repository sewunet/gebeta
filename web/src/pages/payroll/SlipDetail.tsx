import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney } from "../../lib/ethiopian";
import { can, type SalarySlipDto } from "../../lib/types";

export function SlipDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const { id } = useParams({ from: "/payroll/slips/$id" });
  const queryClient = useQueryClient();
  const slip = useQuery({ queryKey: ["slip", id], queryFn: () => api<SalarySlipDto>(`/api/payroll/salary-slips/${id}`) });
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel") => post<SalarySlipDto>(`/api/payroll/salary-slips/${id}/${action}`),
    onSuccess: (updated) => {
      queryClient.setQueryData(["slip", id], updated);
      void queryClient.invalidateQueries({ queryKey: ["slips"] });
    },
  });

  if (slip.isPending) return <p>{t("common.loading")}</p>;
  if (slip.isError) return <p className="error">{slip.error.message}</p>;
  const s = slip.data;
  const am = i18n.language === "am";

  return (
    <>
      <Link to="/payroll/slips" className="back">← {t("slips.back")}</Link>
      <div className="page-head">
        <h1>{s.number} · {s.employeeName}</h1>
        <span className={`badge ${s.status}`}>{t(`status.${s.status}`)}</span>
      </div>
      <p className="muted">
        {formatDate(s.periodStart, me.calendar, am)} – {formatDate(s.periodEnd, me.calendar, am)} · {s.employeeNumber} · {s.structureName} ·{" "}
        {t("fields.paymentDays")}: {s.paymentDays}/{s.totalWorkingDays} · {t("fields.taxTable")}: {s.taxTable}
      </p>
      <table className="table narrow">
        <thead><tr><th>{t("fields.component")}</th><th>{t("fields.type")}</th><th className="num">{t("fields.amount")}</th></tr></thead>
        <tbody>
          {s.lines.map((l, i) => (
            <tr key={`${l.abbreviation}-${i}`} className={l.type === "EmployerContribution" ? "muted" : ""}>
              <td title={l.formula}>{l.component}</td><td>{l.type}</td><td className="num">{formatMoney(l.amount)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr><td colSpan={2}>{t("fields.gross")}</td><td className="num">{formatMoney(s.grossPay)}</td></tr>
          <tr><td colSpan={2}>{t("fields.deductions")}</td><td className="num">{formatMoney(s.totalDeductions)}</td></tr>
          <tr className="strong"><td colSpan={2}>{t("fields.net")}</td><td className="num">{formatMoney(s.netPay)}</td></tr>
          <tr className="muted"><td colSpan={2}>{t("fields.employer")}</td><td className="num">{formatMoney(s.employerContributions)}</td></tr>
        </tfoot>
      </table>
      <div className="form-actions">
        {s.status === "Draft" && can(me, "payroll.salary-slips.submit") && (
          <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>
        )}
        {s.status === "Submitted" && can(me, "payroll.salary-slips.cancel") && (
          <button className="danger" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("slips.cancelSlip")}</button>
        )}
        {act.isError && <span className="error">{act.error.message}</span>}
      </div>
    </>
  );
}
