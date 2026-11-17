import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { post } from "../../lib/api";
import { formatMoney, todayIso } from "../../lib/ethiopian";
import { useSalaryStructures } from "../../lib/queries";
import { can, type SalaryCalculationDto, type SalaryStructureDto } from "../../lib/types";
import { emptyRow, parseVariables, toComponent, TYPES, type Row } from "./structureForm";
import { DynamicPage, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus } from "../../ui/fiori";

/** List report of the company's salary structures. */
export function Structures() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const structures = useSalaryStructures();
  const rows = useMemo(() => (structures.data ?? []).filter((s) => !search.trim() || s.name.toLowerCase().includes(search.trim().toLowerCase())), [structures.data, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;

  return (
    <ListReport
      title={t("nav.salaryStructures")}
      filters={<FilterBar><FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} /></FilterField></FilterBar>}
      tableTitle={t("nav.salaryStructures")}
      count={rows.length}
      toolbar={can(me, "payroll.structures.manage") && <button onClick={() => void navigate({ to: "/payroll/structures/new" })}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.name")}</th><th>{t("structures.components")}</th><th>{t("structures.payValues")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {rows.map((s) => (
              <tr key={s.id} className="navigable" onClick={() => void navigate({ to: "/payroll/structures/$id", params: { id: s.id } })}>
                <td data-label={t("fields.name")}><span className="strong-link">{s.name}</span></td>
                <td data-label={t("structures.components")} className="mono small">{s.components.map((c) => c.abbreviation).join(" · ")}</td>
                <td data-label={t("structures.payValues")} className="mono small">{s.variables?.length ? s.variables.join(", ") : "—"}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={s.isActive ? "positive" : "neutral"}>{t(s.isActive ? "status.Active" : "status.Inactive")}</ObjectStatus></td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{structures.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}

/**
 * A new salary structure: the standard Ethiopian one (basic, income tax, 7% and 11% pension, kept on the server), or components
 * written here with formulas in the payroll formula language. "Try it" runs the calculator on the unsaved components.
 */
export function StructureCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [standard, setStandard] = useState(true);
  const [rows, setRows] = useState<Row[]>([{ ...emptyRow(), name: "Basic Salary", abbreviation: "BS", formula: "base", dependsOnPaymentDays: true }]);
  const [variables, setVariables] = useState("");
  const [base, setBase] = useState("10000");
  const [values, setValues] = useState<Record<string, string>>({});
  const names = parseVariables(variables);
  const components = rows.filter((r) => r.name.trim() || r.abbreviation.trim()).map(toComponent);
  const create = useMutation({
    mutationFn: (name: string) => post<SalaryStructureDto>("/api/payroll/structures", standard
      ? { companyId: company!.id, name }
      : { companyId: company!.id, name, components, variables: names }),
    onSuccess: (s) => {
      void queryClient.invalidateQueries({ queryKey: ["salary-structures"] });
      void navigate({ to: "/payroll/structures/$id", params: { id: s.id } });
    },
  });
  const tryIt = useMutation({
    mutationFn: () => post<SalaryCalculationDto>("/api/payroll/calculator", {
      base: Number(base), payrollDate: todayIso(), components: standard ? [] : components,
      variables: standard ? null : Object.fromEntries(names.map((n) => [n, Number(values[n] || 0)])),
    }),
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const setRow = (i: number, patch: Partial<Row>) => setRows((rs) => rs.map((r, j) => (j === i ? { ...r, ...patch } : r)));
  const onSubmit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(String(new FormData(e.currentTarget).get("name"))); };

  return (
    <DynamicPage
      title={t("structures.new")}
      breadcrumb={<Link to="/payroll/structures">{t("nav.salaryStructures")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        <button type="submit" form="structure-create" disabled={create.isPending}>{t("common.save")}</button>
        <button className="secondary" onClick={() => void navigate({ to: "/payroll/structures" })}>{t("common.cancel")}</button>
      </>}
    >
      <form id="structure-create" onSubmit={onSubmit}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" required /></label>
            <label className="check"><input type="checkbox" checked={standard} onChange={(e) => setStandard(e.target.checked)} /> {t("structures.useStandard")}</label>
            {!standard && <label>{t("structures.payValues")}<input value={variables} onChange={(e) => setVariables(e.target.value)} placeholder="ctc, fuel_payment" /></label>}
          </div>
          <p className="muted small">{standard ? t("structures.standardHint") : t("structures.formulaHint")}</p>
        </section>
        {!standard && (
          <section className="object-section">
            <h2>{t("structures.components")}</h2>
            <table className="table edit-table">
              <thead><tr>
                <th>{t("fields.name")}</th><th>{t("structures.abbreviation")}</th><th>{t("fields.type")}</th><th>{t("calculator.formula")}</th>
                <th className="num">{t("fields.amount")}</th><th>{t("structures.condition")}</th><th>{t("structures.taxable")}</th><th>{t("structures.byDays")}</th><th />
              </tr></thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={i}>
                    <td><input aria-label={t("fields.name")} value={r.name} onChange={(e) => setRow(i, { name: e.target.value })} /></td>
                    <td><input aria-label={t("structures.abbreviation")} className="mono" size={6} value={r.abbreviation} onChange={(e) => setRow(i, { abbreviation: e.target.value })} /></td>
                    <td>
                      <select aria-label={t("fields.type")} value={r.type} onChange={(e) => setRow(i, { type: e.target.value as Row["type"] })}>
                        {TYPES.map((type) => <option key={type} value={type}>{t(`slips.lineType.${type}`)}</option>)}
                      </select>
                    </td>
                    <td><input aria-label={t("calculator.formula")} className="mono" value={r.formula} onChange={(e) => setRow(i, { formula: e.target.value })} placeholder="base * 0.07" /></td>
                    <td><input aria-label={t("fields.amount")} className="num" type="number" step="0.01" value={r.amount} disabled={!!r.formula.trim()} onChange={(e) => setRow(i, { amount: e.target.value })} /></td>
                    <td><input aria-label={t("structures.condition")} className="mono" value={r.condition} onChange={(e) => setRow(i, { condition: e.target.value })} /></td>
                    <td><input aria-label={t("structures.taxable")} type="checkbox" checked={r.isTaxable} onChange={(e) => setRow(i, { isTaxable: e.target.checked })} /></td>
                    <td><input aria-label={t("structures.byDays")} type="checkbox" checked={r.dependsOnPaymentDays} onChange={(e) => setRow(i, { dependsOnPaymentDays: e.target.checked })} /></td>
                    <td>{rows.length > 1 && <button type="button" className="link-button" aria-label={t("ui.remove")} onClick={() => setRows((rs) => rs.filter((_, j) => j !== i))}>×</button>}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot><tr><td colSpan={9}><button type="button" className="secondary" onClick={() => setRows((rs) => [...rs, emptyRow()])}>{t("structures.addComponent")}</button></td></tr></tfoot>
            </table>
          </section>
        )}
      </form>
      <section className="object-section">
        <h2>{t("structures.tryIt")}</h2>
        <FilterBar>
          <FilterField label={t("fields.base")}><input type="number" min="0" step="0.01" value={base} onChange={(e) => setBase(e.target.value)} /></FilterField>
          {!standard && names.map((n) => (
            <FilterField key={n} label={n}><input type="number" step="0.01" value={values[n] ?? ""} onChange={(e) => setValues((v) => ({ ...v, [n]: e.target.value }))} /></FilterField>
          ))}
          <button type="button" className="secondary" onClick={() => tryIt.mutate()} disabled={tryIt.isPending}>{t("calculator.calculate")}</button>
        </FilterBar>
        {tryIt.isError && <MessageStrip tone="negative">{tryIt.error.message}</MessageStrip>}
        {tryIt.data && (
          <table className="table">
            <tbody>
              {tryIt.data.lines.map((l) => (
                <tr key={l.abbreviation} className={l.type === "EmployerContribution" ? "muted" : ""}>
                  <td>{l.component}</td><td className="mono small muted">{l.formula}</td><td className="num">{formatMoney(l.amount)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={2}>{t("fields.net")}</td><td className="num">{formatMoney(tryIt.data.netPay)}</td></tr></tfoot>
          </table>
        )}
      </section>
    </DynamicPage>
  );
}

/** Object page of one salary structure: its pay values and components with their formulas. */
export function StructureDetail() {
  const { t } = useTranslation();
  const { id } = useParams({ from: "/payroll/structures/$id" });
  const structures = useSalaryStructures();
  if (structures.isPending) return <p className="muted">{t("common.loading")}</p>;
  const s = structures.data?.find((x) => x.id === id);
  if (!s) return <MessageStrip tone="negative">{t("structures.notFound")}</MessageStrip>;

  return (
    <ObjectPage
      breadcrumb={<Link to="/payroll/structures">{t("nav.salaryStructures")}</Link>}
      title={s.name}
      status={<ObjectStatus tone={s.isActive ? "positive" : "neutral"}>{t(s.isActive ? "status.Active" : "status.Inactive")}</ObjectStatus>}
      kpis={[
        { label: t("structures.components"), value: String(s.components.length) },
        { label: t("structures.payValues"), value: s.variables?.length ? <span className="mono">{s.variables.join(", ")}</span> : "—" },
      ]}
      sections={[{
        id: "components", title: t("structures.components"), content: (
          <>
            {!!s.variables?.length && <p className="muted small">{t("structures.payValuesHint", { names: s.variables.join(", ") })}</p>}
            <table className="table responsive">
              <thead><tr><th>{t("fields.name")}</th><th>{t("structures.abbreviation")}</th><th>{t("fields.type")}</th><th>{t("calculator.formula")}</th><th>{t("structures.condition")}</th><th>{t("structures.taxable")}</th><th>{t("structures.byDays")}</th></tr></thead>
              <tbody>
                {s.components.map((c) => (
                  <tr key={c.abbreviation}>
                    <td data-label={t("fields.name")}>{c.name}</td>
                    <td data-label={t("structures.abbreviation")} className="mono">{c.abbreviation}</td>
                    <td data-label={t("fields.type")}>{t(`slips.lineType.${c.type}`)}</td>
                    <td data-label={t("calculator.formula")} className="mono small">{c.formula ?? (c.amount != null ? formatMoney(c.amount) : "—")}</td>
                    <td data-label={t("structures.condition")} className="mono small">{c.condition ?? "—"}</td>
                    <td data-label={t("structures.taxable")}>{c.isTaxable ? t("ui.yes") : t("ui.no")}</td>
                    <td data-label={t("structures.byDays")}>{c.dependsOnPaymentDays ? t("ui.yes") : t("ui.no")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        ),
      }]}
    />
  );
}
