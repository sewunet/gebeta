# Custom features inventory

Every custom doctype in the current Frappe apps and the Gebeta module it moves to.
Use this as the checklist that nothing is forgotten. Mark each row when it is rebuilt.

Source: `customized_forcommon` (and `cash_management`) on the Sugar bench, November 2026.

## cash_management → Cash Management

| Doctype | Status |
|---|---|
| cashier | ☑ Cash Management: cashiers and the cashier ledger |
| petty_cash_request | ☑ Cash Management: petty cash requests |
| cash_accounting_settings | ☐ |

## customized_forcommon

### → Payroll
| Doctype | Status |
|---|---|
| salary_back_payment, back_payment_detail, back_payment_component, back_payment_payroll_line, salary_back_payment_settings | ☑ Payroll: salary back payment |
| bulk_additional_salary, employees_for_additional_salary | ☑ Payroll: additional salary (step 47) |
| salary_payment_distribution_request | ☐ |
| component_expense_account | ☐ |
| healthcare_payment_deduction | ☐ |

### → HR
| Doctype | Status |
|---|---|
| children_information, ethnicity, qualification, education_program, institution | ☐ |
| level, employee_step | ☐ |
| bulk_employee_promotion, bulk_promotion_employee, promotion_applicant, promotion_test, promotion_test_criteria, promotion_test_employees, promotion_test_rank | ☐ |
| bulk_employee_transfer | ☐ |
| internal_vacancy_applicant, applicant_test_result, interview_criteria_score | ☐ |
| succession_planning_details, talent_management, talent_planning | ☐ |
| employee_separation_reason, separation_reason, employee_severance_amount | ☐ |
| clearance, clearance_table, clearance_template, clearance_template_table | ☐ |
| attendance_collection, attendance_collection_log, bulk_employe_checkin, bulk_employee_checkin_list, employee_checkin_list | ☐ |
| leave_allocation_auto_increment, leave_auto_increment_employees, leave_plan | ☐ |
| self_appraisal, area_of_improvement, disciplinary_action, disciplinary_action_type | ☐ |
| training_plan, department_for_training_plan, employee_for_training_plan, training_address, training_assessment_template, training_assessment_template_criteria, training_assessment_response, training_assessment_response_criteria, training_commitment, training_evaluation, training_evaluation_template, training_event_attendance, training_event_total_present_days, training_impact_assessment, training_impact_assessment_employee, training_impact_assessment_report, training_impact_assessment_report_result, training_report, training_result_template, student_report | ☐ |
| employee_per_diem_and_expense_request, per_diem_breakdown, per_diem_component, per_diem_default_account, per_diem_expense_category, per_diem_expense_line, employee_grade_per_diem_component, employee_grade_expense_limit | ☐ |
| employee_advance_auto_repayment_dates, employee_advance_clearance, advance_recovery_detail | ☐ |
| healthcare_payment_request | ☐ |
| knowledge_management, knowledge_details, knowledge_distribution_methods | ☐ |
| house | ☐ |
| organizational_issue | ☐ |

### → Accounting
| Doctype | Status |
|---|---|
| vat_closing | ☐ |
| bank_reconciliation_reversal_log | ☐ |
| cif_value_accounts | ☐ |
| lc_master, lc_journal_entry_table, lc_payment_entry_table, lc_purchase_invoice_table, lc_purchase_order_table, lc_purchase_receipt_table | ☐ |
| purchase_invoice_employee_advance | ☐ |

### → Stock / Buying
| Doctype | Status |
|---|---|
| material_request_type | ☐ |
| material_receipt_for_transfer, material_receipt_for_transfer_items | ☐ |
| transport_order, transport_order_item, transport_order_warehouse_change, warehouse_distance | ☐ |
| item_specification_template, item_specification_template_criteria, item_status | ☐ |
| item_supply, supply_items | ☐ |
| tender_bidding_settings, tender_specification, tender_specification_committee_member, tender_specification_criteria | ☐ |
| supplier_specification_score, supplier_specification_score_detail, supplier_specification_score_item | ☐ |
| warranty_request, warranty_wintess, witness_employee_detail | ☐ |

### → Manufacturing
| Doctype | Status |
|---|---|
| workstation_detail, workstation_operating_component, cost_components | ☐ |
| maintenance_request, maintenance_department, maintenance_employee, maintenance_material_table | ☐ |
| Override: job-card time for parallel workstations (`overrides/workorder.py`) | ☐ |

### → Assets
| Doctype | Status |
|---|---|
| asset_borrowing, asset_disposal, disposal_method, sub_asset | ☐ |
| collateral_type | ☐ |

### → Projects
| Doctype | Status |
|---|---|
| project_advance_payment, project_advance_payment_history, project_advance_payment_term, project_advance_payment_terms, project_advance_progress_payment, project_advance_rebate, project_advance_accounts, project_advance_contract_additions, project_advance_settings | ☐ |

### → Selling (property sales, FHC)
| Doctype | Status |
|---|---|
| agreement, agreement_item, agreement_witness | ☐ |
| FHC property sales custom fields (branch `sw-fhc`) | ☐ |

### → Platform
| Doctype | Status |
|---|---|
| branch_office | ☐ |
| user_company_assignment, max_user_restriction | ☐ |
| brand_theme app (Branding Settings, workspace order, login, spinner) | ☐ |

Also review the custom fields, client scripts, server scripts and overrides in each app (not
only doctypes) before the module's phase starts.
