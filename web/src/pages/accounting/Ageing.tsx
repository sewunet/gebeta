import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatEthiopian, formatMoney, todayIso } from "../../lib/ethiopian";
import type { AgeingReportDto, AgeingRow } from "../../lib/types";
import { DynamicPage, FilterBar, FilterField } from "../../ui/fiori";

/** What customers owe and what is owed to suppliers, by days past the due date. */
export function Ageing() {
  const { t, i18n } = useTranslation();
  const { company } = useSession();
  const [kind, setKind] = useState<"Sales" | "Purchase">("Sales");
  const [asOf, setAsOf] = useState(todayIso());
  const report = useQuery({
    queryKey: ["invoices", "ageing", company?.id, kind, asOf],
    queryFn: () => api<AgeingReportDto>(`/api/accounting/reports/ageing?companyId=${company!.id}&kind=${kind}&asOf=${asOf}`),
    enabled: !!company,
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const tot = report.data?.totals;
  const overdue = tot ? tot.days1To30 + tot.days31To60 + tot.days61To90 + tot.over90 : 0;
  const cells = (r: AgeingRow) => [r.notDue, r.days1To30, r.days31To60, r.days61To90, r.over90].map((v, i) => (
    <td key={i} className={`num ${i >= 3 && v > 0 ? "neg" : ""}`}>{v ? formatMoney(v) : ""}</td>
  ));

  return (
    <DynamicPage
      title={t("nav.ageing")}
      subtitle={<>{company.name} · {formatEthiopian(asOf, i18n.language === "am")}</>}
      actions={
        <div className="segmented" role="tablist">
          {(["Sales", "Purchase"] as const).map((k) => <button key={k} role="tab" aria-selected={kind === k} className={kind === k ? "on" : ""} onClick={() => setKind(k)}>{t(k === "Sales" ? "ageing.receivables" : "ageing.payables")}</button>)}
        </div>
      }
      header={<>
        <FilterBar><FilterField label={t("acc.asOf")}><input type="date" value={asOf} onChange={(e) => setAsOf(e.target.value)} /></FilterField></FilterBar>
        {tot && (
          <div className="kpis" style={{ marginTop: 12 }}>
            <div className="kpi"><div className="kpi-label">{t("common.total")}</div><div className="kpi-value">{formatMoney(tot.total)}</div></div>
            <div className="kpi"><div className="kpi-label">{t("ageing.notDue")}</div><div className="kpi-value">{formatMoney(tot.notDue)}</div></div>
            <div className="kpi"><div className="kpi-label">{t("inv.overdue")}</div><div className={`kpi-value ${overdue ? "negative" : ""}`}>{formatMoney(overdue)}</div></div>
          </div>
        )}
      </>}
    >
      <section className="table-card">
        <div className="table-toolbar"><h2>{t(kind === "Sales" ? "ageing.receivables" : "ageing.payables")}<span className="count"> ({report.data?.rows.length ?? 0})</span></h2></div>
        {report.data?.rows.length ? (
          <table className="table">
            <thead><tr><th>{t(kind === "Sales" ? "inv.customer" : "inv.supplier")}</th><th>{t("fields.tin")}</th>
              <th className="num">{t("ageing.notDue")}</th><th className="num">1–30</th><th className="num">31–60</th><th className="num">61–90</th>
              <th className="num">{t("ageing.over90")}</th><th className="num">{t("common.total")}</th></tr></thead>
            <tbody>
              {report.data.rows.map((r) => (
                <tr key={r.partyId}><td>{r.partyName}</td><td className="mono">{r.partyTin}</td>{cells(r)}<td className="num strong">{formatMoney(r.total)}</td></tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={2}>{t("common.total")}</td>{cells(report.data.totals)}<td className="num">{formatMoney(report.data.totals.total)}</td></tr></tfoot>
          </table>
        ) : <div className="empty">{t("ageing.nothingOpen")}</div>}
      </section>
      <p className="muted small">{t("ageing.daysHint")}</p>
    </DynamicPage>
  );
}
