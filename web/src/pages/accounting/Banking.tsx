import { useMemo, useState, type ChangeEvent, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { parseAmount, parseCsv, parseDate } from "../../lib/csv";
import { formatDate, formatMoney } from "../../lib/ethiopian";
import { formData } from "../../lib/hooks";
import { can, type BankAccountDto, type ReconciliationDto } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";

type Field = "date" | "description" | "reference" | "amount" | "withdrawal" | "deposit" | "balance";
const FIELDS: Field[] = ["date", "description", "reference", "amount", "withdrawal", "deposit", "balance"];
const GUESS: Record<Field, RegExp> = {
  date: /date|ቀን/i, description: /desc|narr|particular|detail|ዝርዝር/i, reference: /ref|cheque|chq/i, amount: /^amount$|ገንዘብ/i,
  withdrawal: /withdraw|debit|out/i, deposit: /deposit|credit|in$/i, balance: /bal/i,
};

/** Bank accounts, statement import from the bank's CSV, and reconciliation against the bank's ledger account. */
export function useBankAccounts() {
  const { company } = useSession();
  return useQuery({
    queryKey: ["bank-accounts", company?.id],
    queryFn: () => api<BankAccountDto[]>(`/api/accounting/bank-accounts?companyId=${company!.id}`),
    enabled: !!company,
  });
}

export function Banking() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const accounts = useBankAccounts();
  const [selected, setSelected] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [month, setMonth] = useState(currentMonth());
  const [line, setLine] = useState<string | null>(null);
  const [entries, setEntries] = useState<string[]>([]);
  const [rows, setRows] = useState<string[][] | null>(null);
  const [mapping, setMapping] = useState<Partial<Record<Field, number>>>({});
  const [notice, setNotice] = useState<string | null>(null);
  const { start, end } = monthRange(month);
  const bankId = selected ?? accounts.data?.[0]?.id ?? null;
  const manage = can(me, "accounting.banking.manage");

  const rec = useQuery({
    queryKey: ["bank-rec", bankId, month],
    queryFn: () => api<ReconciliationDto>(`/api/accounting/bank-accounts/${bankId}/reconciliation?from=${start}&to=${end}`),
    enabled: !!bankId,
  });
  const refresh = () => {
    setLine(null); setEntries([]);
    void queryClient.invalidateQueries({ queryKey: ["bank-rec"] });
    void queryClient.invalidateQueries({ queryKey: ["bank-accounts"] });
  };
  const create = useMutation({
    mutationFn: (body: Record<string, string>) => post<BankAccountDto>("/api/accounting/bank-accounts", { companyId: company!.id, ...body, ledgerAccountCode: body.ledgerAccountCode || null }),
    onSuccess: (b) => { setShowForm(false); setSelected(b.id); refresh(); },
  });
  const importLines = useMutation({
    mutationFn: (lines: object[]) => post<{ imported: number; duplicates: number }>(`/api/accounting/bank-accounts/${bankId}/statement`, { lines }),
    onSuccess: (r) => { setRows(null); setNotice(t("bank.imported", { imported: r.imported, duplicates: r.duplicates })); refresh(); },
  });
  const match = useMutation({
    mutationFn: () => post(`/api/accounting/bank-accounts/${bankId}/match`, { statementLineId: line, glEntryIds: entries }),
    onSuccess: refresh,
  });
  const unmatch = useMutation({ mutationFn: (lineId: string) => post(`/api/accounting/bank-accounts/${bankId}/unmatch/${lineId}`), onSuccess: refresh });
  const autoMatch = useMutation({
    mutationFn: () => post<number>(`/api/accounting/bank-accounts/${bankId}/auto-match?from=${start}&to=${end}`),
    onSuccess: (n) => { setNotice(t("bank.autoMatched", { count: n })); refresh(); },
  });

  const parsed = useMemo(() => {
    if (!rows || mapping.date === undefined) return [];
    const cell = (r: string[], f: Field) => (mapping[f] === undefined ? "" : r[mapping[f]!] ?? "");
    return rows.slice(1).map((r) => {
      const amount = mapping.amount !== undefined ? parseAmount(cell(r, "amount")) : parseAmount(cell(r, "deposit")) - Math.abs(parseAmount(cell(r, "withdrawal")));
      const balance = mapping.balance !== undefined && cell(r, "balance").trim() ? parseAmount(cell(r, "balance")) : null;
      return { date: parseDate(cell(r, "date")), description: cell(r, "description"), reference: cell(r, "reference") || null, amount, balance };
    }).filter((l) => l.date && Number.isFinite(l.amount) && l.amount !== 0);
  }, [rows, mapping]);

  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";
  const date = (iso: string) => formatDate(iso, me.calendar, am);
  const r = rec.data;
  const suggestedLines = new Set(r?.suggestions.map((s) => s.statementLineId));
  const suggestedEntries = new Set(r?.suggestions.map((s) => s.glEntryId));
  const lineAmount = r?.statementLines.find((l) => l.id === line)?.amount ?? 0;
  const entriesAmount = (r?.bookEntries ?? []).filter((e) => entries.includes(e.glEntryId)).reduce((a, e) => a + e.amount, 0);
  const balanced = !!line && entries.length > 0 && Math.round(lineAmount * 100) === Math.round(entriesAmount * 100);
  const error = create.error ?? importLines.error ?? match.error ?? unmatch.error ?? autoMatch.error;

  const onFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const table = parseCsv(await file.text());
    const header = table[0] ?? [];
    const guess: Partial<Record<Field, number>> = {};
    for (const f of FIELDS) {
      const i = header.findIndex((h) => GUESS[f].test(h.trim()));
      if (i >= 0 && !Object.values(guess).includes(i)) guess[f] = i;
    }
    if (guess.amount !== undefined) { delete guess.withdrawal; delete guess.deposit; }
    setMapping(guess); setRows(table); setNotice(null);
    e.target.value = "";
  };

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.banking")}</h1>
        {manage && <button onClick={() => setShowForm((s) => !s)}>{t("bank.newAccount")}</button>}
      </div>
      {showForm && (
        <form className="form-grid" onSubmit={(e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(formData(e.currentTarget)); }}>
          <label>{t("fields.name")}<input name="name" required placeholder={t("bank.namePlaceholder")} /></label>
          <label>{t("bank.bank")}<input name="bankName" required list="ethiopian-banks" /></label>
          <label>{t("bank.accountNumber")}<input name="accountNumber" required inputMode="numeric" /></label>
          <label>{t("bank.ledgerAccount")}<input name="ledgerAccountCode" placeholder={t("bank.ledgerAuto")} /></label>
          <datalist id="ethiopian-banks">
            {["Commercial Bank of Ethiopia", "Awash Bank", "Dashen Bank", "Bank of Abyssinia", "Wegagen Bank", "Hibret Bank", "Nib International Bank",
              "Cooperative Bank of Oromia", "Lion International Bank", "Zemen Bank", "Bunna Bank", "Berhan Bank", "Abay Bank", "Oromia Bank", "Enat Bank"].map((b) => <option key={b} value={b} />)}
          </datalist>
          <div className="form-actions"><button type="submit" disabled={create.isPending}>{t("common.save")}</button></div>
        </form>
      )}
      {error && <div className="error">{error.message}</div>}
      {notice && <div className="notice">{notice}</div>}

      {accounts.data?.length ? (
        <div className="cards">
          {accounts.data.map((b) => (
            <button key={b.id} className={`card card-button ${b.id === bankId ? "selected" : ""}`} onClick={() => { setSelected(b.id); setLine(null); setEntries([]); }}>
              <div className="card-label">{b.bankName}</div>
              <div className="card-value">{formatMoney(b.bookBalance)}</div>
              <div className="card-sub">{b.name} · <span className="mono">{b.accountNumber}</span> · {b.ledgerAccountCode}</div>
            </button>
          ))}
        </div>
      ) : <p className="muted">{t("bank.none")}</p>}

      {bankId && (
        <>
          <div className="inline-fields toolbar">
            <label className="field-inline"><span>{t("common.month")}</span><input type="month" value={month} onChange={(e) => { setMonth(e.target.value); setLine(null); setEntries([]); }} /></label>
            {manage && <label className="button secondary file-button">{t("bank.importCsv")}<input type="file" accept=".csv,text/csv" onChange={(e) => void onFile(e)} hidden /></label>}
            {manage && <button className="secondary" onClick={() => autoMatch.mutate()} disabled={autoMatch.isPending || !r?.suggestions.length}>{t("bank.autoMatch", { count: r?.suggestions.length ?? 0 })}</button>}
          </div>

          {rows && (
            <section className="form-grid">
              <div className="lines">
                <p className="muted small">{t("bank.mapHint", { rows: rows.length - 1 })}</p>
                <div className="inline-fields">
                  {FIELDS.map((f) => (
                    <label key={f} className="field-inline"><span>{t(`bank.field.${f}`)}</span>
                      <select value={mapping[f] ?? ""} onChange={(e) => setMapping((m) => ({ ...m, [f]: e.target.value === "" ? undefined : Number(e.target.value) }))}>
                        <option value="">—</option>
                        {(rows[0] ?? []).map((h, i) => <option key={i} value={i}>{h || `#${i + 1}`}</option>)}
                      </select>
                    </label>
                  ))}
                </div>
                {parsed.length > 0 && (
                  <table className="table inner">
                    <tbody>{parsed.slice(0, 5).map((l, i) => (
                      <tr key={i}><td>{l.date}</td><td>{l.description}</td><td className="mono">{l.reference}</td><td className={`num ${l.amount < 0 ? "neg" : ""}`}>{formatMoney(l.amount)}</td></tr>
                    ))}</tbody>
                  </table>
                )}
              </div>
              <div className="form-actions">
                <button onClick={() => importLines.mutate(parsed)} disabled={!parsed.length || importLines.isPending}>{t("bank.importLines", { count: parsed.length })}</button>
                <button className="secondary" onClick={() => setRows(null)}>{t("common.cancel")}</button>
              </div>
            </section>
          )}

          {r && (
            <>
              <div className="cards">
                <div className="card"><div className="card-label">{t("bank.bookBalance")}</div><div className="card-value">{formatMoney(r.bookBalance)}</div></div>
                <div className="card"><div className="card-label">{t("bank.notInBooks")}</div><div className="card-value">{formatMoney(r.unmatchedStatement)}</div></div>
                <div className="card"><div className="card-label">{t("bank.notOnStatement")}</div><div className="card-value">{formatMoney(r.unmatchedBook)}</div></div>
                <div className="card">
                  <div className="card-label">{t("bank.statementBalance")}</div>
                  <div className="card-value">{r.statementBalance != null ? formatMoney(r.statementBalance) : "—"}</div>
                  <div className={`card-sub ${r.difference ? "error" : "ok"}`}>
                    {r.difference == null ? t("bank.expected", { amount: formatMoney(r.expectedStatementBalance) }) : r.difference === 0 ? t("bank.reconciled") : t("bank.difference", { amount: formatMoney(r.difference) })}
                  </div>
                </div>
              </div>
              {manage && line && (
                <div className="inline-fields toolbar">
                  <span className={balanced ? "ok" : "muted"}>{t("bank.selection", { line: formatMoney(lineAmount), entries: formatMoney(entriesAmount) })}</span>
                  <button onClick={() => match.mutate()} disabled={!balanced || match.isPending}>{t("bank.match")}</button>
                  <button className="secondary" onClick={() => { setLine(null); setEntries([]); }}>{t("common.cancel")}</button>
                </div>
              )}
              <div className="split">
                <section>
                  <h3>{t("bank.statement")}</h3>
                  <table className="table">
                    <tbody>{r.statementLines.map((l) => (
                      <tr key={l.id} className={`${l.matched ? "muted" : ""} ${line === l.id ? "selected" : ""} ${suggestedLines.has(l.id) ? "suggested" : ""}`}
                        onClick={() => !l.matched && manage && setLine(line === l.id ? null : l.id)} style={{ cursor: l.matched || !manage ? "default" : "pointer" }}>
                        <td>{date(l.date)}</td><td>{l.description}{l.reference && <div className="mono small muted">{l.reference}</div>}</td>
                        <td className={`num ${l.amount < 0 ? "neg" : ""}`}>{formatMoney(l.amount)}</td>
                        <td className="actions">{l.matched
                          ? <>{l.matchedVouchers.join(", ")} {manage && <button className="link-button" onClick={(e) => { e.stopPropagation(); unmatch.mutate(l.id); }}>{t("bank.unmatch")}</button>}</>
                          : null}</td>
                      </tr>
                    ))}</tbody>
                  </table>
                  {!r.statementLines.length && <p className="muted">{t("bank.noStatement")}</p>}
                </section>
                <section>
                  <h3>{t("bank.books")}</h3>
                  <table className="table">
                    <tbody>{r.bookEntries.map((e) => (
                      <tr key={e.glEntryId} className={`${e.matched ? "muted" : ""} ${entries.includes(e.glEntryId) ? "selected" : ""} ${suggestedEntries.has(e.glEntryId) ? "suggested" : ""}`}
                        onClick={() => !e.matched && line && setEntries((s) => (s.includes(e.glEntryId) ? s.filter((x) => x !== e.glEntryId) : [...s, e.glEntryId]))}
                        style={{ cursor: e.matched || !line ? "default" : "pointer" }}>
                        <td>{date(e.date)}</td><td className="mono">{e.voucher}</td><td>{e.description}</td>
                        <td className={`num ${e.amount < 0 ? "neg" : ""}`}>{formatMoney(e.amount)}</td>
                      </tr>
                    ))}</tbody>
                  </table>
                  {!r.bookEntries.length && <p className="muted">{t("common.empty")}</p>}
                </section>
              </div>
              <p className="muted small">{t("bank.howTo")}</p>
            </>
          )}
        </>
      )}
    </>
  );
}
