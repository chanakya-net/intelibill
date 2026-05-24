import type {
  PaymentMixDto,
  PaymentMixTrendPointDto,
  ProfitTrendPointDto,
  SalesTrendPointDto,
} from '../models/dashboard-dto';

export type DashboardChartType = 'bar' | 'stackedBar' | 'line' | 'pie' | 'doughnut';

export interface ChartDataset {
  label?: string;
  data: number[];
  fill?: boolean;
  tension?: number;
  backgroundColor?: string | string[];
  borderColor?: string | string[];
  borderWidth?: number;
}

export interface ChartData {
  labels: string[];
  datasets: ChartDataset[];
}

export type DashboardChartData = ChartData;

export interface SalesChartLabels {
  salesBooked: string;
  netSalesBooked: string;
  profitBeforeTax: string;
  profitAfterTax: string;
}

export interface PaymentMixChartLabels {
  cash: string;
  upi: string;
  card: string;
  credit: string;
}

export function formatTrendDateLabel(value: string): string {
  const [year, month, day] = value.split('-').map(Number);
  if (!year || !month || !day) return value;

  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
  }).format(new Date(year, month - 1, day));
}

export function buildSalesTrendChartData(
  salesTrendSeries: ReadonlyArray<SalesTrendPointDto> | null | undefined,
  profitTrendSeries: ReadonlyArray<ProfitTrendPointDto> | null | undefined,
  labels: SalesChartLabels,
): ChartData | null {
  if (!salesTrendSeries?.length || !profitTrendSeries?.length) return null;

  return {
    labels: salesTrendSeries.map((point) => formatTrendDateLabel(point.date)),
    datasets: [
      {
        label: labels.salesBooked,
        data: salesTrendSeries.map((point) => point.amount),
        backgroundColor: '#0f766e',
        borderColor: '#0f766e',
        borderWidth: 1,
        tension: 0.25,
      },
      {
        label: labels.netSalesBooked,
        data: salesTrendSeries.map((point) => point.netAmount),
        backgroundColor: '#2563eb',
        borderColor: '#2563eb',
        borderWidth: 1,
        tension: 0.25,
      },
      {
        label: labels.profitBeforeTax,
        data: profitTrendSeries.map((point) => point.profitBeforeTax),
        backgroundColor: '#ca8a04',
        borderColor: '#ca8a04',
        borderWidth: 1,
        tension: 0.25,
      },
      {
        label: labels.profitAfterTax,
        data: profitTrendSeries.map((point) => point.profitAfterTax),
        backgroundColor: '#7c3aed',
        borderColor: '#7c3aed',
        borderWidth: 1,
        tension: 0.25,
      },
    ],
  };
}

export function buildProfitTrendChartData(
  profitTrendSeries: ReadonlyArray<ProfitTrendPointDto> | null | undefined,
  label: string,
): ChartData | null {
  if (!profitTrendSeries?.length) return null;

  return {
    labels: profitTrendSeries.map((point) => point.date),
    datasets: [
      {
        label,
        data: profitTrendSeries.map((point) => point.profitAfterTax),
        fill: false,
        tension: 0.3,
      },
    ],
  };
}

export function buildPaymentMixTrendChartData(
  paymentMixTrendSeries: ReadonlyArray<PaymentMixTrendPointDto> | null | undefined,
  labels: PaymentMixChartLabels,
): ChartData | null {
  if (!paymentMixTrendSeries?.length) return null;

  const hasAnyValue = paymentMixTrendSeries.some((point) => point.cash + point.upi + point.card + point.credit > 0);
  if (!hasAnyValue) return null;

  return {
    labels: paymentMixTrendSeries.map((point) => formatTrendDateLabel(point.date)),
    datasets: [
      {
        label: labels.cash,
        data: paymentMixTrendSeries.map((point) => point.cash),
        backgroundColor: '#3f9ad6',
        borderColor: '#3f9ad6',
        borderWidth: 1,
      },
      {
        label: labels.upi,
        data: paymentMixTrendSeries.map((point) => point.upi),
        backgroundColor: '#f35b7a',
        borderColor: '#f35b7a',
        borderWidth: 1,
      },
      {
        label: labels.card,
        data: paymentMixTrendSeries.map((point) => point.card),
        backgroundColor: '#f59e42',
        borderColor: '#f59e42',
        borderWidth: 1,
      },
      {
        label: labels.credit,
        data: paymentMixTrendSeries.map((point) => point.credit),
        backgroundColor: '#f0c451',
        borderColor: '#f0c451',
        borderWidth: 1,
      },
    ],
  };
}

export function buildPaymentMixDonutChartData(
  paymentMix: PaymentMixDto | null | undefined,
  labels: PaymentMixChartLabels,
): ChartData | null {
  if (!paymentMix) return null;

  const total = paymentMix.cash + paymentMix.upi + paymentMix.card + paymentMix.credit;
  if (total === 0) return null;

  return {
    labels: [labels.cash, labels.upi, labels.card, labels.credit],
    datasets: [
      {
        data: [paymentMix.cash, paymentMix.upi, paymentMix.card, paymentMix.credit],
      },
    ],
  };
}
