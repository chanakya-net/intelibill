# P04-07 purchase orders and receiving

A shop needs current operating access to create, edit or place a purchase order and to post new receipts. The merchant decides from its local licence projection at transaction time. A prior order date or a backdated receipt does not extend that authority.

Type 13 reserves the receipt key before the account and shop licence fences. The receipt retains its existing posting-period, expiry, order, line and stock rules. Both keyed and unkeyed paths roll back the entire receipt if operating access ends while they are waiting. A replay uses Recovery after the reservation wait and returns the saved response only to a current Owner or Manager with permission. A permitted history read requires a current shop membership and can include Staff. Security suspension blocks history and recovery.

The purchase order screens show the scoped restriction and keep owner-bound receiving work in IndexedDB. A lookup miss and an ambiguous response preserve the original key and body. The history refresh after confirmation reads current order data. The frozen receipt response represents the original posting.

## Executable acceptance

- `P0407LicensingTests.PreExpiry_order_cannot_receive_at_deadline` creates and places an order for ten, posts four just before expiry, then attempts the remaining six with a backdated posting date. It compares PostgreSQL order, lot, stock, payable, sequence and operation facts after both keyed and unkeyed refusals.
- `P0407LicensingTests` also exercises direct dispatch, same-key waiting and caller revocation, projection-versus-receipt ordering, multi-lot rollback and permitted history and recovery.
- `licensing-purchase-orders.spec.ts` covers the built merchant UI at desktop and narrow widths. The local fixture drops the original receipt response after recording four units. A reload and lookup miss retain the key and body. A later authorized lookup confirms the same receipt with six units remaining. The security case withholds the receipt and preserves the original intent until permission returns.

PostgreSQL tests explicitly enroll only their local fixture shops, and verify that applying an entitlement projection leaves enrollment unset by default. The Chromium tests use a local HTTP fixture and real IndexedDB. The two suites establish merchant behavior. Live provider verification and production enrollment are outside this check.
