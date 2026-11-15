import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { post } from "../../lib/api";
import { formatMoney, todayIso } from "../../lib/ethiopian";
import type { SalaryCalculationDto } from "../../lib/types";

export function Calculator() {
  const { t } = useTranslation();
  const [base, setBase] = useState("13000");
  const [days, setDays] = useState("30");
  const [date, setDate] = useState(todayIso());
  const calc = useMutation({
    mutationFn: () => post<SalaryCalculationDto>("/api/payroll/calculator", { base: Number(base), paymentDays: Number(days), payrollDate: date }),
  });
  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    calc.mutate();
  };

  return (
    <>
      <h1>{t("nav.calculator")}</h1>
      <p className="muted">{t("calculator.explain")}</p>
      <form className="form-grid" onSubmit={onSubmit}>
        <label>{t("fields.base")}<input type="number" min="0" step="0.01" value={base} onChange={(e) => setBase(e.target.value)} /></label>
        <label>{t("fields.paymentDays")}<input type="number" min="0" max="30" value={days} onChange={(e) => setDays(e.target.value)} /></label>
        <label>{t("fields.payrollDate")}<input type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
        <div className="form-actions"><button type="submit">{t("calculator.calculate")}</button></div>
      </form>
      {calc.isError && <div className="error">{calc.error.message}</div>}
      {calc.data && (
        <table className="table narrow">
          <tbody>
            {calc.data.lines.map((l) => (
              <tr key={l.abbreviation} className={l.type === "EmployerContribution" ? "muted" : ""}>
                <td title={l.formula}>{l.component}</td><td className="num">{formatMoney(l.amount)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="strong"><td>{t("fields.net")}</td><td className="num">{formatMoney(calc.data.netPay)}</td></tr>
            <tr className="muted"><td>{t("fields.taxTable")}</td><td className="num">{calc.data.taxTable}</td></tr>
          </tfoot>
        </table>
      )}
    </>
  );
}
