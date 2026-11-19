import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams, Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";
import { api, put } from "../lib/api";
import { formData } from "../lib/hooks";
import { can, type CompanyDto } from "../lib/types";
import { EditFooter, Fields, ListReport, MessageStrip, ObjectPage, ObjectStatus } from "../ui/fiori";
import { historySection } from "../app/history";
import { attachmentsSection } from "../app/attachments";

/** List report of the companies this user can work in. */
export function Companies() {
  const { t } = useTranslation();
  const { companies, company } = useSession();
  const navigate = useNavigate();
  return (
    <ListReport title={t("nav.companies")} tableTitle={t("nav.companies")} count={companies.length}>
      <table className="table responsive">
        <thead><tr>
          <th>{t("fields.name")}</th><th>{t("fields.abbreviation")}</th><th>{t("fields.tin")}</th>
          <th>{t("fields.legalForm")}</th><th>{t("fields.currency")}</th><th>{t("fields.fiscalYear")}</th>
        </tr></thead>
        <tbody>
          {companies.map((c) => (
            <tr key={c.id} className="navigable" onClick={() => void navigate({ to: "/companies/$id", params: { id: c.id } })}>
              <td data-label={t("fields.name")}><span className="strong-link">{c.name}</span>{c.id === company?.id && <span className="chip small">{t("companies.current")}</span>}</td>
              <td data-label={t("fields.abbreviation")}>{c.abbreviation}</td>
              <td data-label={t("fields.tin")} className="mono">{c.tin}</td>
              <td data-label={t("fields.legalForm")}>{c.legalForm}</td>
              <td data-label={t("fields.currency")}>{c.defaultCurrency}</td>
              <td data-label={t("fields.fiscalYear")}>{c.fiscalYearType}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </ListReport>
  );
}

/** Object page of one company, with an edit mode for the name, VAT registration and trade licence. */
export function CompanyDetail() {
  const { t } = useTranslation();
  const { me, company, setCompanyId } = useSession();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/companies/$id" });
  const [editing, setEditing] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const query = useQuery({ queryKey: ["company", id], queryFn: () => api<CompanyDto>(`/api/core/companies/${id}`) });
  const save = useMutation({
    mutationFn: (b: Record<string, string>) => put<CompanyDto>(`/api/core/companies/${id}`, {
      name: b.name, vatRegistrationNumber: b.vatRegistrationNumber || null, tradeLicenseNumber: b.tradeLicenseNumber || null,
    }),
    onSuccess: (c) => {
      queryClient.setQueryData(["company", id], c);
      void queryClient.invalidateQueries({ queryKey: ["companies"] });
      setEditing(false);
      setNotice(t("ui.saved"));
    },
  });
  if (query.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (query.isError) return <MessageStrip tone="negative">{t("companies.notFound")}</MessageStrip>;
  const c = query.data;
  const current = c.id === company?.id;
  const onSave = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); save.mutate(formData(e.currentTarget)); };

  return (
    <ObjectPage
      breadcrumb={<Link to="/companies">{t("nav.companies")}</Link>}
      title={c.name}
      subtitle={<>{c.abbreviation} · {c.legalForm}</>}
      status={<ObjectStatus tone={c.vatRegistrationNumber ? "positive" : "information"}>{c.vatRegistrationNumber ? t("companies.vatRegistered") : t("companies.notVatRegistered")}</ObjectStatus>}
      kpis={[
        { label: t("fields.tin"), value: <span className="mono">{c.tin}</span> },
        { label: t("fields.currency"), value: c.defaultCurrency },
        { label: t("fields.fiscalYear"), value: c.fiscalYearType },
      ]}
      actions={!editing && <>
        {can(me, "core.companies.manage") && <button onClick={() => { setNotice(null); setEditing(true); }}>{t("ui.edit")}</button>}
        {!current && <button className="secondary" onClick={() => setCompanyId(c.id)}>{t("companies.useThis")}</button>}
      </>}
      footer={editing && <EditFooter form="company-edit" saving={save.isPending} error={save.error?.message} onCancel={() => setEditing(false)}
        saveLabel={t("common.save")} cancelLabel={t("common.cancel")} />}
      messages={<>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {current && <MessageStrip tone="information">{t("companies.isCurrent")}</MessageStrip>}
      </>}
      sections={[{ id: "general", title: t("ui.general"), content: editing ? (
        <form id="company-edit" className="form-columns" onSubmit={onSave}>
          <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" defaultValue={c.name} required /></label>
          <label>{t("inv.vatNumber")}<input name="vatRegistrationNumber" defaultValue={c.vatRegistrationNumber ?? ""} /></label>
          <label>{t("companies.tradeLicense")}<input name="tradeLicenseNumber" defaultValue={c.tradeLicenseNumber ?? ""} /></label>
          <p className="muted small" style={{ gridColumn: "1 / -1" }}>{t("companies.fixedFields")}</p>
        </form>
      ) : (
        <Fields items={[
          { label: t("fields.name"), value: c.name },
          { label: t("fields.abbreviation"), value: c.abbreviation },
          { label: t("fields.legalForm"), value: c.legalForm },
          { label: t("fields.tin"), value: <span className="mono">{c.tin}</span> },
          { label: t("inv.vatNumber"), value: c.vatRegistrationNumber },
          { label: t("companies.tradeLicense"), value: c.tradeLicenseNumber },
          { label: t("fields.currency"), value: c.defaultCurrency },
          { label: t("fields.fiscalYear"), value: c.fiscalYearType },
          { label: t("companies.timeZone"), value: c.timeZone },
        ]} />
      ) },
        ...attachmentsSection(t, me, id),
        ...historySection(t, me, id, "Company")
      ]}
    />
  );
}
