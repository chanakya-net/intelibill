/**
 * Shared PassThrough (PT) configurations for PrimeNG components to ensure consistent
 * styling across the application.
 */

export const CURRENCY_INPUT_GROUP_PT = {
  root: {
    class: [
      'flex items-stretch border border-slate-300 rounded-xl overflow-hidden',
      'focus-within:border-orange-600 focus-within:ring-1 focus-within:ring-orange-600/20 transition-all',
      'bg-white'
    ].join(' ')
  }
};

export const CURRENCY_ADDON_PT = {
  root: {
    class: [
      'bg-slate-50/50 border-none border-r border-slate-200 text-slate-600',
      'px-4 flex items-center justify-center min-w-[3.5rem] text-xl font-normal'
    ].join(' ')
  }
};

export const CURRENCY_INPUT_NUMBER_PT = {
  pcInputText: {
    root: {
      class: 'border-none shadow-none bg-transparent px-4 py-2.5 text-lg text-slate-800 focus:ring-0'
    }
  }
};

export const CURRENCY_SELECT_PT = {
  root: {
    class: 'border-none border-l border-slate-200 rounded-none bg-slate-50 shadow-none focus-within:ring-0'
  },
  label: {
    class: 'py-2.5 px-3'
  }
};
