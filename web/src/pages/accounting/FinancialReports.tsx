import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatDate, formatEthiopian, formatMoney, todayIso } from "../../lib/ethiopian";
import type { AccountDto, BalanceSheetDto, GeneralLedgerDto, ProfitAndLossDto, ReportRow } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";

type Tab = "pl" | "bs" | "gl";

/** Profit and loss, balance sheet and general ledger. */
export function FinancialReports() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const month = monthRange(currentMonth());
  const [tab, setTab] = useState<Tab>("pl");
  const [from, setFrom] = useState(month.start);
  const [to, setTo] = useState(todayIso());
  const [account, setAccount] = useState("1120");
  const base = `companyId=${company?.id}`;
  const pl = useQuery({
    queryKey: ["financial", "pl", company?.id, from, to],
    queryFn: () => api<ProfitAndLossDto>(`/api/accounting/reports/profit-and-loss?${base}&from=${from}&to=${to}`),
    enabled: !!company && tab === "pl" && from <= to,
  });
  const bs = useQuery({
    queryKey: ["financial", "bs", company?.id, to],
    queryFn: () => api<BalanceSheetDto>(`/api/accounting/reports/balance-sheet?${base}&asOf=${to}`),
    enabled: !!company && tab === "bs",
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
    <>
      <div className="page-head">
        <h1>{t("nav.financialReports")}</h1>
        <div className="inline-fields">
          {tab !== "bs" && <label className="field-inline"><span>{t("periods.from")}</span><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>}
          <label className="field-inline"><span>{tab === "bs" ? t("acc.asOf") : t("periods.to")}</span><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        </div>
      </div>
      <div className="segmented" role="tablist">
        {(["pl", "bs", "gl"] as const).map((k) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? "on" : ""} onClick={() => setTab(k)}>{t(`reports.${k}`)}</button>)}
      </div>
      <p className="muted">
        {company.name} · {tab === "bs" ? formatEthiopian(to, am) : `${formatEthiopian(from, am)} – ${formatEthiopian(to, am)}`}
      </p>

      {tab === "pl" && pl.data && (
        <table className="table narrow">
          <tbody>
            {section(t("reports.income"), pl.data.income, pl.data.totalIncome, t("reports.totalIncome"))}
            {section(t("reports.expenses"), pl.data.expenses, pl.data.totalExpenses, t("reports.totalExpenses"))}
          </tbody>
          <tfoot><tr className="report-total"><td colSpan={2}>{t(pl.data.netProfit >= 0 ? "reports.netProfit" : "reports.netLoss")}</td>
            <td className={`num ${pl.data.netProfit < 0 ? "neg" : ""}`}>{formatMoney(pl.data.netProfit)}</td></tr></tfoot>
        </table>
      )}

      {tab === "bs" && bs.data && (
        <>
          {!bs.data.balanced && <div className="error">{t("reports.notBalanced")}</div>}
          <table className="table narrow">
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
          <label className="field-inline">
            <span>{t("reports.account")}</span>
            <select value={account} onChange={(e) => setAccount(e.target.value)}>
              {accounts.data?.filter((a) => !a.isGroup).map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
            </select>
          </label>
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
      {(pl.isError || bs.isError || gl.isError) && <div className="error">{(pl.error ?? bs.error ?? gl.error)?.message}</div>}
    </>
  );
}
