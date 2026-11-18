import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { api, download } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import type { PayComparisonRow, PayrollComparisonDto, PayrollRunDto, PayTotals } from "../../lib/types";
import { MessageStrip, ObjectStatus, type Tone } from "../../ui/fiori";

const tone: Record<PayComparisonRow["status"], Tone> = { Changed: "critical", New: "information", Missing: "negative", Same: "neutral" };

/** This run against last month: who is paid differently and why, before the run is submitted. A warning, never a block. */
export function RunComparison({ run }: { run: PayrollRunDto }) {
  const { t } = useTranslation();
  const [all, setAll] = useState(false);
  const comparison = useQuery({
    queryKey: ["payroll-comparison", run.id, run.slipCount, run.status],
    queryFn: () => api<PayrollComparisonDto>(`/api/payroll/runs/${run.id}/comparison`),
  });
  if (comparison.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (comparison.isError) return <MessageStrip tone="negative">{comparison.error.message}</MessageStrip>;
  const c = comparison.data;
  const rows = all ? c.rows : c.rows.filter((r) => r.status !== "Same");
  const total = (label: string, pick: (x: PayTotals) => number, money = true) => {
    const before = pick(c.previousTotals), after = pick(c.currentTotals);
    const show = (v: number) => (money ? formatMoney(v) : String(v));
    return <tr key={label}><td>{label}</td><td className="num">{show(before)}</td><td className="num">{show(after)}</td><td className="num">{show(after - before)}</td></tr>;
  };

  return (
    <section className="object-section">
      <div className="table-toolbar">
        <h2>{t("compare.title")} {c.flaggedCount > 0 && <ObjectStatus tone="critical">{t("compare.flagged", { count: c.flaggedCount })}</ObjectStatus>}</h2>
        <div className="inline-fields">
          <label className="check"><input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} /> {t("compare.showAll")}</label>
          <button className="secondary" onClick={() => void download(`/api/payroll/runs/${run.id}/comparison?format=csv`, `payroll-comparison-${c.periodStart.slice(0, 7)}.csv`)}>CSV</button>
        </div>
      </div>
      {!c.hasPrevious ? <MessageStrip tone="information">{t("compare.none")}</MessageStrip> : (
        <>
          <p className="muted small">{t("compare.explain", { threshold: c.thresholdPercent })}</p>
          <table className="table">
            <thead><tr><th /><th className="num">{t("compare.last")}</th><th className="num">{t("compare.this")}</th><th className="num">{t("compare.change")}</th></tr></thead>
            <tbody>
              {total(t("compare.headcount"), (x) => x.headcount, false)}
              {total(t("fields.gross"), (x) => x.gross)}
              {total(t("run.incomeTax"), (x) => x.incomeTax)}
              {total(t("run.pension"), (x) => x.employeePension)}
              {total(t("compare.employer"), (x) => x.employerPension)}
              {total(t("fields.net"), (x) => x.net)}
            </tbody>
          </table>
        </>
      )}
      {rows.length > 0 && (
        <table className="table responsive">
          <thead><tr>
            <th>{t("fields.number")}</th><th>{t("fields.name")}</th><th className="num">{t("compare.last")}</th><th className="num">{t("compare.this")}</th>
            <th className="num">{t("compare.change")}</th><th>{t("fields.status")}</th><th>{t("compare.reasons")}</th>
          </tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.employeeId} className={r.flagged ? "suggested" : ""}>
                <td data-label={t("fields.number")} className="mono">{r.employeeNumber}</td>
                <td data-label={t("fields.name")}>{r.employeeName}</td>
                <td data-label={t("compare.last")} className="num">{r.previous ? formatMoney(r.previous.net) : "—"}</td>
                <td data-label={t("compare.this")} className="num">{r.current ? formatMoney(r.current.net) : "—"}</td>
                <td data-label={t("compare.change")} className="num">{formatMoney(r.netChange)}{r.changePercent != null && <span className="muted small"> ({r.changePercent > 0 ? "+" : ""}{r.changePercent}%)</span>}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={tone[r.status]}>{t(`compare.status.${r.status}`)}</ObjectStatus></td>
                <td data-label={t("compare.reasons")} className="small">{r.reasons.join(" · ") || "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
