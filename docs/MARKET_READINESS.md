# Intelibill — Market Readiness & Feature Roadmap

This document outlines the current state of the Intelibill Inventory & Billing system and provides a strategic roadmap for market launch.

## 🟢 Fully Implemented (Market Ready)
These features are robust, follow the Clean Architecture pattern, and are ready for production use.

*   **Core Multi-Tenant Engine**: 
    *   JWT Authentication with Refresh Tokens.
    *   Multi-shop management (Switching/Defaulting active shops).
    *   Role-Based Access Control (Owner, Manager, Staff).
*   **Intelligent Dashboard**:
    *   Real-time KPIs (Sales Booked, Cash Collected, Net Profit).
    *   Role-based data masking (Staff cannot see profit/financials).
    *   Trend charts and comparison with previous periods.
    *   Automated priority alerts (Critical Stock, Highest Due Customer).
*   **Billing (POS) Core**:
    *   Recording sales with multiple payment modes (Cash, UPI, Card, Credit).
    *   Automatic stock deduction on sale.
    *   Tax calculation per line-item.
*   **Inventory & Supplier Management**:
    *   Batch-based inventory (FIFO support).
    *   Tracking cost vs. sales price vs. MRP.
    *   Supplier ledger for credit purchases and settlements.
*   **Customer Management**:
    *   Credit tracking (Outstanding Dues).
    *   Customer ledger for recording partial or full payments.
*   **Expense Management**:
    *   Categorized expense tracking.
    *   Correction/Adjustment flow for expense entries.

---

## 🟡 Partially Implemented (Needs Polish)
Infrastructure exists in the backend, but the frontend or logic needs completion.

*   **Tax Engine**:
    *   *Status*: Domain supports tax rates.
    *   *Missing*: GST/Tax Summary reports for accounting.
*   **Stock Adjustments**:
    *   *Status*: Enums for Damage (`DMG`), Stolen (`STOL`), and Rejection (`REJ`) exist.
    *   *Missing*: UI screens to record these transactions without a Sale/Purchase.
*   **Barcode Integration**:
    *   *Status*: Repository and Domain support barcode lookups.
    *   *Missing*: Fully optimized "Scan-to-Add" UI flow for high-speed billing.
*   **I18n (Localization)**:
    *   *Status*: Transloco setup for Angular.
    *   *Missing*: Complete translation files for languages other than English.

---

## 🔴 Missing Features (Required for Success)
These are high-priority gaps that typically determine if a shop owner will buy the software.

### 1. Point of Sale (POS) Experience
*   [ ] **Invoice Printing**: Support for 2-inch/3-inch Thermal Printers (ESC/POS) and A4 PDF generation.
*   [ ] **Discounts**: Ability to apply flat or percentage discounts at the line-item and invoice level.
*   [ ] **Sales Returns**: A dedicated flow to process returns, refund money, and restock the item.

### 2. Data Onboarding & Organization
*   [ ] **Bulk Excel/CSV Import**: Essential for onboarding customers with existing large inventories.
*   [ ] **Item Categories**: Organizing products (e.g., Electronics, Food) for better navigation and reporting.
*   [ ] **UoM Conversion**: Support for selling in different units (e.g., Buy in "Box", Sell in "Pieces").

### 3. Business Intelligence & Reporting
*   [ ] **Inventory Valuation**: Report showing the total value of stock at cost and sales price.
*   [ ] **Account Statements**: Exportable PDF/Excel ledgers for Customers and Suppliers.
*   [ ] **Daily Closing (Z-Report)**: A summary report at the end of the day for cash counter reconciliation.

### 4. System Controls & Branding
*   [ ] **Shop Settings UI**: Page to upload shop logo, set currency symbols, and customize invoice headers.
*   [ ] **Audit Logs**: Tracking who changed what (e.g., "User X edited the price of Item Y").
*   [ ] **Data Export**: Full export of sales/expenses to Excel for tax filing (Tally compatibility).

---

## 🗺️ Implementation Strategy (Priority)

1.  **Phase 1 (The Essentials)**: Printing support, Discounts, and Item Categories.
2.  **Phase 2 (The Onboarding)**: Bulk Excel Import and Stock Adjustment UI.
3.  **Phase 3 (The Compliance)**: Tax Reports, Valuation Reports, and Audit Logs.
4.  **Phase 4 (The Growth)**: WhatsApp/SMS Notifications and Mobile App.
