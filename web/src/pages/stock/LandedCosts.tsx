import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type LandedCostDto, type StockEntryDto } from "../../lib/types";
import { MessageStrip, ObjectStatus, statusTone } from "../../ui/fiori";
import { useSession } from "../../app/context";

const BASES = ["Value", "Quantity", "Weight", "Volume"] as const;
type ChargeRow = { description: string; amount: string; basis: (typeof BASES)[number]; accountCode: string };
const emptyCharge = (): ChargeRow => ({ description: "", amount: "", basis: "Value", accountCode: "" });
const refreshKeys = ["stock-entries", "stock-entry", "stock-balance", "stock-ledger", "landed-costs", "history", "related"];

/** The landed costs (freight, duty, clearing, LC charges) of a posted receipt, with a form to add one. They are shared across the receipt's lines by value, quantity, weight or volume. */
export function LandedCosts({ receipt }: { receipt: StockEntryDto }) {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const am = i18n.language === "am";
  const [adding, setAdding] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [date, setDate] = useState(todayIso());
  const [remark, setRemark] = useState("");
  const [charges, setCharges] = useState<ChargeRow[]>([emptyCharge()]);
  const [sizes, setSizes] = useState<{ weight: string; volume: string }[]>(() => receipt.lines.map(() => ({ weight: "", volume: "" })));
  const costs = useQuery({ queryKey: ["landed-costs", receipt.id], queryFn: () => api<LandedCostDto[]>(`/api/stock/landed-costs?receiptId=${receipt.id}`) });
  const refresh = () => { for (const k of refreshKeys) void queryClient.invalidateQueries({ queryKey: [k] }); };
  const create = useMutation({
    mutationFn: () => post<LandedCostDto>("/api/stock/landed-costs", {
      receiptId: receipt.id, postingDate: date, remark: remark || undefined,
      charges: charges.filter((c) => c.description.trim() || c.amount).map((c) => ({ description: c.description, amount: Number(c.amount), basis: c.basis, accountCode: c.accountCode || undefined })),
      lines: sizes.map((s, i) => ({ receiptLineIndex: i, weight: s.weight ? Number(s.weight) : undefined, volume: s.volume ? Number(s.volume) : undefined })),
    }),
    onSuccess: () => { setAdding(false); setCharges([emptyCharge()]); setRemark(""); refresh(); },
  });
  const act = useMutation({
    mutationFn: (v: { id: string; action: "submit" | "cancel" }) => post<LandedCostDto>(`/api/stock/landed-costs/${v.id}/${v.action}`),
    onSuccess: (c) => { setNotice(t(c.status === "Cancelled" ? "landed.cancelled" : "landed.posted", { number: c.number })); refresh(); },
  });
  if (costs.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (costs.isError) return <MessageStrip tone="negative">{costs.error.message}</MessageStrip>;
  const needs = (b: string) => charges.some((c) => c.basis === b);
  const setCharge = (i: number, patch: Partial<ChargeRow>) => setCharges(charges.map((c, j) => (j === i ? { ...c, ...patch } : c)));
  const setSize = (i: number, patch: Partial<{ weight: string; volume: string }>) => setSizes(sizes.map((s, j) => (j === i ? { ...s, ...patch } : s)));
  const manage = can(me, "stock.landedcosts.create");
  const submit = can(me, "stock.landedcosts.submit");

  return (
    <>
      {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
      {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
      {costs.data.length === 0 && <div className="empty">{t("landed.none")}</div>}
      {costs.data.map((c) => (
        <div key={c.id} className="object-section">
          <h3>{c.number} · {formatDate(c.postingDate, me.calendar, am)} <ObjectStatus tone={statusTone(c.status)}>{t(`status.${c.status}`)}</ObjectStatus>{c.remark && <span className="muted"> · {c.remark}</span>}</h3>
          <table className="table">
            <thead><tr><th>{t("landed.charges")}</th><th>{t("landed.basis")}</th><th>{t("landed.account")}</th><th className="num">{t("landed.amount")}</th></tr></thead>
            <tbody>{c.charges.map((x, i) => <tr key={i}><td>{x.description}</td><td>{t(`landed.bases.${x.basis}`)}</td><td className="mono">{x.accountCode}</td><td className="num">{formatMoney(x.amount)}</td></tr>)}</tbody>
            <tfoot><tr><td colSpan={3}>{t("landed.total")}</td><td className="num">{formatMoney(c.totalCharges)}</td></tr></tfoot>
          </table>
          <table className="table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("landed.received")}</th><th className="num">{t("landed.share")}</th><th className="num">{t("landed.newCost")}</th></tr></thead>
            <tbody>{c.lines.map((l) => <tr key={l.receiptLineIndex}><td><span className="mono">{l.itemCode}</span> · {l.itemName}</td><td className="num">{formatMoney(l.receivedValue)}</td>
              <td className="num">{formatMoney(l.allocated)}</td><td className="num">{formatMoney(l.receivedValue + l.allocated)}</td></tr>)}</tbody>
          </table>
          {submit && c.status === "Draft" && <button onClick={() => act.mutate({ id: c.id, action: "submit" })} disabled={act.isPending}>{t("landed.post")}</button>}
          {submit && c.status === "Submitted" && <button className="secondary" onClick={() => act.mutate({ id: c.id, action: "cancel" })} disabled={act.isPending}>{t("landed.cancel")}</button>}
        </div>
      ))}
      {manage && !adding && <button className="secondary" onClick={() => setAdding(true)}>{t("landed.add")}</button>}
      {manage && adding && (
        <form onSubmit={(e) => { e.preventDefault(); create.mutate(); }}>
          <div className="form-columns">
            <label>{t("attendance.date")}<input type="date" value={date} min={receipt.postingDate} max={todayIso()} onChange={(e) => setDate(e.target.value)} required /></label>
            <label>{t("acc.remark")}<input value={remark} onChange={(e) => setRemark(e.target.value)} /></label>
          </div>
          <table className="table edit-table">
            <thead><tr><th>{t("landed.description")}</th><th className="num">{t("landed.amount")}</th><th>{t("landed.basis")}</th><th>{t("landed.account")}</th><th /></tr></thead>
            <tbody>{charges.map((c, i) => (
              <tr key={i}>
                <td><input aria-label={t("landed.description")} value={c.description} onChange={(e) => setCharge(i, { description: e.target.value })} required={i === 0} /></td>
                <td><input aria-label={t("landed.amount")} className="num" type="number" step="0.01" min="0.01" value={c.amount} onChange={(e) => setCharge(i, { amount: e.target.value })} required={i === 0} /></td>
                <td><select aria-label={t("landed.basis")} value={c.basis} onChange={(e) => setCharge(i, { basis: e.target.value as ChargeRow["basis"] })}>
                  {BASES.map((b) => <option key={b} value={b}>{t(`landed.bases.${b}`)}</option>)}</select></td>
                <td><input aria-label={t("landed.account")} value={c.accountCode} placeholder={t("landed.accountHint")} onChange={(e) => setCharge(i, { accountCode: e.target.value })} /></td>
                <td>{charges.length > 1 && <button type="button" className="link-button" aria-label={t("ui.remove")} onClick={() => setCharges(charges.filter((_, j) => j !== i))}>×</button>}</td>
              </tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={5}><button type="button" className="secondary" onClick={() => setCharges([...charges, emptyCharge()])}>{t("landed.addCharge")}</button></td></tr></tfoot>
          </table>
          {(needs("Weight") || needs("Volume")) && (
            <table className="table edit-table">
              <thead><tr><th>{t("nav.items")}</th>{needs("Weight") && <th className="num">{t("landed.weight")}</th>}{needs("Volume") && <th className="num">{t("landed.volume")}</th>}</tr></thead>
              <tbody>{receipt.lines.map((l, i) => (
                <tr key={i}><td><span className="mono">{l.itemCode}</span> · {l.itemName}</td>
                  {needs("Weight") && <td><input aria-label={t("landed.weight")} className="num" type="number" step="0.001" min="0" value={sizes[i]?.weight ?? ""} onChange={(e) => setSize(i, { weight: e.target.value })} /></td>}
                  {needs("Volume") && <td><input aria-label={t("landed.volume")} className="num" type="number" step="0.001" min="0" value={sizes[i]?.volume ?? ""} onChange={(e) => setSize(i, { volume: e.target.value })} /></td>}
                </tr>
              ))}</tbody>
            </table>
          )}
          <MessageStrip tone="information">{t("landed.hint")}{(needs("Weight") || needs("Volume")) && <> {t("landed.weightsHint")}</>}</MessageStrip>
          {create.isError && <MessageStrip tone="negative">{create.error.message}</MessageStrip>}
          <button type="submit" disabled={create.isPending}>{t("landed.save")}</button>{" "}
          <button type="button" className="secondary" onClick={() => setAdding(false)}>{t("common.cancel")}</button>
        </form>
      )}
    </>
  );
}
