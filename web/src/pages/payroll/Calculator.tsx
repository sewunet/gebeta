import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { post } from "../../lib/api";
import { formatMoney, todayIso } from "../../lib/ethiopian";
import type { SalaryCalculationDto } from "../../lib/types";
import { DynamicPage, FilterBar, FilterField, MessageStrip } from "../../ui/fiori";

/** Net pay and tax for a basic salary, with the formula behind each line. */
export function Calculator() {
  const { t } = useTranslation();
  const [base, setBase] = useState("13000");
  const [days, setDays] = useState("30");
  const [date, setDate] = useState(todayIso());
  const calc = useMutation({
    mutationFn: () => post<SalaryCalculationDto>("/api/payroll/calculator", { base: Number(base), paymentDays: Number(days), payrollDate: date }),
  });
  const onSubmit = (e: FormEvent) => { e.preventDefault(); calc.mutate(); };
  const sum = (type: string) => calc.data?.lines.filter((l) => l.type === type).reduce((a, l) => a + l.amount, 0) ?? 0;

  return (
    <DynamicPage
      title={t("nav.calculator")}
      subtitle={t("calculator.explain")}
      header={<>
        <form id="calc" onSubmit={onSubmit}>
          <FilterBar>
            <FilterField label={t("fields.base")}><input type="number" min="0" step="0.01" value={base} onChange={(e) => setBase(e.target.value)} /></FilterField>
            <FilterField label={t("fields.paymentDays")}><input type="number" min="0" max="30" value={days} onChange={(e) => setDays(e.target.value)} /></FilterField>
            <FilterField label={t("fields.payrollDate")}><input type="date" value={date} onChange={(e) => setDate(e.target.value)} /></FilterField>
            <button type="submit" disabled={calc.isPending}>{t("calculator.calculate")}</button>
          </FilterBar>
        </form>
        {calc.data && (
          <div className="kpis kpis-gap">
            <div className="kpi"><div className="kpi-label">{t("fields.gross")}</div><div className="kpi-value">{formatMoney(sum("Earning"))}</div></div>
            <div className="kpi"><div className="kpi-label">{t("fields.deductions")}</div><div className="kpi-value">{formatMoney(sum("Deduction"))}</div></div>
            <div className="kpi"><div className="kpi-label">{t("fields.net")}</div><div className="kpi-value positive">{formatMoney(calc.data.netPay)}</div></div>
            <div className="kpi"><div className="kpi-label">{t("fields.employer")}</div><div className="kpi-value">{formatMoney(sum("EmployerContribution"))}</div></div>
          </div>
        )}
      </>}
    >
      {calc.isError && <MessageStrip tone="negative">{calc.error.message}</MessageStrip>}
      {calc.data ? (
        <section className="table-card">
          <div className="table-toolbar"><h2>{t("calculator.breakdown")}</h2><span className="muted small">{t("fields.taxTable")}: {calc.data.taxTable}</span></div>
          <table className="table">
            <thead><tr><th>{t("fields.component")}</th><th>{t("fields.type")}</th><th>{t("calculator.formula")}</th><th className="num">{t("fields.amount")}</th></tr></thead>
            <tbody>
              {calc.data.lines.map((l) => (
                <tr key={l.abbreviation} className={l.type === "EmployerContribution" ? "muted" : ""}>
                  <td>{l.component}</td><td>{t(`slips.lineType.${l.type}`)}</td><td className="mono small muted">{l.formula}</td><td className="num">{formatMoney(l.amount)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={3}>{t("fields.net")}</td><td className="num">{formatMoney(calc.data.netPay)}</td></tr></tfoot>
          </table>
        </section>
      ) : <div className="empty">{t("calculator.prompt")}</div>}
    </DynamicPage>
  );
}
