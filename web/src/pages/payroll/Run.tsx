import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { download, post } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import { can, type PayrollRunDto } from "../../lib/types";
import { currentMonth, monthRange } from "./month";

export function Run() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [month, setMonth] = useState(currentMonth());
  const [run, setRun] = useState<PayrollRunDto | null>(null);
  const { start, end } = monthRange(month);

  const step = useMutation({
    mutationFn: async (action: "create" | "generate" | "submit") =>
      action === "create"
        ? post<PayrollRunDto>("/api/payroll/runs", { companyId: company!.id, periodStart: start, periodEnd: end })
        : post<PayrollRunDto>(`/api/payroll/runs/${run!.id}/${action}`),
    onSuccess: (r) => {
      setRun(r);
      void queryClient.invalidateQueries({ queryKey: ["slips"] });
    },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const query = `companyId=${company.id}&periodStart=${start}&periodEnd=${end}&format=csv`;

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.payrollRun")}</h1>
        <label className="field-inline">
          <span>{t("common.month")}</span>
          <input type="month" value={month} onChange={(e) => { setMonth(e.target.value); setRun(null); }} />
        </label>
      </div>
      <p className="muted">{t("run.explain")}</p>
      {can(me, "payroll.runs.manage") && (
        <div className="steps">
          <button onClick={() => step.mutate("create")} disabled={!!run || step.isPending}>1. {t("run.create")}</button>
          <button onClick={() => step.mutate("generate")} disabled={!run || run.slipsGenerated || step.isPending}>2. {t("run.generate")}</button>
          <button onClick={() => step.mutate("submit")} disabled={!run?.slipsGenerated || run.status !== "Draft" || step.isPending}>3. {t("run.submit")}</button>
        </div>
      )}
      {step.isError && <div className="error">{step.error.message}</div>}
      {run && (
        <div className="cards">
          <div className="card"><div className="card-label">{run.number}</div><div className="card-value">{t(`status.${run.status}`)}</div></div>
          <div className="card"><div className="card-label">{t("run.slips")}</div><div className="card-value">{run.slipCount}</div></div>
          <div className="card"><div className="card-label">{t("fields.gross")}</div><div className="card-value">{formatMoney(run.totalGross)}</div></div>
          <div className="card"><div className="card-label">{t("fields.net")}</div><div className="card-value">{formatMoney(run.totalNet)}</div></div>
        </div>
      )}
      {run && run.issues.length > 0 && (
        <>
          <h2>{t("run.issues")}</h2>
          <ul className="issues">{run.issues.map((i) => <li key={i.employeeId}><strong>{i.employeeName}</strong>: {i.reason}</li>)}</ul>
        </>
      )}
      {can(me, "payroll.reports.read") && (
        <>
          <h2>{t("run.reports")}</h2>
          <div className="steps">
            <button className="secondary" onClick={() => void download(`/api/payroll/reports/income-tax?${query}`, `income-tax-${month}.csv`)}>{t("run.incomeTax")} (CSV)</button>
            <button className="secondary" onClick={() => void download(`/api/payroll/reports/pension?${query}`, `pension-${month}.csv`)}>{t("run.pension")} (CSV)</button>
            <button className="secondary" onClick={() => void download(`/api/payroll/reports/bank-transfer?${query}`, `bank-transfer-${month}.csv`)}>{t("run.bank")} (CSV)</button>
          </div>
        </>
      )}
    </>
  );
}
