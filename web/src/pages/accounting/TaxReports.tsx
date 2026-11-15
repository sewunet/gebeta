import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, download } from "../../lib/api";
import { formatDate, formatMoney } from "../../lib/ethiopian";
import type { VatReportDto, WithholdingReportDto, WithholdingRow } from "../../lib/types";
import { FilterBar, FilterField, ObjectPage } from "../../ui/fiori";
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

  const whtTable = (rows: WithholdingRow[], total: number) => rows.length ? (
    <table className="table">
      <thead><tr><th>{t("fields.number")}</th><th>{t("receipt.short")}</th><th>{t("attendance.date")}</th><th>{t("fields.name")}</th><th>{t("fields.tin")}</th><th className="num">{t("inv.taxable")}</th><th className="num">{t("inv.withholding")}</th></tr></thead>
      <tbody>{rows.map((r, i) => (
        <tr key={i}><td className="mono">{r.number}</td><td className="mono">{r.receiptNumber}</td><td>{date(r.date)}</td><td>{r.partyName}</td><td className="mono">{r.partyTin}</td>
          <td className="num">{formatMoney(r.taxableAmount)}</td><td className="num">{formatMoney(r.withheld)}</td></tr>
      ))}</tbody>
      <tfoot><tr><td colSpan={6}>{t("common.total")}</td><td className="num">{formatMoney(total)}</td></tr></tfoot>
    </table>
  ) : <div className="empty">{t("common.empty")}</div>;

  return (
    <ObjectPage
      title={t("nav.taxReports")}
      subtitle={company.name}
      filters={<FilterBar><FilterField label={t("common.month")}><input type="month" value={month} onChange={(e) => setMonth(e.target.value)} /></FilterField></FilterBar>}
      kpis={vat.data && wht.data ? [
        { label: t("inv.outputVat"), value: formatMoney(vat.data.outputVat) },
        { label: t("inv.inputVat"), value: formatMoney(vat.data.inputVat) },
        { label: t(vat.data.netPayable >= 0 ? "inv.vatPayable" : "inv.vatCredit"), value: formatMoney(Math.abs(vat.data.netPayable)), tone: vat.data.netPayable >= 0 ? undefined : "positive" },
        { label: t("inv.withheldToPay"), value: formatMoney(wht.data.totalWithheldFromSuppliers) },
      ] : []}
      actions={<>
        <button className="secondary" onClick={() => void download(`/api/accounting/reports/vat?${q}&format=csv`, `vat-${month}.csv`)}>{t("inv.vatReport")} (CSV)</button>
        <button className="secondary" onClick={() => void download(`/api/accounting/reports/withholding?${q}&format=csv`, `withholding-${month}.csv`)}>{t("inv.withholding")} (CSV)</button>
      </>}
      sections={[
        { id: "vat", title: t("inv.vatReport"), content: vat.data?.rows.length ? (
          <table className="table">
            <thead><tr><th>{t("fields.type")}</th><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("fields.name")}</th><th>{t("fields.tin")}</th><th className="num">{t("inv.net")}</th><th className="num">{t("inv.vat")}</th></tr></thead>
            <tbody>{vat.data.rows.map((r, i) => (
              <tr key={i}><td>{t(`inv.kind.${r.kind}`)}</td><td className="mono">{r.number}</td><td>{date(r.date)}</td><td>{r.partyName}</td><td className="mono">{r.partyTin}</td>
                <td className={`num ${r.net < 0 ? "neg" : ""}`}>{formatMoney(r.net)}</td><td className={`num ${r.vat < 0 ? "neg" : ""}`}>{formatMoney(r.vat)}</td></tr>
            ))}</tbody>
          </table>
        ) : <div className="empty">{t("common.empty")}</div> },
        { id: "suppliers", title: t("inv.withheldFromSuppliers"), content: wht.data ? whtTable(wht.data.fromSuppliers, wht.data.totalWithheldFromSuppliers) : null },
        { id: "customers", title: t("inv.withheldByCustomers"), content: wht.data ? whtTable(wht.data.byCustomers, wht.data.totalWithheldByCustomers) : null },
      ]}
    />
  );
}
