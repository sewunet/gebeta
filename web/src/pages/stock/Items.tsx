import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post, put } from "../../lib/api";
import { formatMoney, todayIso } from "../../lib/ethiopian";
import { formData } from "../../lib/hooks";
import { can, type ItemDto, type ItemStockSettingDto, type StockBalanceDto } from "../../lib/types";
import { Dialog, EditFooter, Fields, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus } from "../../ui/fiori";
import { useItems } from "../../lib/queries";
import { historySection } from "../../app/history";

const UOMS = ["pcs", "kg", "quintal", "ton", "litre", "metre", "bag", "box", "set", "trip", "hour"];

/** List report of items; "Create" opens a dialog, a row opens the item. */
export function Items() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const [search, setSearch] = useState("");
  const [kind, setKind] = useState("");
  const [group, setGroup] = useState("");
  const items = useItems();
  const create = useMutation({
    mutationFn: (b: Record<string, string>) => post<ItemDto>("/api/core/items", { companyId: company!.id, code: b.code, name: b.name, uom: b.uom, group: b.group || null, isStockItem: b.isStockItem === "on" }),
    onSuccess: (i) => { setCreating(false); void queryClient.invalidateQueries({ queryKey: ["items"] }); void navigate({ to: "/items/$id", params: { id: i.id } }); },
  });
  const groups = useMemo(() => [...new Set((items.data ?? []).map((i) => i.group).filter(Boolean) as string[])].sort(), [items.data]);
  const rows = (items.data ?? []).filter((i) => (!kind || (kind === "stock") === i.isStockItem) && (!group || i.group === group)
    && (!search.trim() || `${i.code} ${i.name}`.toLowerCase().includes(search.trim().toLowerCase())));
  if (!company) return <p>{t("common.noCompany")}</p>;
  const onCreate = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(formData(e.currentTarget)); };

  return (
    <ListReport
      title={t("nav.items")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("acc.accountSearchHint")} /></FilterField>
          <FilterField label={t("fields.type")}>
            <select value={kind} onChange={(e) => setKind(e.target.value)}>
              <option value="">{t("ui.all")}</option><option value="stock">{t("stock.stockItem")}</option><option value="other">{t("stock.nonStock")}</option>
            </select>
          </FilterField>
          <FilterField label={t("stock.group")}>
            <select value={group} onChange={(e) => setGroup(e.target.value)}>
              <option value="">{t("ui.all")}</option>{groups.map((g) => <option key={g} value={g}>{g}</option>)}
            </select>
          </FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.items")}
      count={rows.length}
      toolbar={can(me, "core.items.manage") && <button onClick={() => setCreating(true)}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th>{t("stock.uom")}</th><th>{t("stock.group")}</th><th>{t("fields.type")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {rows.map((i) => (
              <tr key={i.id} className="navigable" onClick={() => void navigate({ to: "/items/$id", params: { id: i.id } })}>
                <td data-label={t("acc.code")}><span className="mono strong-link">{i.code}</span></td>
                <td data-label={t("fields.name")}>{i.name}</td>
                <td data-label={t("stock.uom")}>{i.uom}</td>
                <td data-label={t("stock.group")}>{i.group}</td>
                <td data-label={t("fields.type")}>{i.isStockItem ? t("stock.stockItem") : t("stock.nonStock")}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={i.isActive ? "positive" : "neutral"}>{i.isActive ? t("status.Active") : t("stock.inactive")}</ObjectStatus></td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{items.isPending ? t("common.loading") : t("common.empty")}</div>}

      <Dialog open={creating} title={t("stock.newItem")} onClose={() => setCreating(false)}
        footer={<><button type="submit" form="item-form" disabled={create.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setCreating(false)}>{t("common.cancel")}</button></>}>
        <form id="item-form" className="dialog-form" onSubmit={onCreate}>
          <label>{t("acc.code")}<span className="required" aria-hidden>*</span><input name="code" required maxLength={40} pattern="\S+" /></label>
          <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" required /></label>
          <label>{t("stock.uom")}<input name="uom" list="uoms" defaultValue="pcs" required /></label>
          <label>{t("stock.group")}<input name="group" list="groups" /></label>
          <label className="check"><input type="checkbox" name="isStockItem" defaultChecked /> {t("stock.isStockItem")}</label>
          <datalist id="uoms">{UOMS.map((u) => <option key={u} value={u} />)}</datalist>
          <datalist id="groups">{groups.map((g) => <option key={g} value={g} />)}</datalist>
          {create.isError && <MessageStrip tone="negative">{create.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}

/** Object page of one item: details with an edit mode, stock per warehouse and the valuation method. */
export function ItemDetail() {
  const { t } = useTranslation();
  const { me, company, modules } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { id } = useParams({ from: "/items/$id" });
  const [editing, setEditing] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const hasStock = modules.some((m) => m.id === "stock");
  const item = useQuery({ queryKey: ["item", id], queryFn: () => api<ItemDto>(`/api/core/items/${id}`) });
  const setting = useQuery({ queryKey: ["item-setting", id], queryFn: () => api<ItemStockSettingDto>(`/api/stock/items/${id}/settings`),
    enabled: hasStock && !!item.data?.isStockItem && can(me, "stock.warehouses.read") });
  const balance = useQuery({ queryKey: ["stock-balance", company?.id, todayIso()], queryFn: () => api<StockBalanceDto>(`/api/stock/reports/balance?companyId=${company!.id}&asOf=${todayIso()}`),
    enabled: hasStock && !!company && !!item.data?.isStockItem && can(me, "stock.reports.read") });
  const save = useMutation({
    mutationFn: (b: Record<string, string>) => put<ItemDto>(`/api/core/items/${id}`, { name: b.name, uom: b.uom, group: b.group || null, isStockItem: b.isStockItem === "on", isActive: b.isActive === "on" }),
    onSuccess: (i) => { queryClient.setQueryData(["item", id], i); void queryClient.invalidateQueries({ queryKey: ["items"] }); setEditing(false); setNotice(t("ui.saved")); },
  });
  const method = useMutation({
    mutationFn: (m: string) => put<ItemStockSettingDto>(`/api/stock/items/${id}/settings`, { method: m }),
    onSuccess: (s) => { queryClient.setQueryData(["item-setting", id], s); setNotice(t("ui.saved")); },
  });
  if (item.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (item.isError) return <MessageStrip tone="negative">{item.error.message}</MessageStrip>;
  const i = item.data;
  const rows = balance.data?.rows.filter((r) => r.itemId === id) ?? [];
  const qty = rows.reduce((a, r) => a + r.qty, 0);
  const value = rows.reduce((a, r) => a + r.value, 0);
  const onSave = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); save.mutate(formData(e.currentTarget)); };

  return (
    <ObjectPage
      breadcrumb={<Link to="/items">{t("nav.items")}</Link>}
      title={i.name}
      subtitle={<><span className="mono">{i.code}</span>{i.group && <> · {i.group}</>}</>}
      status={<ObjectStatus tone={i.isActive ? "positive" : "neutral"}>{i.isActive ? t("status.Active") : t("stock.inactive")}</ObjectStatus>}
      kpis={[
        { label: t("stock.uom"), value: i.uom },
        { label: t("fields.type"), value: i.isStockItem ? t("stock.stockItem") : t("stock.nonStock") },
        ...(i.isStockItem && balance.data ? [{ label: t("stock.onHand"), value: `${qty} ${i.uom}` }, { label: t("stock.value"), value: formatMoney(value) }] : []),
      ]}
      actions={!editing && <>
        {can(me, "core.items.manage") && <button onClick={() => { setNotice(null); setEditing(true); }}>{t("ui.edit")}</button>}
        {i.isStockItem && hasStock && can(me, "stock.reports.read") &&
          <button className="secondary" onClick={() => void navigate({ to: "/stock/ledger", search: { item: i.id } })}>{t("nav.stockLedger")}</button>}
      </>}
      footer={editing && <EditFooter form="item-edit" saving={save.isPending} error={save.error?.message} onCancel={() => setEditing(false)} saveLabel={t("common.save")} cancelLabel={t("common.cancel")} />}
      messages={<>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {method.isError && <MessageStrip tone="negative">{method.error.message}</MessageStrip>}
      </>}
      sections={[
        { id: "general", title: t("ui.general"), content: editing ? (
          <form id="item-edit" className="form-columns" onSubmit={onSave}>
            <label>{t("acc.code")}<input value={i.code} disabled /></label>
            <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" defaultValue={i.name} required /></label>
            <label>{t("stock.uom")}<input name="uom" defaultValue={i.uom} list="uoms" required /></label>
            <label>{t("stock.group")}<input name="group" defaultValue={i.group ?? ""} /></label>
            <label className="check"><input type="checkbox" name="isStockItem" defaultChecked={i.isStockItem} disabled={setting.data?.hasMovements} /> {t("stock.isStockItem")}</label>
            {setting.data?.hasMovements && <input type="hidden" name="isStockItem" value="on" />}
            <label className="check"><input type="checkbox" name="isActive" defaultChecked={i.isActive} /> {t("status.Active")}</label>
            <datalist id="uoms">{UOMS.map((u) => <option key={u} value={u} />)}</datalist>
          </form>
        ) : (
          <Fields items={[
            { label: t("acc.code"), value: <span className="mono">{i.code}</span> },
            { label: t("fields.name"), value: i.name },
            { label: t("stock.uom"), value: i.uom },
            { label: t("stock.group"), value: i.group },
            { label: t("fields.type"), value: i.isStockItem ? t("stock.stockItem") : t("stock.nonStock") },
          ]} />
        ) },
        { id: "stock", title: t("stock.stock"), hidden: !i.isStockItem || !hasStock, content: (
          <>
            {setting.data && (
              <div className="form-columns">
                <label>{t("stock.method")}
                  <select value={setting.data.method} disabled={setting.data.hasMovements || !can(me, "stock.warehouses.manage") || method.isPending}
                    onChange={(e) => method.mutate(e.target.value)}>
                    <option value="MovingAverage">{t("stock.methods.MovingAverage")}</option><option value="Fifo">{t("stock.methods.Fifo")}</option>
                  </select>
                </label>
                <p className="muted small">{t(setting.data.hasMovements ? "stock.methodFixed" : "stock.methodHint")}</p>
              </div>
            )}
            {rows.length ? (
              <table className="table">
                <thead><tr><th>{t("nav.warehouses")}</th><th className="num">{t("stock.qty")}</th><th className="num">{t("inv.rate")}</th><th className="num">{t("stock.value")}</th></tr></thead>
                <tbody>{rows.map((r) => (
                  <tr key={r.warehouseId}><td className="mono">{r.warehouse}</td><td className="num">{r.qty}</td><td className="num">{formatMoney(r.rate)}</td><td className="num">{formatMoney(r.value)}</td></tr>
                ))}</tbody>
                <tfoot><tr><td>{t("common.total")}</td><td className="num">{qty}</td><td /><td className="num">{formatMoney(value)}</td></tr></tfoot>
              </table>
            ) : <div className="empty">{t("stock.noStock")}</div>}
          </>
        ) },
        ...historySection(t, me, id, "Item")
      ]}
    />
  );
}
