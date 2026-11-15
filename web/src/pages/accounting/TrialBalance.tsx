import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatEthiopian, formatMoney, todayIso } from "../../lib/ethiopian";
import type { TrialBalanceDto } from "../../lib/types";

export function TrialBalance() {
  const { t, i18n } = useTranslation();
  const { company } = useSession();
  const [asOf, setAsOf] = useState(todayIso());
  const tb = useQuery({
    queryKey: ["trial-balance", company?.id, asOf],
    queryFn: () => api<TrialBalanceDto>(`/api/accounting/reports/trial-balance?companyId=${company!.id}&asOf=${asOf}`),
    enabled: !!company,
  });
  if (!company) return <p>{t("common.noCompany")}</p>;

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.trialBalance")}</h1>
        <label className="field-inline"><span>{t("acc.asOf")}</span><input type="date" value={asOf} onChange={(e) => setAsOf(e.target.value)} /></label>
      </div>
      <p className="muted">{company.name} · {formatEthiopian(asOf, i18n.language === "am")}</p>
      {tb.data?.rows.length ? (
        <table className="table narrow">
          <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th className="num">{t("acc.debit")}</th><th className="num">{t("acc.credit")}</th></tr></thead>
          <tbody>
            {tb.data.rows.map((r) => (
              <tr key={r.code}><td className="mono">{r.code}</td><td>{r.name}</td>
                <td className="num">{r.debit ? formatMoney(r.debit) : ""}</td><td className="num">{r.credit ? formatMoney(r.credit) : ""}</td></tr>
            ))}
          </tbody>
          <tfoot>
            <tr className={tb.data.totalDebit === tb.data.totalCredit ? "" : "error"}>
              <td colSpan={2}>{t("common.total")}</td><td className="num">{formatMoney(tb.data.totalDebit)}</td><td className="num">{formatMoney(tb.data.totalCredit)}</td>
            </tr>
          </tfoot>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </>
  );
}
