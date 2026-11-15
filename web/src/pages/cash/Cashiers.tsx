import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { formData } from "../../lib/hooks";
import { can, type CashLedgerEntryDto, type CashMovementDto, type CashierDto } from "../../lib/types";

export function Cashiers() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState<string | null>(null);
  const [mode, setMode] = useState<"Receipt" | "Payment" | "Transfer">("Receipt");
  const [showAdd, setShowAdd] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const cashiers = useQuery({
    queryKey: ["cashiers", company?.id],
    queryFn: () => api<CashierDto[]>(`/api/cash/cashiers?companyId=${company!.id}`),
    enabled: !!company,
  });
  const ledger = useQuery({
    queryKey: ["cash-ledger", selected],
    queryFn: () => api<CashLedgerEntryDto[]>(`/api/cash/cashiers/${selected}/ledger`),
    enabled: !!selected,
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ["cashiers"] });
    void queryClient.invalidateQueries({ queryKey: ["cash-ledger"] });
    void queryClient.invalidateQueries({ queryKey: ["petty-cash"] });
  };

  const add = useMutation({
    mutationFn: (b: Record<string, string>) => post<CashierDto>("/api/cash/cashiers", { ...b, companyId: company!.id, autoTopUp: b.autoTopUp === "on" }),
    onSuccess: () => { setShowAdd(false); refresh(); },
  });
  const move = useMutation({
    mutationFn: (b: Record<string, string>) =>
      mode === "Transfer"
        ? post<CashMovementDto>("/api/cash/transfers", { fromCashierId: selected, toCashierId: b.toCashierId, amount: Number(b.amount), date: b.date, description: b.description || null })
        : post<CashMovementDto>("/api/cash/transactions", { cashierId: selected, kind: mode, amount: Number(b.amount), counterAccountCode: b.account, description: b.description, date: b.date }),
    onSuccess: (r) => {
      setMessage(`${r.number} · ${t("cash.balance")}: ${formatMoney(r.balanceAfter)}` + (r.topUpRequest ? ` · ${t("cash.topUp", { amount: formatMoney(r.topUpRequest.requestedAmount) })}` : ""));
      refresh();
    },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const current = cashiers.data?.find((c) => c.id === selected);
  const onMove = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); move.mutate(formData(e.currentTarget)); };
  const onAdd = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); add.mutate(formData(e.currentTarget)); };

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.cashiers")}</h1>
        {can(me, "cash.cashiers.manage") && <button onClick={() => setShowAdd((s) => !s)}>{t("cash.addCashier")}</button>}
      </div>
      {showAdd && (
        <form className="form-grid" onSubmit={onAdd}>
          <label>{t("cash.code")}<input name="code" required maxLength={20} /></label>
          <label>{t("fields.name")}<input name="name" required /></label>
          <label>{t("fields.type")}<select name="type">{["SalesCashier", "PettyCashier", "Other"].map((x) => <option key={x} value={x}>{t(`cash.types.${x}`)}</option>)}</select></label>
          <label className="check"><input type="checkbox" name="autoTopUp" /> {t("cash.autoTopUp")}</label>
          <div className="form-actions"><button type="submit">{t("common.save")}</button>{add.isError && <span className="error">{add.error.message}</span>}</div>
        </form>
      )}
      {message && <div className="notice">{message}</div>}
      <table className="table">
        <thead><tr><th>{t("cash.code")}</th><th>{t("fields.name")}</th><th>{t("fields.type")}</th><th>{t("fields.status")}</th><th className="num">{t("cash.balance")}</th></tr></thead>
        <tbody>
          {cashiers.data?.map((c) => (
            <tr key={c.id} className={c.id === selected ? "selected" : ""} onClick={() => setSelected(c.id)} style={{ cursor: "pointer" }}>
              <td className="mono">{c.code}</td><td>{c.name}</td><td>{t(`cash.types.${c.type}`)}</td>
              <td><span className={`badge ${c.status}`}>{t(`status.${c.status}`, c.status)}</span></td>
              <td className="num strong">{formatMoney(c.balance)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {current && (
        <>
          <h2>{current.name}</h2>
          {can(me, "cash.transactions.create") && (
            <>
              <div className="segmented" role="group">
                {(["Receipt", "Payment", "Transfer"] as const).map((m) => (
                  <button key={m} className={mode === m ? "on" : ""} onClick={() => setMode(m)}>{t(`cash.${m.toLowerCase()}`)}</button>
                ))}
              </div>
              <form className="form-grid" onSubmit={onMove} key={mode}>
                <label>{t("cash.amount")}<input name="amount" type="number" min="0.01" step="0.01" required /></label>
                {mode === "Transfer" ? (
                  <label>{t("cash.to")}
                    <select name="toCashierId" required>{cashiers.data?.filter((c) => c.id !== current.id).map((c) => <option key={c.id} value={c.id}>{c.code} · {c.name}</option>)}</select>
                  </label>
                ) : (
                  <label>{t("cash.account")}<input name="account" required defaultValue={mode === "Receipt" ? "4100" : "6900"} /></label>
                )}
                <label>{t("cash.description")}<input name="description" required={mode !== "Transfer"} /></label>
                <label>{t("attendance.date")}<input name="date" type="date" defaultValue={todayIso()} required /></label>
                <div className="form-actions"><button type="submit" disabled={move.isPending}>{t(`cash.${mode.toLowerCase()}`)}</button>{move.isError && <span className="error">{move.error.message}</span>}</div>
              </form>
            </>
          )}
          <h2>{t("cash.ledger")}</h2>
          {ledger.data?.length ? (
            <table className="table">
              <thead><tr><th>{t("attendance.date")}</th><th>{t("fields.number")}</th><th>{t("fields.type")}</th><th>{t("cash.description")}</th>
                <th className="num">{t("cash.amount")}</th><th className="num">{t("cash.balance")}</th></tr></thead>
              <tbody>
                {ledger.data.map((e, i) => (
                  <tr key={i}>
                    <td>{formatDate(e.date, me.calendar, i18n.language === "am")}</td><td className="mono">{e.sourceNumber}</td><td>{e.kind}</td><td>{e.description}</td>
                    <td className={`num ${e.amount < 0 ? "neg" : ""}`}>{formatMoney(e.amount)}</td><td className="num">{formatMoney(e.balanceAfter)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : <p className="muted">{t("common.empty")}</p>}
        </>
      )}
    </>
  );
}
