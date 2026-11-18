import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import type { SalarySlipDto } from "../../lib/types";
import { ListReport, MessageStrip } from "../../ui/fiori";

/** My payslips: every submitted slip of the signed-in employee, latest first. */
export function MyPayslips() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const slips = useQuery({ queryKey: ["my-payslips"], queryFn: () => api<SalarySlipDto[]>("/api/payroll/my/salary-slips"), retry: false });
  if (slips.isError) return <MessageStrip tone="information">{slips.error.message}</MessageStrip>;
  const rows = slips.data ?? [];

  return (
    <ListReport title={t("nav.myPayslips")} tableTitle={t("nav.myPayslips")} count={rows.length}>
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("me.period")}</th><th className="num">{t("fields.gross")}</th><th className="num">{t("fields.deductions")}</th><th className="num">{t("fields.net")}</th></tr></thead>
          <tbody>
            {rows.map((s) => (
              <tr key={s.id} className="navigable" onClick={() => void navigate({ to: "/me/payslips/$id", params: { id: s.id } })}>
                <td data-label={t("me.period")}><span className="strong-link">{s.ethiopianPeriod}</span> <span className="muted small">{s.periodStart} – {s.periodEnd}</span></td>
                <td data-label={t("fields.gross")} className="num">{formatMoney(s.grossPay)}</td>
                <td data-label={t("fields.deductions")} className="num">{formatMoney(s.totalDeductions)}</td>
                <td data-label={t("fields.net")} className="num strong">{formatMoney(s.netPay)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{slips.isPending ? t("common.loading") : t("me.noPayslips")}</div>}
    </ListReport>
  );
}

/** One payslip, laid out to print (the menu and top bar are hidden when printing). */
export function MyPayslip() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const { id } = useParams({ from: "/me/payslips/$id" });
  const slip = useQuery({ queryKey: ["my-payslip", id], queryFn: () => api<SalarySlipDto>(`/api/payroll/my/salary-slips/${id}`), retry: false });
  if (slip.isError) return <MessageStrip tone="negative">{slip.error.message}</MessageStrip>;
  if (!slip.data) return <p className="muted">{t("common.loading")}</p>;
  const s = slip.data;
  const lines = (type: string) => s.lines.filter((l) => l.type === type);

  return (
    <>
      <div className="no-print inline-fields">
        <Link to="/me/payslips" className="back">← {t("nav.myPayslips")}</Link>
        <button onClick={() => window.print()}>{t("me.print")}</button>
      </div>
      <article className="receipt payslip">
        <header>
          <h2>{t("me.payslip")}</h2>
          <div>{company?.name}</div>
          <div><strong>{s.ethiopianPeriod}</strong> <span className="muted small">({s.periodStart} – {s.periodEnd})</span></div>
          {s.number && <div className="mono small">{s.number}</div>}
        </header>
        <section className="parties">
          <div><div className="card-label">{t("me.employee")}</div><strong>{s.employeeName}</strong><div className="mono">{s.employeeNumber}</div></div>
          <div><div className="card-label">{t("fields.paymentDays")}</div><strong>{s.paymentDays} / {s.totalWorkingDays}</strong><div>{t("fields.base")} {formatMoney(s.base)}</div></div>
        </section>
        <table className="table">
          <thead><tr><th>{t("slips.earnings")}</th><th className="num">{t("fields.amount")}</th></tr></thead>
          <tbody>{lines("Earning").map((l) => <tr key={l.abbreviation}><td>{l.component}</td><td className="num">{formatMoney(l.amount)}</td></tr>)}</tbody>
          <tfoot><tr><td>{t("fields.gross")}</td><td className="num">{formatMoney(s.grossPay)}</td></tr></tfoot>
        </table>
        <table className="table">
          <thead><tr><th>{t("fields.deductions")}</th><th className="num">{t("fields.amount")}</th></tr></thead>
          <tbody>{lines("Deduction").map((l) => <tr key={l.abbreviation}><td>{l.component}</td><td className="num">{formatMoney(l.amount)}</td></tr>)}</tbody>
          <tfoot><tr><td>{t("fields.deductions")}</td><td className="num">{formatMoney(s.totalDeductions)}</td></tr></tfoot>
        </table>
        <table className="table">
          <tbody><tr className="report-total"><td>{t("fields.net")}</td><td className="num">{formatMoney(s.netPay)}</td></tr></tbody>
        </table>
        {lines("EmployerContribution").length > 0 && (
          <p className="muted small">{t("me.employerPaid", { items: lines("EmployerContribution").map((l) => `${l.component} ${formatMoney(l.amount)}`).join(", ") })}</p>
        )}
        {s.isHistory && <p className="muted small">{t("slips.history")}</p>}
      </article>
      <p className="no-print muted small">{t("receipt.printedFor", { user: me.fullName ?? me.username })}</p>
    </>
  );
}
