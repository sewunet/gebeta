import { useMemo, useState, type ChangeEvent, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { parseAmount, parseCsv, parseDate } from "../../lib/csv";
import { formatDate, formatMoney } from "../../lib/ethiopian";
import { formData } from "../../lib/hooks";
import { can, type AccountDto, type BankAccountDto, type ReconciliationDto } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";
import { Dialog, DynamicPage, FilterBar, FilterField, MessageStrip, ObjectStatus } from "../../ui/fiori";

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
  const [booking, setBooking] = useState<string | null>(null);
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
    onSuccess: (b) => { setShowForm(false); setSelected(b.id); setNotice(t("bank.accountSaved", { name: `${b.bankName} · ${b.name}` })); refresh(); },
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
  const chartAccounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company && !!booking,
  });
  const book = useMutation({
    mutationFn: ({ lineId, accountCode, remark }: { lineId: string; accountCode: string; remark: string }) =>
      post(`/api/accounting/bank-accounts/${bankId}/lines/${lineId}/post`, { accountCode, remark: remark || null }),
    onSuccess: () => { setBooking(null); refresh(); void queryClient.invalidateQueries({ queryKey: ["journals"] }); },
  });
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
  const error = importLines.error ?? match.error ?? unmatch.error ?? autoMatch.error ?? book.error;

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

  const current = accounts.data?.find((b) => b.id === bankId);
  const recTone = !r ? "neutral" : r.difference == null ? "information" : r.difference === 0 ? "positive" : "negative";

  return (
    <DynamicPage
      title={t("nav.banking")}
      subtitle={current ? <>{current.bankName} · <span className="mono">{current.accountNumber}</span> · {current.ledgerAccountCode}
        {r && <ObjectStatus tone={recTone}>{r.difference == null ? t("bank.expected", { amount: formatMoney(r.expectedStatementBalance) })
          : r.difference === 0 ? t("bank.reconciled") : t("bank.difference", { amount: formatMoney(r.difference) })}</ObjectStatus>}</> : undefined}
      actions={manage && <>
        {bankId && <label className="button file-button">{t("bank.importCsv")}<input type="file" accept=".csv,text/csv" onChange={(e) => void onFile(e)} hidden /></label>}
        {bankId && <button className="secondary" onClick={() => autoMatch.mutate()} disabled={autoMatch.isPending || !r?.suggestions.length}>{t("bank.autoMatch", { count: r?.suggestions.length ?? 0 })}</button>}
        <button className="secondary" onClick={() => setShowForm(true)}>{t("bank.newAccount")}</button>
      </>}
      header={accounts.data?.length ? <>
        <FilterBar>
          <FilterField label={t("bank.bank")}>
            <select value={bankId ?? ""} onChange={(e) => { setSelected(e.target.value); setLine(null); setEntries([]); }}>
              {accounts.data.map((b) => <option key={b.id} value={b.id}>{b.bankName} · {b.name} · {formatMoney(b.bookBalance)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("common.month")}><input type="month" value={month} onChange={(e) => { setMonth(e.target.value); setLine(null); setEntries([]); }} /></FilterField>
        </FilterBar>
        {r && (
          <div className="kpis kpis-gap">
            <div className="kpi"><div className="kpi-label">{t("bank.bookBalance")}</div><div className="kpi-value">{formatMoney(r.bookBalance)}</div></div>
            <div className="kpi"><div className="kpi-label">{t("bank.notInBooks")}</div><div className="kpi-value">{formatMoney(r.unmatchedStatement)}</div></div>
            <div className="kpi"><div className="kpi-label">{t("bank.notOnStatement")}</div><div className="kpi-value">{formatMoney(r.unmatchedBook)}</div></div>
            <div className="kpi"><div className="kpi-label">{t("bank.statementBalance")}</div>
              <div className={`kpi-value ${r.difference ? "negative" : ""}`}>{r.statementBalance != null ? formatMoney(r.statementBalance) : "—"}</div></div>
          </div>
        )}
      </> : undefined}
    >
      {error && <MessageStrip tone="negative">{error.message}</MessageStrip>}
      {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
      {!accounts.data?.length && !accounts.isPending && <div className="empty">{t("bank.none")}</div>}
          {rows && (
            <section className="object-section">
              <h2>{t("bank.importCsv")}</h2>
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
              <div className="inline-fields toolbar">
                <button onClick={() => importLines.mutate(parsed)} disabled={!parsed.length || importLines.isPending}>{t("bank.importLines", { count: parsed.length })}</button>
                <button className="secondary" onClick={() => setRows(null)}>{t("common.cancel")}</button>
              </div>
            </section>
          )}

          {r && (
            <>
              {manage && line && (
                <div className="inline-fields toolbar">
                  <span className={balanced ? "ok" : "muted"}>{t("bank.selection", { line: formatMoney(lineAmount), entries: formatMoney(entriesAmount) })}</span>
                  <button onClick={() => match.mutate()} disabled={!balanced || match.isPending}>{t("bank.match")}</button>
                  <button className="secondary" onClick={() => { setLine(null); setEntries([]); }}>{t("common.cancel")}</button>
                </div>
              )}
              <div className="split">
                <section className="table-card">
                  <div className="table-toolbar"><h2>{t("bank.statement")}<span className="count"> ({r.statementLines.length})</span></h2></div>
                  <table className="table">
                    <tbody>{r.statementLines.flatMap((l) => [
                      <tr key={l.id} className={`${l.matched ? "muted" : ""} ${line === l.id ? "selected" : ""} ${suggestedLines.has(l.id) ? "suggested" : ""}`}
                        onClick={() => !l.matched && manage && setLine(line === l.id ? null : l.id)} style={{ cursor: l.matched || !manage ? "default" : "pointer" }}>
                        <td>{date(l.date)}</td><td>{l.description}{l.reference && <div className="mono small muted">{l.reference}</div>}</td>
                        <td className={`num ${l.amount < 0 ? "neg" : ""}`}>{formatMoney(l.amount)}</td>
                        <td className="actions">{l.matched
                          ? <>{l.matchedVouchers.join(", ")} {manage && <button className="link-button" onClick={(e) => { e.stopPropagation(); unmatch.mutate(l.id); }}>{t("bank.unmatch")}</button>}</>
                          : manage && <button className="link-button" onClick={(e) => { e.stopPropagation(); setBooking(booking === l.id ? null : l.id); }}>{t("bank.book")}</button>}</td>
                      </tr>,
                      booking === l.id ? (
                        <tr key={`${l.id}-book`} className="detail"><td colSpan={4}>
                          <form className="inline-fields" onSubmit={(e) => {
                            e.preventDefault();
                            const f = new FormData(e.currentTarget);
                            book.mutate({ lineId: l.id, accountCode: String(f.get("accountCode")), remark: String(f.get("remark") ?? "") });
                          }}>
                            <select name="accountCode" defaultValue={l.amount < 0 ? "6400" : "4300"}>
                              {chartAccounts.data?.filter((a) => !a.isGroup && a.code !== accounts.data?.find((b) => b.id === bankId)?.ledgerAccountCode)
                                .map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
                            </select>
                            <input name="remark" placeholder={l.description} />
                            <button type="submit" disabled={book.isPending}>{t("bank.bookButton")}</button>
                          </form>
                          <p className="muted small">{t("bank.bookHint")}</p>
                        </td></tr>
                      ) : null,
                    ])}</tbody>
                  </table>
                  {!r.statementLines.length && <div className="empty">{t("bank.noStatement")}</div>}
                </section>
                <section className="table-card">
                  <div className="table-toolbar"><h2>{t("bank.books")}<span className="count"> ({r.bookEntries.length})</span></h2></div>
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
                  {!r.bookEntries.length && <div className="empty">{t("common.empty")}</div>}
                </section>
              </div>
              <p className="muted small">{t("bank.howTo")}</p>
            </>
          )}

      <Dialog open={showForm} title={t("bank.newAccount")} onClose={() => setShowForm(false)}
        footer={<><button type="submit" form="bank-form" disabled={create.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setShowForm(false)}>{t("common.cancel")}</button></>}>
        <form id="bank-form" className="dialog-form" onSubmit={(e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(formData(e.currentTarget)); }}>
          <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" required placeholder={t("bank.namePlaceholder")} /></label>
          <label>{t("bank.bank")}<span className="required" aria-hidden>*</span><input name="bankName" required list="ethiopian-banks" /></label>
          <label>{t("bank.accountNumber")}<span className="required" aria-hidden>*</span><input name="accountNumber" required inputMode="numeric" /></label>
          <label>{t("bank.ledgerAccount")}<input name="ledgerAccountCode" placeholder={t("bank.ledgerAuto")} /></label>
          <datalist id="ethiopian-banks">
            {["Commercial Bank of Ethiopia", "Awash Bank", "Dashen Bank", "Bank of Abyssinia", "Wegagen Bank", "Hibret Bank", "Nib International Bank",
              "Cooperative Bank of Oromia", "Lion International Bank", "Zemen Bank", "Bunna Bank", "Berhan Bank", "Abay Bank", "Oromia Bank", "Enat Bank"].map((b) => <option key={b} value={b} />)}
          </datalist>
          {create.isError && <MessageStrip tone="negative">{create.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </DynamicPage>
  );
}
