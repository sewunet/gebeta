import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { post } from "../../lib/api";
import { formData } from "../../lib/hooks";
import { can, type WarehouseDto } from "../../lib/types";
import { Dialog, ListReport, MessageStrip } from "../../ui/fiori";
import { useWarehouses } from "../../lib/queries";

/** Warehouses and the stock account each one carries its stock value on. */
export function Warehouses() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [creating, setCreating] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const warehouses = useWarehouses();
  const create = useMutation({
    mutationFn: (b: Record<string, string>) => post<WarehouseDto>("/api/stock/warehouses", { companyId: company!.id, code: b.code, name: b.name, stockAccountCode: b.stockAccountCode || null }),
    onSuccess: (w) => { setCreating(false); setNotice(t("stock.warehouseSaved", { code: w.code })); void queryClient.invalidateQueries({ queryKey: ["warehouses"] }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const onCreate = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(formData(e.currentTarget)); };

  return (
    <ListReport
      title={t("nav.warehouses")}
      tableTitle={t("nav.warehouses")}
      count={warehouses.data?.length}
      messages={notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
      toolbar={can(me, "stock.warehouses.manage") && <button onClick={() => setCreating(true)}>{t("ui.create")}</button>}
    >
      {warehouses.data?.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th>{t("stock.stockAccount")}</th></tr></thead>
          <tbody>{warehouses.data.map((w) => (
            <tr key={w.id}><td data-label={t("acc.code")} className="mono">{w.code}</td><td data-label={t("fields.name")}>{w.name}</td><td data-label={t("stock.stockAccount")} className="mono">{w.stockAccountCode}</td></tr>
          ))}</tbody>
        </table>
      ) : <div className="empty">{warehouses.isPending ? t("common.loading") : t("stock.noWarehouses")}</div>}

      <Dialog open={creating} title={t("stock.newWarehouse")} onClose={() => setCreating(false)}
        footer={<><button type="submit" form="warehouse-form" disabled={create.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setCreating(false)}>{t("common.cancel")}</button></>}>
        <form id="warehouse-form" className="dialog-form" onSubmit={onCreate}>
          <label>{t("acc.code")}<span className="required" aria-hidden>*</span><input name="code" required maxLength={20} /></label>
          <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" required /></label>
          <label>{t("stock.stockAccount")}<input name="stockAccountCode" placeholder="1300" /></label>
          <p className="muted small">{t("stock.stockAccountHint")}</p>
          {create.isError && <MessageStrip tone="negative">{create.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}
