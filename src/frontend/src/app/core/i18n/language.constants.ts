export const SUPPORTED_LANGUAGES = [
  'en-IN',
  'hi-IN',
  'ta-IN',
  'te-IN',
  'bn-IN',
  'ml-IN',
  'kn-IN',
  'mr-IN',
  'gu-IN',
] as const;

export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export const DEFAULT_LANGUAGE: SupportedLanguage = 'en-IN';

export const NATIVE_LANGUAGE_NAMES: Record<SupportedLanguage, string> = {
  'en-IN': 'English',
  'hi-IN': 'हिंदी',
  'ta-IN': 'தமிழ்',
  'te-IN': 'తెలుగు',
  'bn-IN': 'বাংলা',
  'ml-IN': 'മലയാളം',
  'kn-IN': 'ಕನ್ನಡ',
  'mr-IN': 'मराठी',
  'gu-IN': 'ગુજરાતી',
};
