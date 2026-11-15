import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatEthiopian, formatMoney, todayIso } from "../../lib/ethiopian";
import type { TrialBalanceDto } from "../../lib/types";
import { DynamicPage, FilterBar, FilterField, ObjectStatus } from "../../ui/fiori";

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
  const balanced = tb.data ? tb.data.totalDebit === tb.data.totalCredit : true;

  return (
    <DynamicPage
      title={t("nav.trialBalance")}
      subtitle={<>{company.name} · {formatEthiopian(asOf, i18n.language === "am")}{tb.data && <ObjectStatus tone={balanced ? "positive" : "negative"}>{t(balanced ? "acc.balanced" : "acc.unbalanced")}</ObjectStatus>}</>}
      header={<FilterBar><FilterField label={t("acc.asOf")}><input type="date" value={asOf} onChange={(e) => setAsOf(e.target.value)} /></FilterField></FilterBar>}
    >
      <section className="table-card">
        {tb.data?.rows.length ? (
          <table className="table">
            <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th className="num">{t("acc.debit")}</th><th className="num">{t("acc.credit")}</th></tr></thead>
            <tbody>
              {tb.data.rows.map((r) => (
                <tr key={r.code}><td className="mono">{r.code}</td><td>{r.name}</td>
                  <td className="num">{r.debit ? formatMoney(r.debit) : ""}</td><td className="num">{r.credit ? formatMoney(r.credit) : ""}</td></tr>
              ))}
            </tbody>
            <tfoot>
              <tr><td colSpan={2}>{t("common.total")}</td><td className="num">{formatMoney(tb.data.totalDebit)}</td><td className="num">{formatMoney(tb.data.totalCredit)}</td></tr>
            </tfoot>
          </table>
        ) : <div className="empty">{tb.isPending ? t("common.loading") : t("common.empty")}</div>}
      </section>
    </DynamicPage>
  );
}
