/** Bounded API fixture shape shared by browser flows using online party selectors. */
type Party = { name: string; isActive: boolean };
function page<T extends Party>(
  items: readonly T[],
  url: URL,
  fields: (party: T) => readonly (string | null | undefined)[],
  id: (party: T) => string,
) {
  const search = (url.searchParams.get('search') ?? '').trim().toLowerCase();
  const status = url.searchParams.get('status') ?? 'all';
  const matches = items
    .filter(
      (item) =>
        (status === 'all' || item.isActive === (status === 'active')) &&
        fields(item).some((value) => value?.toLowerCase().includes(search)),
    )
    .sort((a, b) => a.name.localeCompare(b.name) || id(a).localeCompare(id(b)));
  const pageNumber = Number(url.searchParams.get('pageNumber') ?? 1),
    pageSize = Number(url.searchParams.get('pageSize') ?? 20);
  return {
    items: matches.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
    totalCount: matches.length,
    pageNumber,
    pageSize,
  };
}
type Customer = Party & {
  customerId: string;
  phoneNumber: string;
  address?: string | null;
  outstandingDue?: number;
  creditLimit?: number;
  currentMonthRevenue?: number;
};
export function customerDirectoryResponse<T extends Customer>(items: readonly T[], url: URL) {
  return {
    ...page(
      items,
      url,
      (x) => [x.name, x.phoneNumber, x.address, x.customerId],
      (x) => x.customerId,
    ),
    summary: {
      totalCustomers: items.length,
      outstandingBalance: items.reduce((sum, x) => sum + Math.max(0, x.outstandingDue ?? 0), 0),
      outstandingCount: items.filter((x) => (x.outstandingDue ?? 0) > 0).length,
      totalCreditIssued: items.reduce((sum, x) => sum + (x.creditLimit ?? 0), 0),
      accountsWithCredit: items.filter((x) => (x.creditLimit ?? 0) > 0).length,
      monthlyRevenue: items.reduce((sum, x) => sum + (x.currentMonthRevenue ?? 0), 0),
    },
  };
}
type Supplier = Party & {
  supplierId: string;
  isSystem: boolean;
  isPreferred: boolean;
  balanceDue: number;
  contactPersonName?: string | null;
  contactPersonPhone?: string | null;
  city?: string | null;
};
export function supplierDirectoryResponse<T extends Supplier>(items: readonly T[], url: URL) {
  const visible = items.filter(
    (x) => url.searchParams.get('include_system') !== 'false' || !x.isSystem,
  );
  return {
    ...page(
      visible,
      url,
      (x) => [x.name, x.contactPersonName, x.contactPersonPhone, x.city],
      (x) => x.supplierId,
    ),
    summary: {
      totalSuppliers: visible.length,
      activeSuppliers: visible.filter((x) => x.isActive).length,
      preferredSuppliers: visible.filter((x) => x.isPreferred).length,
      pendingPayables: visible.reduce((sum, x) => sum + Math.max(0, x.balanceDue), 0),
    },
  };
}
