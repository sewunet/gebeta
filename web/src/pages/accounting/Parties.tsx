import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formData } from "../../lib/hooks";
import { can, type PartyDto } from "../../lib/types";

/** Customers and suppliers of the selected company (kept in Core, used by invoices). */
export function useParties() {
  const { company } = useSession();
  return useQuery({
    queryKey: ["parties", company?.id],
    queryFn: () => api<PartyDto[]>(`/api/core/parties?companyId=${company!.id}`),
    enabled: !!company,
  });
}

export function Parties() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const parties = useParties();
  const create = useMutation({
    mutationFn: (body: Record<string, string>) =>
      post<PartyDto>("/api/core/parties", {
        companyId: company!.id, name: body.name, type: body.type,
        tin: body.tin || null, vatRegistrationNumber: body.vatRegistrationNumber || null, phone: body.phone || null,
      }),
    onSuccess: () => { setShowForm(false); void queryClient.invalidateQueries({ queryKey: ["parties"] }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const onSubmit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(formData(e.currentTarget)); };

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.parties")}</h1>
        {can(me, "core.parties.manage") && <button onClick={() => setShowForm((s) => !s)}>{t("inv.newParty")}</button>}
      </div>
      {showForm && (
        <form className="form-grid" onSubmit={onSubmit}>
          <label>{t("fields.name")}<input name="name" required /></label>
          <label>{t("fields.type")}
            <select name="type" defaultValue="Customer">
              {(["Customer", "Supplier", "Both"] as const).map((k) => <option key={k} value={k}>{t(`inv.partyType.${k}`)}</option>)}
            </select>
          </label>
          <label>{t("fields.tin")}<input name="tin" inputMode="numeric" pattern="\d{10}" title={t("inv.tinHint")} /></label>
          <label>{t("inv.vatNumber")}<input name="vatRegistrationNumber" /></label>
          <label>{t("inv.phone")}<input name="phone" type="tel" /></label>
          <div className="form-actions">
            <button type="submit" disabled={create.isPending}>{t("common.save")}</button>
            {create.isError && <span className="error">{create.error.message}</span>}
          </div>
        </form>
      )}
      {parties.data?.length ? (
        <table className="table">
          <thead><tr><th>{t("fields.name")}</th><th>{t("fields.type")}</th><th>{t("fields.tin")}</th><th>{t("inv.vat")}</th><th>{t("inv.phone")}</th></tr></thead>
          <tbody>
            {parties.data.map((p) => (
              <tr key={p.id}>
                <td>{p.name}</td><td>{t(`inv.partyType.${p.type}`)}</td><td className="mono">{p.tin}</td>
                <td>{p.vatRegistrationNumber ? <span className="badge Submitted">{p.vatRegistrationNumber}</span> : <span className="muted">—</span>}</td>
                <td>{p.phone}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </>
  );
}
