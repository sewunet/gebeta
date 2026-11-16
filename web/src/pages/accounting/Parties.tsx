import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formData } from "../../lib/hooks";
import { can, type PartyDto } from "../../lib/types";
import { Dialog, FilterBar, FilterField, ListReport, MessageStrip, ObjectStatus } from "../../ui/fiori";

/** Customers and suppliers of the selected company (kept in Core, used by invoices). */
export function useParties() {
  const { company } = useSession();
  return useQuery({
    queryKey: ["parties", company?.id],
    queryFn: () => api<PartyDto[]>(`/api/core/parties?companyId=${company!.id}`),
    enabled: !!company,
  });
}

/** List report of customers and suppliers; a row opens the party's object page. */
export function Parties() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const [search, setSearch] = useState("");
  const [type, setType] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const parties = useParties();
  const create = useMutation({
    mutationFn: (body: Record<string, string>) =>
      post<PartyDto>("/api/core/parties", {
        companyId: company!.id, name: body.name, type: body.type,
        tin: body.tin || null, vatRegistrationNumber: body.vatRegistrationNumber || null, phone: body.phone || null,
      }),
    onSuccess: (p) => { setCreating(false); setNotice(t("inv.partySaved", { name: p.name })); void queryClient.invalidateQueries({ queryKey: ["parties"] }); },
  });
  const rows = useMemo(() => (parties.data ?? []).filter((p) => {
    if (type && p.type !== type && !(p.type === "Both" && type !== "Both")) return false;
    const q = search.trim().toLowerCase();
    return !q || [p.name, p.tin, p.phone, p.vatRegistrationNumber].some((v) => v?.toLowerCase().includes(q));
  }), [parties.data, search, type]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const onSubmit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(formData(e.currentTarget)); };
  const open = (p: PartyDto) => void navigate({ to: "/accounting/parties/$id", params: { id: p.id } });

  return (
    <ListReport
      title={t("nav.parties")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("inv.partySearchHint")} /></FilterField>
          <FilterField label={t("fields.type")}>
            <select value={type} onChange={(e) => setType(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {(["Customer", "Supplier", "Both"] as const).map((k) => <option key={k} value={k}>{t(`inv.partyType.${k}`)}</option>)}
            </select>
          </FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.parties")}
      count={rows.length}
      messages={notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
      toolbar={can(me, "core.parties.manage") && <button onClick={() => setCreating(true)}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.name")}</th><th>{t("fields.type")}</th><th>{t("fields.tin")}</th><th>{t("inv.vat")}</th><th>{t("inv.phone")}</th></tr></thead>
          <tbody>
            {rows.map((p) => (
              <tr key={p.id} className="navigable" onClick={() => open(p)}>
                <td data-label={t("fields.name")}><span className="strong-link">{p.name}</span></td>
                <td data-label={t("fields.type")}>{t(`inv.partyType.${p.type}`)}</td>
                <td data-label={t("fields.tin")} className="mono">{p.tin}</td>
                <td data-label={t("inv.vat")}>{p.vatRegistrationNumber ? <ObjectStatus tone="positive">{p.vatRegistrationNumber}</ObjectStatus> : <span className="muted">—</span>}</td>
                <td data-label={t("inv.phone")}>{p.phone}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{parties.isPending ? t("common.loading") : t("common.empty")}</div>}

      <Dialog open={creating} title={t("inv.newParty")} onClose={() => setCreating(false)}
        footer={<><button type="submit" form="party-form" disabled={create.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setCreating(false)}>{t("common.cancel")}</button></>}>
        <form id="party-form" className="dialog-form" onSubmit={onSubmit}>
          <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" required /></label>
          <label>{t("fields.type")}
            <select name="type" defaultValue="Customer">
              {(["Customer", "Supplier", "Both"] as const).map((k) => <option key={k} value={k}>{t(`inv.partyType.${k}`)}</option>)}
            </select>
          </label>
          <label>{t("fields.tin")}<input name="tin" inputMode="numeric" pattern="\d{10}" title={t("inv.tinHint")} /></label>
          <label>{t("inv.vatNumber")}<input name="vatRegistrationNumber" /></label>
          <label>{t("inv.phone")}<input name="phone" type="tel" /></label>
          {create.isError && <MessageStrip tone="negative">{create.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}
