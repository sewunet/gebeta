import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatEthiopian, formatMoney, todayIso } from "../../lib/ethiopian";
import type { AgeingReportDto, AgeingRow } from "../../lib/types";

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
  const cells = (r: AgeingRow) => [r.notDue, r.days1To30, r.days31To60, r.days61To90, r.over90].map((v, i) => (
    <td key={i} className={`num ${i >= 3 && v > 0 ? "neg" : ""}`}>{v ? formatMoney(v) : ""}</td>
  ));

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.ageing")}</h1>
        <div className="inline-fields">
          <div className="segmented" role="group">
            {(["Sales", "Purchase"] as const).map((k) => <button key={k} className={kind === k ? "on" : ""} onClick={() => setKind(k)}>{t(k === "Sales" ? "ageing.receivables" : "ageing.payables")}</button>)}
          </div>
          <label className="field-inline"><span>{t("acc.asOf")}</span><input type="date" value={asOf} onChange={(e) => setAsOf(e.target.value)} /></label>
        </div>
      </div>
      <p className="muted">{company.name} · {formatEthiopian(asOf, i18n.language === "am")}</p>
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
      ) : <p className="muted">{t("ageing.nothingOpen")}</p>}
      <p className="muted small">{t("ageing.daysHint")}</p>
    </>
  );
}
