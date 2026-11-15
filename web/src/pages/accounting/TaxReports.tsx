import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatDate, formatMoney } from "../../lib/ethiopian";
import type { VatReportDto, WithholdingReportDto, WithholdingRow } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";

/** The month's VAT (output, input, net payable) and withholding tax (deducted from suppliers, withheld by customers). */
export function TaxReports() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const [month, setMonth] = useState(currentMonth());
  const { start, end } = monthRange(month);
  const q = `companyId=${company?.id}&from=${start}&to=${end}`;
  const vat = useQuery({ queryKey: ["tax-reports", "vat", company?.id, month], queryFn: () => api<VatReportDto>(`/api/accounting/reports/vat?${q}`), enabled: !!company });
  const wht = useQuery({ queryKey: ["tax-reports", "wht", company?.id, month], queryFn: () => api<WithholdingReportDto>(`/api/accounting/reports/withholding?${q}`), enabled: !!company });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";
  const date = (iso: string) => formatDate(iso, me.calendar, am);

  const whtTable = (title: string, rows: WithholdingRow[], total: number) => (
    <section>
      <h3>{title}</h3>
      {rows.length ? (
        <table className="table">
          <thead><tr><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("fields.name")}</th><th>{t("fields.tin")}</th><th className="num">{t("inv.taxable")}</th><th className="num">{t("inv.withholding")}</th></tr></thead>
          <tbody>{rows.map((r, i) => (
            <tr key={i}><td className="mono">{r.number}</td><td>{date(r.date)}</td><td>{r.partyName}</td><td className="mono">{r.partyTin}</td>
              <td className="num">{formatMoney(r.taxableAmount)}</td><td className="num">{formatMoney(r.withheld)}</td></tr>
          ))}</tbody>
          <tfoot><tr><td colSpan={5}>{t("common.total")}</td><td className="num">{formatMoney(total)}</td></tr></tfoot>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </section>
  );

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.taxReports")}</h1>
        <label className="field-inline"><span>{t("common.month")}</span><input type="month" value={month} onChange={(e) => setMonth(e.target.value)} /></label>
      </div>
      {vat.data && (
        <section>
          <h3>{t("inv.vatReport")}</h3>
          <div className="cards">
            <div className="card"><div className="card-label">{t("inv.outputVat")}</div><div className="card-value">{formatMoney(vat.data.outputVat)}</div></div>
            <div className="card"><div className="card-label">{t("inv.inputVat")}</div><div className="card-value">{formatMoney(vat.data.inputVat)}</div></div>
            <div className="card"><div className="card-label">{t(vat.data.netPayable >= 0 ? "inv.vatPayable" : "inv.vatCredit")}</div><div className="card-value">{formatMoney(Math.abs(vat.data.netPayable))}</div></div>
          </div>
          {vat.data.rows.length ? (
            <table className="table">
              <thead><tr><th>{t("fields.type")}</th><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("fields.name")}</th><th>{t("fields.tin")}</th><th className="num">{t("inv.net")}</th><th className="num">{t("inv.vat")}</th></tr></thead>
              <tbody>{vat.data.rows.map((r, i) => (
                <tr key={i}><td>{t(`inv.kind.${r.kind}`)}</td><td className="mono">{r.number}</td><td>{date(r.date)}</td><td>{r.partyName}</td><td className="mono">{r.partyTin}</td>
                  <td className="num">{formatMoney(r.net)}</td><td className="num">{formatMoney(r.vat)}</td></tr>
              ))}</tbody>
            </table>
          ) : <p className="muted">{t("common.empty")}</p>}
        </section>
      )}
      {wht.data && (
        <>
          {whtTable(t("inv.withheldFromSuppliers"), wht.data.fromSuppliers, wht.data.totalWithheldFromSuppliers)}
          {whtTable(t("inv.withheldByCustomers"), wht.data.byCustomers, wht.data.totalWithheldByCustomers)}
        </>
      )}
    </>
  );
}
