import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { api, download } from "../../lib/api";
import type { PayrollChecksDto, PayrollRunDto } from "../../lib/types";
import { MessageStrip, ObjectStatus } from "../../ui/fiori";

/** Checks as the server names them, to translation keys. */
const CHECK_KEY: Record<string, string> = {
  "Net pay below zero": "negativeNet",
  "Net pay below the minimum share of gross": "lowNet",
  "No bank account": "noBank",
  "Bank account shared with another employee": "sharedBank",
  "No pension number": "noPension",
  "No TIN": "noTin",
  "No base salary": "noBase",
  "Paid with no payment days": "noDays",
};

/** The run's checks, shared by the checks section and the submit confirmation (one request). */
export function useRunChecks(run: PayrollRunDto | null) {
  return useQuery({
    queryKey: ["payroll-checks", run?.id, run?.slipCount, run?.status],
    queryFn: () => api<PayrollChecksDto>(`/api/payroll/runs/${run!.id}/checks`),
    enabled: !!run?.slipsGenerated,
  });
}

/** Problems to look at before submit, whatever last month looked like: low or negative net pay, missing or shared bank accounts, missing numbers. */
export function RunChecks({ run }: { run: PayrollRunDto }) {
  const { t } = useTranslation();
  const checks = useRunChecks(run);
  if (checks.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (checks.isError) return <MessageStrip tone="negative">{checks.error.message}</MessageStrip>;
  const c = checks.data;
  const name = (check: string) => t(`checks.${CHECK_KEY[check] ?? "other"}`, { share: c.minimumNetSharePercent });

  return (
    <section className="object-section">
      <div className="table-toolbar">
        <h2>{t("checks.title")} {c.flaggedEmployees > 0
          ? <ObjectStatus tone={c.errors > 0 ? "negative" : "critical"}>{t("checks.flagged", { count: c.flaggedEmployees })}</ObjectStatus>
          : <ObjectStatus tone="positive">{t("checks.allClear")}</ObjectStatus>}</h2>
        {c.rows.length > 0 && <button className="secondary" onClick={() => void download(`/api/payroll/runs/${run.id}/checks?format=csv`, `payroll-checks-${c.periodStart.slice(0, 7)}.csv`)}>CSV</button>}
      </div>
      <p className="muted small">{t("checks.explain", { count: c.slipsChecked })}</p>
      {c.counts.map((count) => (
        <details key={count.check} className="check-group" open={count.severity === "Error" || c.counts.length === 1}>
          <summary>
            <ObjectStatus tone={count.severity === "Error" ? "negative" : "critical"}>{t(`checks.severity.${count.severity}`)}</ObjectStatus>
            <strong>{name(count.check)}</strong> <span className="muted">({count.employees})</span>
          </summary>
          <table className="table responsive">
            <tbody>
              {c.rows.filter((r) => r.check === count.check).map((r) => (
                <tr key={`${r.slipId}-${r.check}`}>
                  <td data-label={t("fields.number")} className="mono">{r.employeeNumber}</td>
                  <td data-label={t("fields.name")}><Link to="/payroll/slips/$id" params={{ id: r.slipId }}>{r.employeeName}</Link></td>
                  <td data-label={t("checks.details")} className="small">{r.message}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </details>
      ))}
    </section>
  );
}
