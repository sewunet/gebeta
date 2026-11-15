import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";

export function Companies() {
  const { t } = useTranslation();
  const { companies } = useSession();
  return (
    <>
      <h1>{t("nav.companies")}</h1>
      <table className="table">
        <thead>
          <tr>
            <th>{t("fields.name")}</th><th>{t("fields.abbreviation")}</th><th>{t("fields.tin")}</th>
            <th>{t("fields.legalForm")}</th><th>{t("fields.currency")}</th><th>{t("fields.fiscalYear")}</th>
          </tr>
        </thead>
        <tbody>
          {companies.map((c) => (
            <tr key={c.id}>
              <td>{c.name}</td><td>{c.abbreviation}</td><td className="mono">{c.tin}</td>
              <td>{c.legalForm}</td><td>{c.defaultCurrency}</td><td>{c.fiscalYearType}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
