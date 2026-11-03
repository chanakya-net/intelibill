/**
 * The GST state and union territory codes a place of business or a place of supply can carry. The names are proper
 * nouns printed on tax documents as the GST portal spells them, so they are not translated. 96 (foreign country) and
 * 97 (other territory) are deliberately absent: exports and other-territory supplies are not billed here.
 */
export interface GstState {
  readonly code: string;
  readonly name: string;
}

export const GST_STATES: readonly GstState[] = [
  { code: '01', name: 'Jammu and Kashmir' },
  { code: '02', name: 'Himachal Pradesh' },
  { code: '03', name: 'Punjab' },
  { code: '04', name: 'Chandigarh' },
  { code: '05', name: 'Uttarakhand' },
  { code: '06', name: 'Haryana' },
  { code: '07', name: 'Delhi' },
  { code: '08', name: 'Rajasthan' },
  { code: '09', name: 'Uttar Pradesh' },
  { code: '10', name: 'Bihar' },
  { code: '11', name: 'Sikkim' },
  { code: '12', name: 'Arunachal Pradesh' },
  { code: '13', name: 'Nagaland' },
  { code: '14', name: 'Manipur' },
  { code: '15', name: 'Mizoram' },
  { code: '16', name: 'Tripura' },
  { code: '17', name: 'Meghalaya' },
  { code: '18', name: 'Assam' },
  { code: '19', name: 'West Bengal' },
  { code: '20', name: 'Jharkhand' },
  { code: '21', name: 'Odisha' },
  { code: '22', name: 'Chhattisgarh' },
  { code: '23', name: 'Madhya Pradesh' },
  { code: '24', name: 'Gujarat' },
  { code: '26', name: 'Dadra and Nagar Haveli and Daman and Diu' },
  { code: '27', name: 'Maharashtra' },
  { code: '29', name: 'Karnataka' },
  { code: '30', name: 'Goa' },
  { code: '31', name: 'Lakshadweep' },
  { code: '32', name: 'Kerala' },
  { code: '33', name: 'Tamil Nadu' },
  { code: '34', name: 'Puducherry' },
  { code: '35', name: 'Andaman and Nicobar Islands' },
  { code: '36', name: 'Telangana' },
  { code: '37', name: 'Andhra Pradesh' },
  { code: '38', name: 'Ladakh' },
];

const NAMES_BY_CODE: ReadonlyMap<string, string> = new Map(GST_STATES.map((state) => [state.code, state.name]));

export interface GstStateOption {
  readonly value: string;
  readonly name: string;
}

/** Select options: the value is the code, the shown name carries the code too so a GSTIN prefix is easy to match. */
export const GST_STATE_OPTIONS: readonly GstStateOption[] = GST_STATES.map((state) => ({
  value: state.code,
  name: `${state.name} (${state.code})`,
}));

export function gstStateName(code: string | null | undefined): string | null {
  return code ? (NAMES_BY_CODE.get(code) ?? null) : null;
}

export function isKnownGstStateCode(code: string | null | undefined): boolean {
  return !!code && NAMES_BY_CODE.has(code);
}
