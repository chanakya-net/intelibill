export const ADMIN_NOTIFICATION_LANGUAGES = [
  { code: 'en-IN', label: 'English' },
  { code: 'hi-IN', label: 'हिंदी' },
  { code: 'ta-IN', label: 'தமிழ்' },
  { code: 'te-IN', label: 'తెలుగు' },
  { code: 'bn-IN', label: 'বাংলা' },
  { code: 'ml-IN', label: 'മലയാളം' },
  { code: 'kn-IN', label: 'ಕನ್ನಡ' },
  { code: 'mr-IN', label: 'मराठी' },
  { code: 'gu-IN', label: 'ગુજરાતી' },
] as const;

export const ADMIN_DEFAULT_LANGUAGE = 'en-IN';
export const ADMIN_SUPPORTED_LANGUAGE_CODES = ADMIN_NOTIFICATION_LANGUAGES.map((item) => item.code);
