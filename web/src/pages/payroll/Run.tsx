import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { download, post } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import { can, type PayrollRunDto } from "../../lib/types";
import { DynamicPage, FilterBar, FilterField, MessageStrip, ObjectStatus, statusTone } from "../../ui/fiori";
import { currentMonth, monthRange } from "./month";
import { RunComparison } from "./RunComparison";

/** The month's payroll run as a three-step process (create, generate slips, submit), with the declaration files. */
export function Run() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [month, setMonth] = useState(currentMonth());
  const [run, setRun] = useState<PayrollRunDto | null>(null);
  const { start, end } = monthRange(month);

  const step = useMutation({
    mutationFn: async (action: "create" | "generate" | "submit") =>
      action === "create"
        ? post<PayrollRunDto>("/api/payroll/runs", { companyId: company!.id, periodStart: start, periodEnd: end })
        : post<PayrollRunDto>(`/api/payroll/runs/${run!.id}/${action}`),
    onSuccess: (r) => { setRun(r); void queryClient.invalidateQueries({ queryKey: ["slips"] }); },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const query = `companyId=${company.id}&periodStart=${start}&periodEnd=${end}&format=csv`;
  const manage = can(me, "payroll.runs.manage");
  const stage = !run ? 0 : !run.slipsGenerated ? 1 : run.status === "Draft" ? 2 : 3;
  const steps = [
    { label: t("run.create"), action: "create" as const, enabled: !run },
    { label: t("run.generate"), action: "generate" as const, enabled: !!run && !run.slipsGenerated },
    { label: t("run.submit"), action: "submit" as const, enabled: !!run?.slipsGenerated && run.status === "Draft" },
  ];

  return (
    <DynamicPage
      title={t("nav.payrollRun")}
      subtitle={<>{company.name}{run && <><span className="mono">{run.number}</span><ObjectStatus tone={statusTone(run.status)}>{t(`status.${run.status}`)}</ObjectStatus></>}</>}
      actions={run?.slipsGenerated && <button className="secondary" onClick={() => void navigate({ to: "/payroll/slips" })}>{t("nav.salarySlips")}</button>}
      header={<>
        <FilterBar>
          <FilterField label={t("common.month")}><input type="month" value={month} onChange={(e) => { setMonth(e.target.value); setRun(null); }} /></FilterField>
        </FilterBar>
        {run && (
          <div className="kpis kpis-gap">
            <div className="kpi"><div className="kpi-label">{t("run.slips")}</div><div className="kpi-value">{run.slipCount}</div></div>
            <div className="kpi"><div className="kpi-label">{t("fields.gross")}</div><div className="kpi-value">{formatMoney(run.totalGross)}</div></div>
            <div className="kpi"><div className="kpi-label">{t("fields.net")}</div><div className="kpi-value positive">{formatMoney(run.totalNet)}</div></div>
          </div>
        )}
      </>}
    >
      <MessageStrip tone="information">{t("run.explain")}</MessageStrip>
      {step.isError && <MessageStrip tone="negative">{step.error.message}</MessageStrip>}
      {run && run.issues.length > 0 && (
        <MessageStrip tone="critical">
          <strong>{t("run.issues")}:</strong> {run.issues.map((i) => `${i.employeeName} (${i.reason})`).join("; ")}
        </MessageStrip>
      )}

      <section className="object-section">
        <h2>{t("run.process")}</h2>
        <ol className="process-flow">
          {steps.map((s, i) => (
            <li key={s.action} className={i < stage ? "done" : i === stage ? "current" : ""}>
              <span className="step-no">{i < stage ? "✓" : i + 1}</span>
              <span className="step-label">{s.label}</span>
              {manage && i === stage && <button onClick={() => step.mutate(s.action)} disabled={!s.enabled || step.isPending}>{s.label}</button>}
            </li>
          ))}
        </ol>
      </section>

      {run?.slipsGenerated && <RunComparison run={run} />}

      {can(me, "payroll.reports.read") && (
        <section className="object-section">
          <h2>{t("run.reports")}</h2>
          <div className="inline-fields">
            <button className="secondary" onClick={() => void download(`/api/payroll/reports/income-tax?${query}`, `income-tax-${month}.csv`)}>{t("run.incomeTax")} (CSV)</button>
            <button className="secondary" onClick={() => void download(`/api/payroll/reports/pension?${query}`, `pension-${month}.csv`)}>{t("run.pension")} (CSV)</button>
            <button className="secondary" onClick={() => void download(`/api/payroll/reports/bank-transfer?${query}`, `bank-transfer-${month}.csv`)}>{t("run.bank")} (CSV)</button>
          </div>
        </section>
      )}
    </DynamicPage>
  );
}
