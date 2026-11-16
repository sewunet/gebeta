import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import { can, type PettyCashRequestDto } from "../../lib/types";
import { Dialog, FilterBar, FilterField, ListReport, MessageStrip, ObjectStatus, type Tone } from "../../ui/fiori";
import { useCashiers } from "../../lib/queries";

const TONE: Record<string, Tone> = { Draft: "neutral", Pending: "critical", ApprovedAndPaid: "positive", Rejected: "negative", Cancelled: "negative" };

/** Petty cash top-up requests; approving asks where the money comes from. */
export function PettyCash() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState("Pending");
  const [approving, setApproving] = useState<PettyCashRequestDto | null>(null);
  const [rejecting, setRejecting] = useState<PettyCashRequestDto | null>(null);
  const [from, setFrom] = useState("SourceAccount");
  const [notice, setNotice] = useState<string | null>(null);
  const requests = useQuery({
    queryKey: ["petty-cash", company?.id],
    queryFn: () => api<PettyCashRequestDto[]>(`/api/cash/petty-cash-requests?companyId=${company!.id}`),
    enabled: !!company,
  });
  const cashiers = useCashiers();
  const done = (message: string) => {
    setApproving(null); setRejecting(null); setNotice(message);
    void queryClient.invalidateQueries({ queryKey: ["petty-cash"] });
    void queryClient.invalidateQueries({ queryKey: ["cashiers"] });
  };
  const approve = useMutation({
    mutationFn: ({ r, b }: { r: PettyCashRequestDto; b: FormData }) => post(`/api/cash/petty-cash-requests/${r.id}/approve`, {
      fundingFrom: from, sourceAccountCode: from === "SourceAccount" ? b.get("account") || null : null, fundingCashierId: from === "Cashier" ? b.get("cashier") : null,
    }),
    onSuccess: (_, v) => done(t("cash.approved", { number: v.r.number })),
  });
  const reject = useMutation({
    mutationFn: ({ r, reason }: { r: PettyCashRequestDto; reason: string }) => post(`/api/cash/petty-cash-requests/${r.id}/reject`, { reason }),
    onSuccess: (_, v) => done(t("cash.rejected", { number: v.r.number })),
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const rows = (requests.data ?? []).filter((r) => !status || r.status === status);
  const decide = can(me, "cash.petty-cash.approve");
  const onApprove = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); if (approving) approve.mutate({ r: approving, b: new FormData(e.currentTarget) }); };
  const onReject = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); if (rejecting) reject.mutate({ r: rejecting, reason: String(new FormData(e.currentTarget).get("reason")) }); };

  return (
    <ListReport
      title={t("nav.pettyCash")}
      filters={
        <FilterBar>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {["Pending", "ApprovedAndPaid", "Rejected", "Cancelled"].map((s) => <option key={s} value={s}>{t(`cash.statuses.${s}`)}</option>)}
            </select>
          </FilterField>
        </FilterBar>
      }
      tableTitle={t("cash.requests")}
      count={rows.length}
      messages={notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("nav.cashiers")}</th><th className="num">{t("cash.amount")}</th><th>{t("fields.status")}</th><th>{t("cash.fundingFrom")}</th><th /></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td data-label={t("fields.number")} className="mono">{r.number}{r.createdBySystem && <span className="chip small">{t("cash.system")}</span>}</td>
                <td data-label={t("nav.cashiers")}>{r.cashierName}</td>
                <td data-label={t("cash.amount")} className="num">{formatMoney(r.requestedAmount)}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={TONE[r.status] ?? "neutral"}>{t(`cash.statuses.${r.status}`)}</ObjectStatus>
                  {r.rejectionReason && <div className="muted small">{r.rejectionReason}</div>}</td>
                <td data-label={t("cash.fundingFrom")}>{r.status === "ApprovedAndPaid" ? (r.fundingFrom === "Cashier" ? t("cash.fundingCashier") : r.sourceAccountCode ?? "—") : ""}</td>
                <td className="actions">{r.status === "Pending" && decide && <>
                  <button onClick={() => { setFrom("SourceAccount"); setApproving(r); }}>{t("cash.approve")}</button>
                  <button className="secondary" onClick={() => setRejecting(r)}>{t("cash.reject")}</button>
                </>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{requests.isPending ? t("common.loading") : t("common.empty")}</div>}

      <Dialog open={!!approving} title={`${t("cash.approve")} · ${approving?.number ?? ""}`} onClose={() => setApproving(null)}
        footer={<><button type="submit" form="petty-approve" disabled={approve.isPending}>{t("cash.approve")}</button><button className="secondary" onClick={() => setApproving(null)}>{t("common.cancel")}</button></>}>
        <form id="petty-approve" className="dialog-form" onSubmit={onApprove}>
          <p className="muted small">{approving?.cashierName} · {approving && formatMoney(approving.requestedAmount)}</p>
          <label>{t("cash.fundingFrom")}
            <select value={from} onChange={(e) => setFrom(e.target.value)}>
              <option value="SourceAccount">{t("cash.sourceAccount")}</option><option value="Cashier">{t("cash.fundingCashier")}</option>
            </select>
          </label>
          {from === "SourceAccount"
            ? <label>{t("cash.account")}<input name="account" placeholder="1120" /></label>
            : <label>{t("cash.fundingCashier")}
                <select name="cashier" required defaultValue="">
                  <option value="" disabled>—</option>
                  {cashiers.data?.filter((c) => c.id !== approving?.cashierId).map((c) => <option key={c.id} value={c.id}>{c.code} · {c.name} ({formatMoney(c.balance)})</option>)}
                </select>
              </label>}
          {approve.isError && <MessageStrip tone="negative">{approve.error.message}</MessageStrip>}
        </form>
      </Dialog>
      <Dialog open={!!rejecting} title={`${t("cash.reject")} · ${rejecting?.number ?? ""}`} onClose={() => setRejecting(null)}
        footer={<><button type="submit" form="petty-reject" className="danger" disabled={reject.isPending}>{t("cash.reject")}</button><button className="secondary" onClick={() => setRejecting(null)}>{t("common.cancel")}</button></>}>
        <form id="petty-reject" className="dialog-form" onSubmit={onReject}>
          <label>{t("cash.reason")}<span className="required" aria-hidden>*</span><input name="reason" required /></label>
          {reject.isError && <MessageStrip tone="negative">{reject.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}
