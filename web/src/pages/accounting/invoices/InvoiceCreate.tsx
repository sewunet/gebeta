import { useEffect, useState, type FormEvent } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../../app/context";
import { api, post } from "../../../lib/api";
import { formatMoney, todayIso } from "../../../lib/ethiopian";
import type { AccountDto, InvoiceDto } from "../../../lib/types";
import { DynamicPage, MessageStrip } from "../../../ui/fiori";
import { useInvoiceRefresh, type Kind } from "./shared";
import { useItems, useParties, usePurchaseOrder, useSalesOrder } from "../../../lib/queries";

type Line = { itemId: string; description: string; quantity: string; rate: string; accountCode: string; isService: boolean; vatApplicable: boolean };
const emptyLine = (kind: Kind): Line => ({ itemId: "", description: "", quantity: "1", rate: "", accountCode: kind === "Sales" ? "4100" : "", isService: false, vatApplicable: true });

/** A new invoice as a page in create mode, with Save and Cancel in the footer bar. */
export function InvoiceCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const { kind, po, so } = useSearch({ from: "/accounting/invoices/new" });
  const order = usePurchaseOrder(po);
  const salesOrder = useSalesOrder(so);
  const [partyId, setPartyId] = useState("");
  const sales = kind === "Sales";
  const [lines, setLines] = useState<Line[]>([emptyLine(kind)]);
  const parties = useParties();
  const items = useItems();
  useEffect(() => {
    // Billing a purchase order: its supplier and what is not billed yet, on 2150 for stock items (cleared against the goods received).
    if (!order.data || !items.data) return;
    setPartyId(order.data.supplierId);
    setLines(order.data.lines.filter((l) => l.qty > l.billedQty).map((l) => {
      const item = items.data!.find((x) => x.id === l.itemId);
      return { itemId: l.itemId, description: l.itemName, quantity: String(l.qty - l.billedQty), rate: String(l.rate),
        accountCode: item?.isStockItem ? "2150" : "", isService: !item?.isStockItem, vatApplicable: true };
    }));
  }, [order.data, items.data]);
  useEffect(() => {
    // Invoicing a sales order: its customer and what is not invoiced yet.
    if (!salesOrder.data) return;
    setPartyId(salesOrder.data.customerId);
    setLines(salesOrder.data.lines.filter((l) => l.qty > l.billedQty).map((l) => ({
      itemId: l.itemId, description: l.itemName, quantity: String(l.qty - l.billedQty), rate: String(l.rate), accountCode: "4100", isService: false, vatApplicable: true,
    })));
  }, [salesOrder.data]);
  const refresh = useInvoiceRefresh();
  const accounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company,
  });
  const create = useMutation({
    mutationFn: (body: Record<string, unknown>) => post<InvoiceDto>("/api/accounting/invoices", body),
    onSuccess: (inv) => { refresh(); void navigate({ to: "/accounting/invoices/$id", params: { id: inv.id } }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;

  const partyList = parties.data?.filter((p) => p.type === "Both" || p.type === (sales ? "Customer" : "Supplier")) ?? [];
  const postable = accounts.data?.filter((a) => !a.isGroup && (sales ? a.rootType === "Income" : ["Expense", "Asset"].includes(a.rootType) || a.code === "2150")) ?? [];
  const pickItem = (i: number, itemId: string) => {
    const item = items.data?.find((x) => x.id === itemId);
    setLine(i, item
      ? { itemId, description: item.name, isService: !item.isStockItem, accountCode: sales ? "4100" : item.isStockItem ? "2150" : shown(i).accountCode }
      : { itemId: "" });
  };
  const shown = (i: number) => lines[i]!;
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const net = lines.reduce((a, l) => a + Number(l.quantity || 0) * Number(l.rate || 0), 0);
  const back = () => void navigate({ to: "/accounting/invoices", search: { kind } });
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    create.mutate({
      kind, companyId: company.id, partyId: f.get("partyId"), postingDate: f.get("postingDate"), dueDate: f.get("dueDate") || null,
      supplierInvoiceNumber: f.get("supplierInvoiceNumber") || null, customerWithholds: f.get("customerWithholds") === "on", orderReference: order.data?.number ?? salesOrder.data?.number ?? null,
      lines: lines.filter((l) => l.description && l.accountCode).map((l) => ({ ...l, itemId: l.itemId || null, quantity: Number(l.quantity), rate: Number(l.rate || 0) })),
    });
  };

  return (
    <DynamicPage
      title={t(sales ? "inv.newSales" : "inv.newPurchase")}
      breadcrumb={<Link to="/accounting/invoices" search={{ kind }}>{t("nav.invoices")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        <button type="submit" form="invoice-create" disabled={create.isPending || net <= 0}>{t("inv.saveDraft")}</button>
        <button className="secondary" onClick={back}>{t("common.cancel")}</button>
      </>}
    >
      {order.data && <MessageStrip tone="information">{t("buy.billingAgainst", { number: order.data.number, supplier: order.data.supplierName })}</MessageStrip>}
      {salesOrder.data && <MessageStrip tone="information">{t("sell.invoicingAgainst", { number: salesOrder.data.number, customer: salesOrder.data.customerName })}</MessageStrip>}
      <form id="invoice-create" onSubmit={onSubmit}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t(sales ? "inv.customer" : "inv.supplier")}<span className="required" aria-hidden>*</span>
              <select name="partyId" required value={partyId} onChange={(e) => setPartyId(e.target.value)}>
                <option value="" disabled>—</option>
                {partyList.map((p) => <option key={p.id} value={p.id}>{p.name}{p.tin ? ` · ${p.tin}` : ""}</option>)}
              </select>
            </label>
            <label>{t("attendance.date")}<span className="required" aria-hidden>*</span><input type="date" name="postingDate" defaultValue={todayIso()} required /></label>
            <label>{t("inv.dueDate")}<input type="date" name="dueDate" /></label>
            {sales
              ? <label className="check"><input type="checkbox" name="customerWithholds" /> {t("inv.customerWithholds")}</label>
              : <label>{t("inv.supplierInvoiceNumber")}<input name="supplierInvoiceNumber" /></label>}
          </div>
        </section>
        <section className="object-section">
          <h2>{t("ui.lines")}</h2>
          <table className="table edit-table">
            <thead><tr><th>{t("nav.items")}</th><th>{t("cash.description")}</th><th className="num">{t("inv.qty")}</th><th className="num">{t("inv.rate")}</th><th>{t("acc.code")}</th>
              <th>{t("inv.service")}</th><th>{t("inv.vat")}</th><th className="num">{t("cash.amount")}</th><th /></tr></thead>
            <tbody>
              {lines.map((l, i) => (
                <tr key={i}>
                  <td><select aria-label={t("nav.items")} value={l.itemId} onChange={(e) => pickItem(i, e.target.value)}>
                    <option value="">—</option>{items.data?.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}
                  </select></td>
                  <td><input aria-label={t("cash.description")} value={l.description} onChange={(e) => setLine(i, { description: e.target.value })} /></td>
                  <td><input aria-label={t("inv.qty")} className="num" type="number" step="0.001" min="0" value={l.quantity} onChange={(e) => setLine(i, { quantity: e.target.value })} /></td>
                  <td><input aria-label={t("inv.rate")} className="num" type="number" step="0.01" min="0" value={l.rate} onChange={(e) => setLine(i, { rate: e.target.value })} /></td>
                  <td>
                    <select aria-label={t("acc.code")} value={l.accountCode} onChange={(e) => setLine(i, { accountCode: e.target.value })}>
                      <option value="">—</option>
                      {postable.map((a) => <option key={a.id} value={a.code}>{a.code} · {a.name}</option>)}
                    </select>
                  </td>
                  <td><input type="checkbox" aria-label={t("inv.service")} checked={l.isService} onChange={(e) => setLine(i, { isService: e.target.checked })} /></td>
                  <td><input type="checkbox" aria-label={t("inv.vat")} checked={l.vatApplicable} onChange={(e) => setLine(i, { vatApplicable: e.target.checked })} /></td>
                  <td className="num">{formatMoney(Number(l.quantity || 0) * Number(l.rate || 0))}</td>
                  <td>{lines.length > 1 && <button type="button" className="link-button" aria-label={t("ui.remove")} onClick={() => setLines((ls) => ls.filter((_, j) => j !== i))}>×</button>}</td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={7}><button type="button" className="secondary" onClick={() => setLines((ls) => [...ls, emptyLine(kind)])}>{t("acc.addLine")}</button></td>
              <td className="num">{formatMoney(net)}</td><td /></tr></tfoot>
          </table>
          <MessageStrip tone="information">{t("inv.taxesOnSave")}{!sales && <> {t("inv.stockItemHint")}</>}</MessageStrip>
        </section>
      </form>
    </DynamicPage>
  );
}
