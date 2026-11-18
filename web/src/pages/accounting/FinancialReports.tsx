import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, download } from "../../lib/api";
import { formatDate, formatEthiopian, formatMoney, todayIso } from "../../lib/ethiopian";
import type { AccountDto, BalanceSheetDto, ComparativeReportDto, ComparativeRow, GeneralLedgerDto, ProfitAndLossDto, ReportRow } from "../../lib/types";
import { DynamicPage, FilterBar, FilterField, MessageStrip, ObjectStatus } from "../../ui/fiori";
import { currentMonth, monthRange } from "../payroll/month";

type Tab = "pl" | "bs" | "gl";
type Compare = "None" | "PreviousPeriod" | "LastYear" | "LastYearToDate";

const SECTION_LABEL = { Income: ["reports.income", "reports.totalIncome"], Expenses: ["reports.expenses", "reports.totalExpenses"],
  Assets: ["reports.assets", "reports.totalAssets"], Liabilities: ["reports.liabilities", "reports.totalLiabilities"], Equity: ["reports.equity", "reports.totalEquity"] } as const;

/** Profit and loss, balance sheet and general ledger. */
export function FinancialReports() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const month = monthRange(currentMonth());
  const [tab, setTab] = useState<Tab>("pl");
  const [from, setFrom] = useState(month.start);
  const [to, setTo] = useState(todayIso());
  const [account, setAccount] = useState("1120");
  const [compare, setCompare] = useState<Compare>("None");
  const comparing = compare !== "None" && tab !== "gl" && !(tab === "bs" && compare === "LastYearToDate");
  const comparePath = tab === "pl"
    ? `/api/accounting/reports/profit-and-loss/compare?companyId=${company?.id}&from=${from}&to=${to}&compare=${compare}`
    : `/api/accounting/reports/balance-sheet/compare?companyId=${company?.id}&asOf=${to}&compare=${compare}`;
  const compared = useQuery({
    queryKey: ["financial", "compare", tab, company?.id, from, to, compare],
    queryFn: () => api<ComparativeReportDto>(comparePath),
    enabled: !!company && comparing && from <= to,
  });
  const base = `companyId=${company?.id}`;
  const pl = useQuery({
    queryKey: ["financial", "pl", company?.id, from, to],
    queryFn: () => api<ProfitAndLossDto>(`/api/accounting/reports/profit-and-loss?${base}&from=${from}&to=${to}`),
    enabled: !!company && tab === "pl" && !comparing && from <= to,
  });
  const bs = useQuery({
    queryKey: ["financial", "bs", company?.id, to],
    queryFn: () => api<BalanceSheetDto>(`/api/accounting/reports/balance-sheet?${base}&asOf=${to}`),
    enabled: !!company && tab === "bs" && !comparing,
  });
  const gl = useQuery({
    queryKey: ["financial", "gl", company?.id, account, from, to],
    queryFn: () => api<GeneralLedgerDto>(`/api/accounting/reports/general-ledger?${base}&account=${account}&from=${from}&to=${to}`),
    enabled: !!company && tab === "gl" && !!account && from <= to,
  });
  const accounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company && tab === "gl",
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";

  const section = (title: string, rows: ReportRow[], total: number, totalLabel: string) => (
    <>
      <tr className="strong"><td colSpan={3}>{title}</td></tr>
      {rows.map((r) => <tr key={r.code}><td className="mono">{r.code}</td><td>{r.name}</td><td className={`num ${r.amount < 0 ? "neg" : ""}`}>{formatMoney(r.amount)}</td></tr>)}
      {!rows.length && <tr className="muted"><td /><td>{t("common.empty")}</td><td /></tr>}
      <tr className="strong"><td /><td>{totalLabel}</td><td className="num">{formatMoney(total)}</td></tr>
    </>
  );

  return (
    <DynamicPage
      title={t("nav.financialReports")}
      subtitle={<>{company.name} · {tab === "bs" ? formatEthiopian(to, am) : `${formatEthiopian(from, am)} – ${formatEthiopian(to, am)}`}
        {tab === "bs" && bs.data && <ObjectStatus tone={bs.data.balanced ? "positive" : "negative"}>{t(bs.data.balanced ? "acc.balanced" : "reports.notBalanced")}</ObjectStatus>}</>}
      actions={
        <div className="segmented" role="tablist">
          {(["pl", "bs", "gl"] as const).map((k) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? "on" : ""} onClick={() => setTab(k)}>{t(`reports.${k}`)}</button>)}
        </div>
      }
      header={
        <FilterBar>
          {tab === "gl" && (
            <FilterField label={t("reports.account")}>
              <select value={account} onChange={(e) => setAccount(e.target.value)}>
                {accounts.data?.filter((a) => !a.isGroup).map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
              </select>
            </FilterField>
          )}
          {tab !== "bs" && <FilterField label={t("periods.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>}
          <FilterField label={tab === "bs" ? t("acc.asOf") : t("periods.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
          {tab !== "gl" && (
            <FilterField label={t("reports.compareWith")}>
              <select value={compare} onChange={(e) => setCompare(e.target.value as Compare)}>
                {(["None", "PreviousPeriod", "LastYear", ...(tab === "pl" ? ["LastYearToDate"] as const : [])] as const).map((c) => <option key={c} value={c}>{t(`reports.compare.${c}`)}</option>)}
              </select>
            </FilterField>
          )}
          {comparing && compared.data && <button className="secondary" onClick={() => void download(`${comparePath}&format=csv`, `${tab === "pl" ? "profit-and-loss" : "balance-sheet"}-compared.csv`)}>CSV</button>}
        </FilterBar>
      }
    >
      <section className="table-card report-card">
      {comparing && compared.data && <ComparativeTable report={compared.data} />}

      {tab === "pl" && !comparing && pl.data && (
        <table className="table">
          <tbody>
            {section(t("reports.income"), pl.data.income, pl.data.totalIncome, t("reports.totalIncome"))}
            {section(t("reports.expenses"), pl.data.expenses, pl.data.totalExpenses, t("reports.totalExpenses"))}
          </tbody>
          <tfoot><tr className="report-total"><td colSpan={2}>{t(pl.data.netProfit >= 0 ? "reports.netProfit" : "reports.netLoss")}</td>
            <td className={`num ${pl.data.netProfit < 0 ? "neg" : ""}`}>{formatMoney(pl.data.netProfit)}</td></tr></tfoot>
        </table>
      )}

      {tab === "bs" && !comparing && bs.data && (
        <>
          <table className="table">
            <tbody>
              {section(t("reports.assets"), bs.data.assets, bs.data.totalAssets, t("reports.totalAssets"))}
              {section(t("reports.liabilities"), bs.data.liabilities, bs.data.totalLiabilities, t("reports.totalLiabilities"))}
              {section(t("reports.equity"), [...bs.data.equity, ...(bs.data.unclosedProfit ? [{ code: "", name: t("reports.unclosedProfit"), rootType: "Equity", amount: bs.data.unclosedProfit }] : [])],
                bs.data.totalEquity, t("reports.totalEquity"))}
            </tbody>
            <tfoot><tr className="report-total"><td colSpan={2}>{t("reports.liabilitiesAndEquity")}</td>
              <td className="num">{formatMoney(bs.data.totalLiabilities + bs.data.totalEquity)}</td></tr></tfoot>
          </table>
        </>
      )}

      {tab === "gl" && (
        <>
          {gl.data && (
            <table className="table">
              <thead><tr><th>{t("attendance.date")}</th><th>{t("reports.voucher")}</th><th>{t("cash.description")}</th>
                <th className="num">{t("acc.debit")}</th><th className="num">{t("acc.credit")}</th><th className="num">{t("acc.balance")}</th></tr></thead>
              <tbody>
                <tr className="muted"><td colSpan={5}>{t("reports.opening")}</td><td className="num">{formatMoney(gl.data.opening)}</td></tr>
                {gl.data.rows.map((r, i) => (
                  <tr key={i} className={r.isReversal ? "muted" : ""}>
                    <td>{formatDate(r.date, me.calendar, am)}</td><td className="mono">{r.voucher}</td><td>{r.description}</td>
                    <td className="num">{r.debit ? formatMoney(r.debit) : ""}</td><td className="num">{r.credit ? formatMoney(r.credit) : ""}</td>
                    <td className={`num ${r.balance < 0 ? "neg" : ""}`}>{formatMoney(r.balance)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot><tr><td colSpan={3}>{t("reports.closing")}</td><td className="num">{formatMoney(gl.data.totalDebit)}</td>
                <td className="num">{formatMoney(gl.data.totalCredit)}</td><td className="num">{formatMoney(gl.data.closing)}</td></tr></tfoot>
            </table>
          )}
        </>
      )}
      </section>
      {(pl.isError || bs.isError || gl.isError || compared.isError) && <MessageStrip tone="negative">{(pl.error ?? bs.error ?? gl.error ?? compared.error)?.message}</MessageStrip>}
    </DynamicPage>
  );
}

/** The report with its comparison column, the change and the change in percent; "–" where an account has nothing, "n/a" for no base. */
function ComparativeTable({ report }: { report: ComparativeReportDto }) {
  const { t, i18n } = useTranslation();
  const am = i18n.language === "am";
  const span = (from: string, to: string) => (from === to ? formatEthiopian(to, am) : `${formatEthiopian(from, am)} – ${formatEthiopian(to, am)}`);
  const money = (v?: number) => (v == null ? "–" : formatMoney(v));
  const row = (r: ComparativeRow, className = "") => (
    <tr key={`${r.code}-${r.name}`} className={className}>
      <td className="mono">{r.code}</td><td>{r.name}</td>
      <td className="num">{money(r.current)}</td><td className="num">{money(r.comparison)}</td>
      <td className={`num ${r.change < 0 ? "neg" : ""}`}>{formatMoney(r.change)}</td>
      <td className="num">{r.changePercent == null ? "n/a" : `${r.changePercent > 0 ? "+" : ""}${r.changePercent}%`}</td>
    </tr>
  );

  return (
    <>
      {report.note && <MessageStrip tone="information">{report.note}</MessageStrip>}
      <table className="table">
        <thead><tr>
          <th /><th />
          <th className="num">{span(report.currentFrom, report.currentTo)}<div className="muted small">{report.currentFrom === report.currentTo ? "" : `${report.currentFrom} – ${report.currentTo}`}</div></th>
          <th className="num">{span(report.comparisonFrom, report.comparisonTo)}<div className="muted small">{report.comparisonFrom === report.comparisonTo ? "" : `${report.comparisonFrom} – ${report.comparisonTo}`}</div></th>
          <th className="num">{t("compare.change")}</th><th className="num">%</th>
        </tr></thead>
        <tbody>
          {report.sections.map((s) => [
            <tr key={s.name} className="strong"><td colSpan={6}>{t(SECTION_LABEL[s.name][0])}</td></tr>,
            ...s.rows.map((r) => row(r)),
            row({ ...s.total, name: t(SECTION_LABEL[s.name][1]) }, "strong"),
          ])}
        </tbody>
        <tfoot>{report.summary.map((r) => row({ ...r, name: report.report === "ProfitAndLoss" ? t("reports.netProfit") : t("reports.unclosedProfit") }, "report-total"))}</tfoot>
      </table>
    </>
  );
}

