import {
  buildPaymentMixDonutChartData,
  buildPaymentMixTrendChartData,
  buildProfitTrendChartData,
  buildSalesTrendChartData,
  formatTrendDateLabel,
} from './dashboard-chart-builders';

const makeSalesTrendPoints = () => [
  { date: '2026-05-01', amount: 120, netAmount: 100 },
  { date: '2026-05-02', amount: 200, netAmount: 180 },
];

const makeProfitTrendPoints = () => [
  { date: '2026-05-01', profitBeforeTax: 80, profitAfterTax: 90 },
  { date: '2026-05-02', profitBeforeTax: 120, profitAfterTax: 130 },
];

const paymentMixLabels = {
  cash: 'dashboard.paymentMixCash',
  upi: 'dashboard.paymentMixUpi',
  card: 'dashboard.paymentMixCard',
  credit: 'dashboard.paymentMixCredit',
};

describe('dashboard chart builders', () => {
  it('buildSalesTrendChartData maps sales+profit trend points', () => {
    const result = buildSalesTrendChartData(makeSalesTrendPoints(), makeProfitTrendPoints(), {
      salesBooked: 'Sales',
      netSalesBooked: 'Net Sales',
      profitBeforeTax: 'Profit Before Tax',
      profitAfterTax: 'Profit After Tax',
    });

    expect(result).not.toBeNull();
    expect(result?.labels).toEqual(['May 1', 'May 2']);
    expect(result?.datasets).toHaveLength(4);
  });

  it('buildSalesTrendChartData requires both trend series', () => {
    expect(buildSalesTrendChartData([], [], {
      salesBooked: 'Sales',
      netSalesBooked: 'Net Sales',
      profitBeforeTax: 'Profit Before Tax',
      profitAfterTax: 'Profit After Tax',
    })).toBeNull();
  });

  it('buildProfitTrendChartData maps a profit trend', () => {
    const result = buildProfitTrendChartData(makeProfitTrendPoints(), 'Profit After Tax');
    expect(result).toEqual({
      labels: ['2026-05-01', '2026-05-02'],
      datasets: [{
        label: 'Profit After Tax',
        data: [90, 130],
        fill: false,
        tension: 0.3,
      }],
    });
  });

  it('builds payment mix trend chart for positive data', () => {
    const result = buildPaymentMixTrendChartData([
      { date: '2026-05-01', cash: 10, upi: 20, card: 0, credit: 5 },
      { date: '2026-05-02', cash: 8, upi: 12, card: 1, credit: 9 },
    ], paymentMixLabels);

    expect(result?.labels).toEqual(['May 1', 'May 2']);
    expect(result?.datasets).toHaveLength(4);
    expect(result?.datasets[0]?.data).toEqual([10, 8]);
  });

  it('builds payment mix donut chart from aggregate values', () => {
    const result = buildPaymentMixDonutChartData(
      { cash: 10, upi: 20, card: 5, credit: 65 },
      paymentMixLabels,
    );

    expect(result?.labels).toEqual([
      'dashboard.paymentMixCash',
      'dashboard.paymentMixUpi',
      'dashboard.paymentMixCard',
      'dashboard.paymentMixCredit',
    ]);
    expect(result?.datasets?.[0]?.data).toEqual([10, 20, 5, 65]);
  });

  it('returns null when payment mix totals are all zero', () => {
    expect(
      buildPaymentMixDonutChartData({ cash: 0, upi: 0, card: 0, credit: 0 }, paymentMixLabels),
    ).toBeNull();
  });

  it('formats trend dates from yyyy-mm-dd', () => {
    expect(formatTrendDateLabel('2026-05-16')).toBe('May 16');
  });
});
