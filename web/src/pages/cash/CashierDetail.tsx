import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { formData } from "../../lib/hooks";
import { can, type CashLedgerEntryDto, type CashMovementDto } from "../../lib/types";
import { Dialog, Fields, MessageStrip, ObjectPage, ObjectStatus, statusTone } from "../../ui/fiori";
import { useCashiers } from "../../lib/queries";

type Mode = "Receipt" | "Payment" | "Transfer";

/** Object page of one cashier: balance in the header, receipt, payment and transfer dialogs, and the cash ledger. */
export function CashierDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const { id } = useParams({ from: "/cash/cashiers/$id" });
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<Mode | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const cashiers = useCashiers();
  const ledger = useQuery({ queryKey: ["cash-ledger", id], queryFn: () => api<CashLedgerEntryDto[]>(`/api/cash/cashiers/${id}/ledger`) });
  const move = useMutation({
    mutationFn: ({ m, b }: { m: Mode; b: Record<string, string> }) =>
      m === "Transfer"
        ? post<CashMovementDto>("/api/cash/transfers", { fromCashierId: id, toCashierId: b.toCashierId, amount: Number(b.amount), date: b.date, description: b.description || null })
        : post<CashMovementDto>("/api/cash/transactions", { cashierId: id, kind: m, amount: Number(b.amount), counterAccountCode: b.account, description: b.description, date: b.date }),
    onSuccess: (r) => {
      setMode(null);
      setNotice(`${r.number} · ${t("cash.balance")}: ${formatMoney(r.balanceAfter)}` + (r.topUpRequest ? ` · ${t("cash.topUp", { amount: formatMoney(r.topUpRequest.requestedAmount) })}` : ""));
      for (const key of ["cashiers", "cash-ledger", "petty-cash"]) void queryClient.invalidateQueries({ queryKey: [key] });
    },
  });
  const c = cashiers.data?.find((x) => x.id === id);
  if (cashiers.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (!c) return <MessageStrip tone="negative">{t("cash.notFound")}</MessageStrip>;
  const am = i18n.language === "am";
  const entries = ledger.data ?? [];
  const inflow = entries.filter((e) => e.amount > 0).reduce((a, e) => a + e.amount, 0);
  const outflow = entries.filter((e) => e.amount < 0).reduce((a, e) => a - e.amount, 0);
  const onMove = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); if (mode) move.mutate({ m: mode, b: formData(e.currentTarget) }); };
  const canMove = can(me, "cash.transactions.create");

  return (
    <>
      <ObjectPage
        breadcrumb={<Link to="/cash/cashiers">{t("nav.cashiers")}</Link>}
        title={c.name}
        subtitle={<><span className="mono">{c.code}</span> · {t(`cash.types.${c.type}`)}</>}
        status={<ObjectStatus tone={statusTone(c.status)}>{t(`status.${c.status}`, c.status)}</ObjectStatus>}
        kpis={[
          { label: t("cash.balance"), value: formatMoney(c.balance), tone: c.balance < 0 ? "negative" : undefined },
          { label: t("cash.in"), value: formatMoney(inflow) },
          { label: t("cash.out"), value: formatMoney(outflow) },
          { label: t("cash.cashAccount"), value: <span className="mono">{c.cashAccountCode}</span> },
        ]}
        actions={canMove && <>
          <button onClick={() => setMode("Receipt")}>{t("cash.receipt")}</button>
          <button className="secondary" onClick={() => setMode("Payment")}>{t("cash.payment")}</button>
          <button className="secondary" onClick={() => setMode("Transfer")}>{t("cash.transfer")}</button>
        </>}
        messages={notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        sections={[
          { id: "ledger", title: t("cash.ledger"), content: entries.length ? (
            <table className="table">
              <thead><tr><th>{t("attendance.date")}</th><th>{t("fields.number")}</th><th>{t("fields.type")}</th><th>{t("cash.description")}</th>
                <th className="num">{t("cash.amount")}</th><th className="num">{t("cash.balance")}</th></tr></thead>
              <tbody>{entries.map((e, i) => (
                <tr key={i}>
                  <td>{formatDate(e.date, me.calendar, am)}</td>
                  <td className="mono"><Link to="/cash/cashiers/$id/voucher/$number" params={{ id, number: e.sourceNumber }} title={t("voucher.print")}>{e.sourceNumber}</Link></td><td>{e.kind}</td><td>{e.description}</td>
                  <td className={`num ${e.amount < 0 ? "neg" : ""}`}>{formatMoney(e.amount)}</td><td className="num">{formatMoney(e.balanceAfter)}</td>
                </tr>
              ))}</tbody>
            </table>
          ) : <div className="empty">{t("common.empty")}</div> },
          { id: "general", title: t("ui.general"), content: (
            <Fields items={[
              { label: t("cash.code"), value: <span className="mono">{c.code}</span> },
              { label: t("fields.type"), value: t(`cash.types.${c.type}`) },
              { label: t("cash.cashAccount"), value: <span className="mono">{c.cashAccountCode}</span> },
            ]} />
          ) },
        ]}
      />
      <Dialog open={!!mode} title={mode ? t(`cash.${mode.toLowerCase()}`) : ""} onClose={() => setMode(null)}
        footer={<><button type="submit" form="cash-move" disabled={move.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setMode(null)}>{t("common.cancel")}</button></>}>
        <form id="cash-move" key={mode} className="dialog-form" onSubmit={onMove}>
          <label>{t("cash.amount")}<span className="required" aria-hidden>*</span><input name="amount" type="number" min="0.01" step="0.01" required /></label>
          {mode === "Transfer" ? (
            <label>{t("cash.to")}
              <select name="toCashierId" required>{cashiers.data?.filter((x) => x.id !== c.id).map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</select>
            </label>
          ) : (
            <label>{t("cash.account")}<input name="account" required defaultValue={mode === "Receipt" ? "4100" : "6900"} /></label>
          )}
          <label>{t("cash.description")}<input name="description" required={mode !== "Transfer"} /></label>
          <label>{t("attendance.date")}<input name="date" type="date" defaultValue={todayIso()} required /></label>
          {move.isError && <MessageStrip tone="negative">{move.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </>
  );
}
