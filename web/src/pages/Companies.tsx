import { useNavigate, useParams, Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";
import { Fields, ListReport, MessageStrip, ObjectPage, ObjectStatus } from "../ui/fiori";

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

/** Object page of one company. */
export function CompanyDetail() {
  const { t } = useTranslation();
  const { companies, company, setCompanyId } = useSession();
  const { id } = useParams({ from: "/companies/$id" });
  const c = companies.find((x) => x.id === id);
  if (!c) return <MessageStrip tone="negative">{t("companies.notFound")}</MessageStrip>;
  const current = c.id === company?.id;
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
      actions={!current && <button onClick={() => setCompanyId(c.id)}>{t("companies.useThis")}</button>}
      messages={current && <MessageStrip tone="information">{t("companies.isCurrent")}</MessageStrip>}
      sections={[{ id: "general", title: t("ui.general"), content: (
        <Fields items={[
          { label: t("fields.name"), value: c.name },
          { label: t("fields.abbreviation"), value: c.abbreviation },
          { label: t("fields.legalForm"), value: c.legalForm },
          { label: t("fields.tin"), value: <span className="mono">{c.tin}</span> },
          { label: t("inv.vatNumber"), value: c.vatRegistrationNumber },
          { label: t("fields.currency"), value: c.defaultCurrency },
          { label: t("fields.fiscalYear"), value: c.fiscalYearType },
          { label: t("companies.timeZone"), value: c.timeZone },
        ]} />
      ) }]}
    />
  );
}
