import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import { can, type CashierDto, type PettyCashRequestDto } from "../../lib/types";

export function PettyCash() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [funding, setFunding] = useState<Record<string, { from: string; account: string; cashier: string }>>({});
  const requests = useQuery({
    queryKey: ["petty-cash", company?.id],
    queryFn: () => api<PettyCashRequestDto[]>(`/api/cash/petty-cash-requests?companyId=${company!.id}`),
    enabled: !!company,
  });
  const cashiers = useQuery({
    queryKey: ["cashiers", company?.id],
    queryFn: () => api<CashierDto[]>(`/api/cash/cashiers?companyId=${company!.id}`),
    enabled: !!company,
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ["petty-cash"] });
    void queryClient.invalidateQueries({ queryKey: ["cashiers"] });
  };
  const approve = useMutation({
    mutationFn: (r: PettyCashRequestDto) => {
      const f = funding[r.id] ?? { from: "SourceAccount", account: "", cashier: "" };
      return post(`/api/cash/petty-cash-requests/${r.id}/approve`, {
        fundingFrom: f.from, sourceAccountCode: f.from === "SourceAccount" ? f.account || null : null, fundingCashierId: f.from === "Cashier" ? f.cashier : null,
      });
    },
    onSuccess: refresh,
  });
  const reject = useMutation({
    mutationFn: (r: PettyCashRequestDto) => post(`/api/cash/petty-cash-requests/${r.id}/reject`, { reason: window.prompt(t("cash.reason")) ?? "" }),
    onSuccess: refresh,
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const set = (id: string, patch: Partial<{ from: string; account: string; cashier: string }>) =>
    setFunding((f) => ({ ...f, [id]: { from: "SourceAccount", account: "", cashier: "", ...f[id], ...patch } }));

  return (
    <>
      <h1>{t("nav.pettyCash")}</h1>
      {(approve.isError || reject.isError) && <div className="error">{(approve.error ?? reject.error)?.message}</div>}
      {requests.data?.length ? (
        <table className="table">
          <thead><tr><th>{t("fields.number")}</th><th>{t("nav.cashiers")}</th><th className="num">{t("cash.amount")}</th><th>{t("fields.status")}</th><th>{t("cash.fundingFrom")}</th><th /></tr></thead>
          <tbody>
            {requests.data.map((r) => {
              const f = funding[r.id] ?? { from: "SourceAccount", account: "", cashier: "" };
              const pending = r.status === "Pending" && can(me, "cash.petty-cash.approve");
              return (
                <tr key={r.id}>
                  <td className="mono">{r.number}{r.createdBySystem && <span className="chip small">{t("cash.system")}</span>}</td>
                  <td>{r.cashierName}</td><td className="num">{formatMoney(r.requestedAmount)}</td>
                  <td><span className={`badge ${r.status}`}>{t(`cash.statuses.${r.status}`)}</span>{r.rejectionReason && <div className="muted small">{r.rejectionReason}</div>}</td>
                  <td>
                    {pending ? (
                      <div className="inline-fields">
                        <select value={f.from} onChange={(e) => set(r.id, { from: e.target.value })}>
                          <option value="SourceAccount">{t("cash.sourceAccount")}</option><option value="Cashier">{t("cash.fundingCashier")}</option>
                        </select>
                        {f.from === "SourceAccount"
                          ? <input placeholder="1120" value={f.account} onChange={(e) => set(r.id, { account: e.target.value })} style={{ width: 90 }} />
                          : <select value={f.cashier} onChange={(e) => set(r.id, { cashier: e.target.value })}>
                              <option value="">—</option>
                              {cashiers.data?.filter((c) => c.id !== r.cashierId).map((c) => <option key={c.id} value={c.id}>{c.code} ({formatMoney(c.balance)})</option>)}
                            </select>}
                      </div>
                    ) : (r.fundingFrom === "Cashier" ? t("cash.fundingCashier") : r.sourceAccountCode ?? "—")}
                  </td>
                  <td>
                    {pending && (
                      <div className="inline-fields">
                        <button onClick={() => approve.mutate(r)} disabled={approve.isPending}>{t("cash.approve")}</button>
                        <button className="secondary" onClick={() => reject.mutate(r)}>{t("cash.reject")}</button>
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </>
  );
}
