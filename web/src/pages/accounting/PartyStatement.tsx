import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, download } from "../../lib/api";
import { formatDate, formatEthiopian, formatMoney, todayIso } from "../../lib/ethiopian";
import type { PartyDto, PartyStatementDto } from "../../lib/types";
import { FilterBar, FilterField, MessageStrip } from "../../ui/fiori";

type Words = { english: string; amharic: string };

/** Line types as the server sends them, to translation keys. */
const TYPE_KEY: Record<string, string> = {
  "Opening balance": "opening", Invoice: "invoice", Withholding: "withholding", "Credit note": "creditNote", "Withholding reversed": "withholdingReversed", Payment: "payment",
};

const daysBefore = (iso: string, days: number) => new Date(Date.parse(iso) - days * 86_400_000).toISOString().slice(0, 10);

/**
 * A customer's or supplier's statement of account, laid out to print: opening balance, every invoice, withholding, credit note and
 * payment with a running balance, the closing balance in figures and words, and the ageing of what is open. Ticked as a balance
 * confirmation, it adds the request to confirm the balance and a reply box for the party to sign and return (auditors ask for these).
 */
export function PartyStatement() {
  const { t, i18n } = useTranslation();
  const { company, me } = useSession();
  const { id } = useParams({ from: "/accounting/parties/$id/statement" });
  const am = i18n.language === "am";
  const today = todayIso();
  const [from, setFrom] = useState(daysBefore(today, 90));
  const [to, setTo] = useState(today);
  const [confirmation, setConfirmation] = useState(false);
  const party = useQuery({ queryKey: ["party", id], queryFn: () => api<PartyDto>(`/api/core/parties/${id}`) });
  const [kindChoice, setKind] = useState<"Sales" | "Purchase" | null>(null);
  const kind = kindChoice ?? (party.data?.type === "Supplier" ? "Purchase" : "Sales");
  const path = `/api/accounting/reports/party-statement?companyId=${company?.id}&partyId=${id}&kind=${kind}&from=${from}&to=${to}`;
  const statement = useQuery({ queryKey: ["party-statement", path], queryFn: () => api<PartyStatementDto>(path), enabled: !!company && !!party.data && from <= to });
  const closing = statement.data?.closing ?? 0;
  const words = useQuery({ queryKey: ["amount-in-words", Math.abs(closing)], queryFn: () => api<Words>(`/api/core/amount-in-words?amount=${Math.abs(closing)}`), enabled: !!statement.data });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const s = statement.data;
  const sales = kind === "Sales";
  const owedLabel = closing === 0 ? t("statement.settled") : (closing > 0) === sales ? t("statement.owesUs") : t("statement.weOwe");

  return (
    <>
      <div className="no-print">
        <div className="inline-fields">
          <Link to="/accounting/parties/$id" params={{ id }} className="back">← {party.data?.name ?? t("nav.parties")}</Link>
          <button onClick={() => window.print()} disabled={!s}>{t("me.print")}</button>
          {s && <button className="secondary" onClick={() => void download(`${path}&format=csv`, `statement-${from}-${to}.csv`)}>CSV</button>}
        </div>
        <FilterBar>
          {party.data?.type === "Both" && (
            <FilterField label={t("fields.type")}>
              <select value={kind} onChange={(e) => setKind(e.target.value as "Sales" | "Purchase")}>
                <option value="Sales">{t("ageing.receivables")}</option><option value="Purchase">{t("ageing.payables")}</option>
              </select>
            </FilterField>
          )}
          <FilterField label={t("periods.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>
          <FilterField label={t("periods.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
          <FilterField label={t("statement.printAs")}>
            <select value={confirmation ? "confirmation" : "statement"} onChange={(e) => setConfirmation(e.target.value === "confirmation")}>
              <option value="statement">{t("statement.title")}</option><option value="confirmation">{t("statement.confirmationTitle")}</option>
            </select>
          </FilterField>
        </FilterBar>
        {(statement.isError || party.isError) && <MessageStrip tone="negative">{(statement.error ?? party.error)?.message}</MessageStrip>}
      </div>

      {s && (
        <article className="receipt voucher statement">
          <header>
            <div><strong>{company.name}</strong></div>
            <div className="muted small">{t("fields.tin")} <span className="mono">{company.tin}</span></div>
            <h2>{confirmation ? t("statement.confirmationTitle") : t("statement.title")}</h2>
            <div>{formatEthiopian(s.from, am)} – {formatEthiopian(s.to, am)} <span className="muted small">({s.from} – {s.to})</span></div>
          </header>
          <section className="parties">
            <div><div className="card-label">{sales ? t("statement.customer") : t("statement.supplier")}</div><strong>{s.partyName}</strong>
              {s.partyTin && <div>{t("fields.tin")} <span className="mono">{s.partyTin}</span></div>}</div>
          </section>
          <table className="table">
            <thead><tr>
              <th>{t("attendance.date")}</th><th>{t("statement.document")}</th><th>{t("statement.reference")}</th>
              <th className="num">{t("acc.debit")}</th><th className="num">{t("acc.credit")}</th><th className="num">{t("statement.balance")}</th>
            </tr></thead>
            <tbody>
              <tr className="muted"><td>{formatDate(s.from, "Ethiopian", am)}</td><td colSpan={4}>{t("statement.type.opening")}</td><td className="num">{formatMoney(s.opening)}</td></tr>
              {s.lines.map((l, i) => (
                <tr key={i}>
                  <td>{formatDate(l.date, "Ethiopian", am)}</td>
                  <td>{t(`statement.type.${TYPE_KEY[l.type] ?? "invoice"}`)} <span className="mono">{l.number}</span></td>
                  <td className="small">{l.reference}{l.dueDate && <span className="muted"> · {t("statement.due")} {formatDate(l.dueDate, "Ethiopian", am)}</span>}</td>
                  <td className="num">{l.debit ? formatMoney(l.debit) : ""}</td><td className="num">{l.credit ? formatMoney(l.credit) : ""}</td>
                  <td className="num">{formatMoney(l.balance)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr className="report-total">
              <td colSpan={3}>{t("statement.closing")}</td><td className="num">{formatMoney(s.totalDebit)}</td><td className="num">{formatMoney(s.totalCredit)}</td>
              <td className="num">{formatMoney(s.closing)}</td>
            </tr></tfoot>
          </table>
          <div className="in-words">
            <div><strong>{owedLabel}</strong></div>
            <div><span className="card-label">{t("voucher.inWords")}</span> {words.data?.amharic ?? "…"}</div>
            <div>{words.data?.english ?? "…"}</div>
          </div>
          <table className="table">
            <thead><tr><th className="num">{t("ageing.notDue")}</th><th className="num">1–30</th><th className="num">31–60</th><th className="num">61–90</th>
              <th className="num">{t("ageing.over90")}</th><th className="num">{t("common.total")}</th></tr></thead>
            <tbody><tr>{[s.ageing.notDue, s.ageing.days1To30, s.ageing.days31To60, s.ageing.days61To90, s.ageing.over90, s.ageing.total]
              .map((v, i) => <td key={i} className="num">{formatMoney(v)}</td>)}</tr></tbody>
          </table>
          {confirmation ? (
            <>
              <p className="small">{t("statement.confirmationText", { date: formatEthiopian(s.to, am), amount: formatMoney(Math.abs(s.closing)) })}</p>
              <footer className="signatures four">
                {(["prepared", "approved"] as const).map((k) => <div key={k}>{t(`voucher.${k}`)}<span /></div>)}
              </footer>
              <section className="confirmation-reply">
                <div className="card-label">{t("statement.reply")}</div>
                <p className="small">☐ {t("statement.agree")}<br />☐ {t("statement.disagree")}</p>
                <footer className="signatures four">
                  {(["replyName", "replySignature", "replyStamp", "replyDate"] as const).map((k) => <div key={k}>{t(`statement.${k}`)}<span /></div>)}
                </footer>
              </section>
            </>
          ) : (
            <p className="muted small">{t("statement.queries")}</p>
          )}
        </article>
      )}
      <p className="no-print muted small">{t("receipt.printedFor", { user: me.fullName ?? me.username })}</p>
    </>
  );
}
