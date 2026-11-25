declare const NL_IS_FIREWALL: boolean;
declare const NL_IS_ANTIMALWARE: boolean;

export const isFirewall = typeof NL_IS_FIREWALL !== 'undefined' ? NL_IS_FIREWALL : false;
export const isAntimalware = typeof NL_IS_ANTIMALWARE !== 'undefined' ? NL_IS_ANTIMALWARE : !isFirewall;

// Anti-Malware listens on 5236, Firewall on 5237 so both products can run side-by-side.
export const SERVICE_BASE_URL = isFirewall ? 'http://localhost:5237' : 'http://localhost:5236';
