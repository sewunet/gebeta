import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatDate, formatMoney } from "../../lib/ethiopian";
import type { WithholdingReceiptDto } from "../../lib/types";

/** The withholding receipt for the supplier, laid out to print (the sidebar and top bar are hidden when printing). */
export function WithholdingReceipt() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const { id } = useParams({ from: "/accounting/invoices/$id/receipt" });
  const receipt = useQuery({ queryKey: ["withholding-receipt", id], queryFn: () => api<WithholdingReceiptDto>(`/api/accounting/invoices/${id}/withholding-receipt`) });
  if (receipt.isError) return <div className="error">{receipt.error.message}</div>;
  if (!receipt.data) return <p className="muted">{t("common.loading")}</p>;
  const r = receipt.data;
  const am = i18n.language === "am";

  return (
    <>
      <div className="no-print inline-fields">
        <Link to="/accounting/invoices" className="back">← {t("nav.invoices")}</Link>
        <button onClick={() => window.print()}>{t("receipt.print")}</button>
      </div>
      <article className="receipt">
        <header>
          <h2>{t("receipt.title")}</h2>
          <div className="mono">{r.receiptNumber}</div>
          <div>{formatDate(r.date, "Both", am)}</div>
          {r.status === "Cancelled" && <div className="error">{t("receipt.void")}</div>}
        </header>
        <section className="parties">
          <div><div className="card-label">{t("receipt.agent")}</div><strong>{r.agentName}</strong><div>{t("fields.tin")} <span className="mono">{r.agentTin}</span></div></div>
          <div><div className="card-label">{t("inv.supplier")}</div><strong>{r.supplierName}</strong><div>{t("fields.tin")} <span className="mono">{r.supplierTin ?? "—"}</span></div></div>
        </section>
        <table className="table">
          <tbody>
            <tr><td>{t("receipt.invoice")}</td><td className="mono">{r.invoiceNumber}{r.supplierInvoiceNumber ? ` · ${r.supplierInvoiceNumber}` : ""}</td></tr>
            <tr><td>{t("receipt.supply")}</td><td>{t(`receipt.supplyType.${r.supplyType}`)}</td></tr>
            <tr><td>{t("inv.taxable")}</td><td className="num">{formatMoney(r.taxableAmount)}</td></tr>
            <tr><td>{t("receipt.rate")}</td><td className="num">{r.ratePercent}%</td></tr>
            <tr className="report-total"><td>{t("receipt.withheld")}</td><td className="num">{formatMoney(r.withheld)}</td></tr>
          </tbody>
        </table>
        <p className="muted small">{t("receipt.note")}</p>
        <footer className="signatures">
          <div>{t("receipt.preparedBy")}<span /></div>
          <div>{t("receipt.signature")}<span /></div>
        </footer>
      </article>
      <p className="no-print muted small">{t("receipt.printedFor", { user: me.fullName ?? me.username })}</p>
    </>
  );
}
