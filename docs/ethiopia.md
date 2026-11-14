# Ethiopian business and law

Gebeta is built **for Ethiopian businesses first** ([ADR 0009](adr/0009-ethiopia-first.md)).
Ethiopian rules are the defaults, not add-ons; other countries can be added later through the same
configuration.

> **Legal values change.** Rates, thresholds and slabs below are the values known when this was
> written (November 2026). Every one of them is stored as **configuration with an effective date**,
> never hard-coded, and must be confirmed with the Ministry of Revenue (MoR), the Ministry of Labor
> and Skills, the Accounting and Auditing Board of Ethiopia (AABE) or a licensed Ethiopian
> accountant or lawyer before a release. Rows marked **verify** are ones we are not certain of.

## 1. Calendar, dates and fiscal year

| Topic | Rule in Gebeta | Status |
|---|---|---|
| Ethiopian calendar | 12 months × 30 days + Pagume (5 days, 6 when `year % 4 == 3`). Implemented in `Gebeta.SharedKernel.Ethiopia.EthiopianDate`, tested against known dates. | Built |
| Storage | All dates are stored Gregorian (`DateOnly`); the Ethiopian date is shown next to it or instead of it, per user setting. | Built |
| Fiscal year | Default **Hamle 1 – Sene 30** (≈ 8 July – 7 July), named after the Ethiopian year it ends in (FY 2018 = 8 Jul 2025 – 7 Jul 2026). Companies may choose the Gregorian year. `EthiopianFiscalYear`. | Built |
| Monthly periods | 12 periods Hamle … Sene; Pagume is added to Nehase. | Built |
| Time zone | `Africa/Addis_Ababa` (UTC+3, no daylight saving). | Built (company default) |
| Ethiopian time of day | Optional display of local time (hour 1 = 7:00). Display only. | Planned |
| Public holidays | Enkutatash, Meskel, Genna, Timket, Adwa Victory Day, Labour Day, Patriots' Victory Day, Downfall of the Derg (Ginbot 20), Siklet (Good Friday), Fasika, Eid al-Fitr, Eid al-Adha, Mawlid. Fixed ones computed from the Ethiopian calendar (`EthiopianHolidays`); Siklet and Fasika from Orthodox Easter; Islamic holidays and company holidays entered each year when announced (`/api/core/holidays`). | Built |

## 2. Company and tax registration

| Topic | Rule | Status |
|---|---|---|
| TIN | 10 digits; required for every company, supplier and customer that receives a tax invoice; on payroll and withholding declarations. `TaxpayerIdentificationNumber`. | Built (format check) |
| Legal forms | Commercial Code (Proclamation 1243/2021): sole proprietorship, general / limited partnership, joint venture, PLC, one-person PLC, share company; plus public enterprise, cooperative, CSO, government body, foreign branch. | Built |
| VAT registration | Company is VAT-registered when it has a VAT registration number; otherwise turnover tax may apply. Registration threshold: annual taxable turnover above ETB 2,000,000 (VAT Proclamation 1341/2024). | Built (flag); threshold **verify** |
| Trade licence | Number and expiry stored; reminder before expiry. | Partly built |
| Currency | Ethiopian birr, `ETB`, 2 decimals (santim). Foreign currency with daily rates from the National Bank of Ethiopia (market-based since July 2024). | Built (default); NBE import planned |

## 3. Tax

| Tax | Rule in Gebeta | Status |
|---|---|---|
| **Employment income tax** | Monthly slab table with effective dates. Current table (matches the formula used in the existing system): 0–2,000: 0%; 2,001–4,000: 15% − 300; 4,001–7,000: 20% − 500; 7,001–10,000: 25% − 850; 10,001–14,000: 30% − 1,350; above 14,000: 35% − 2,050. The previous table (Proclamation 979/2016: exempt up to 600) is kept for back pay of earlier periods. | Planned (Payroll); **verify** proclamation number and effective date |
| Back pay (arrears) tax | Tax allocated to the month the arrears belong to (new tax of the month − tax already withheld), as the current Salary Back Payment does. | Planned (Payroll) |
| **VAT** | 15% standard rate; zero-rated and exempt supplies per the VAT Proclamation; input/output VAT, VAT return per month, VAT closing. | Planned (Accounting) |
| Turnover tax (TOT) | For businesses not registered for VAT; rates per the TOT law. | Planned; rates **verify** |
| **Withholding tax** | Withheld on payments to suppliers above thresholds (goods / services), with a numbered withholding receipt to the supplier and a monthly declaration. Rate and thresholds as configuration. | Planned (Accounting); rate and thresholds **verify** |
| Excise tax | Per item excise rate where applicable (e.g. sugar products, beverages). | Planned (Stock/Selling) |
| Document numbers | Naming series per company and document type (`PCR-.ABBR.-.FY.-.#####`), with Ethiopian year (`EYYYY`) and fiscal year (`FY`) tokens; numbers are gap-free and never repeat (taken in the saving transaction). | Built |
| Tax invoices | Required fields: seller and buyer TIN, VAT number, invoice number in sequence, date, VAT shown separately. Machine numbering per MoR rules; integration with the MoR **e-invoicing** system and sales register machines (fiscal printers) through a capability. | Planned |
| Records retention | Accounting and tax records kept for at least **10 years** (Tax Administration Proclamation 983/2016); ledgers are append-only and never deleted. | Design; period **verify** |

## 4. Payroll and labour law

| Topic | Rule in Gebeta | Status |
|---|---|---|
| Pension (private) | Employee **7%**, employer **11%** of basic salary (Private Organization Employees' Pension Proclamation 1268/2022). | Planned (Payroll) |
| Pension (public servants) | Employee 7%, employer 11% (Proclamation 1267/2022); different rates for military and police. | Planned; military/police **verify** |
| Working hours | 8 hours a day, 48 a week (Labour Proclamation 1156/2019). | Planned (HR) |
| Overtime | ×1.5 normal day work (6:00–22:00), ×1.75 night (22:00–6:00), ×2 weekly rest day, ×2.5 public holiday. | Planned (HR/Payroll) |
| Annual leave | 16 working days for the first year of service, plus 1 day for every additional 2 years. Leave ledger with accrual and expiry rules. | Planned (HR) |
| Maternity leave | 30 days before and 90 days after confinement, paid (120 days). | Planned (HR); **verify** |
| Paternity leave | 3 working days, paid. | Planned (HR) |
| Sick leave | Up to 6 months in 12: first month full pay, next two months 50%, last three months unpaid (on medical certificate). | Planned (HR) |
| Probation | Up to 60 working days. | Planned (HR) |
| Severance pay | 30 days' wages for the first year of service plus 1/3 of that for each additional year, capped at 12 months' wages. | Planned (HR/Payroll) |
| Payment days | Monthly salary on 30 days per month, as is usual in Ethiopia; configurable per company. | Planned (Payroll) |
| Deductions | Cost-sharing repayment (higher education graduates), court orders, credit associations (Idir/Equb), loans; each with a rule and ceiling. | Planned (Payroll) |
| Declarations | Monthly employment income tax declaration and pension contribution schedule in the formats required by MoR and the pension agency (Excel/CSV, later online). | Planned (Payroll) |

## 5. Accounting and reporting

| Topic | Rule | Status |
|---|---|---|
| Framework | IFRS for public interest entities, IFRS for SMEs for others (Financial Reporting Proclamation 847/2014, AABE). Chart of accounts templates for both. | Planned (Accounting) |
| Public enterprises | Templates for public enterprise reporting (e.g. sugar factories). | Planned |
| Amount in words | Amharic and English amount in words on invoices, payment vouchers and cheques ("ብር አንድ ሺህ ሁለት መቶ … ሳንቲም"). | Planned (SharedKernel) |
| Banking | Bank transfer files for major Ethiopian banks (CBE, Awash, Dashen, Abyssinia, …); Telebirr and other mobile money as payment methods. | Planned |

## 6. Language and data

| Topic | Rule | Status |
|---|---|---|
| Language | English and Amharic UI from day one (i18next); Afaan Oromo and Tigrinya later. Ge'ez script fonts in PDFs. | Planned (frontend) |
| Personal data | Personal Data Protection Proclamation 1321/2024: consent, purpose limitation, access logging for salary and personal data, export and deletion on request where the law allows. | Design; **verify** obligations |
| Hosting | On-premises or Ethiopian data centres supported (Docker); no dependency on foreign cloud services. | Built (Docker) |

## How legal rules are implemented

1. **Data, not code**: rates, slabs, thresholds and holiday dates live in tables with
   `valid_from` / `valid_to`, so a new proclamation is a data change with an effective date.
2. **Calculations use the rule valid on the transaction date**, so back pay and corrections for
   old periods use the old rules automatically.
3. **Every legal rule has tests** with worked examples, including the boundaries (2,000.00 and
   2,000.01 for income tax).
4. **A rule change is reviewed** by someone qualified in Ethiopian tax or labour law before release;
   the reviewer and the source (proclamation, directive, article) are recorded with the rule.
