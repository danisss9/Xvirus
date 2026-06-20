// Single source of truth for per-product build metadata (versions, names, service ids).
//
// To bump a product version, change it HERE only. The one place that must be kept in
// sync manually is the runtime update-check version in
// BaseLibrary/Modules/AppInfo.cs (the `antimalware` / `firewall` entries) — it decides
// which version the auto-updater compares against the server.

export const PRODUCTS = {
  am: {
    mode: 'am',
    name: 'Xvirus Anti-Malware',
    installFolder: 'Xvirus Anti-Malware',
    uiExeName: 'XvirusAM.exe',
    serviceName: 'XvirusAntiMalwareService',
    serviceDescription: 'Xvirus Anti-Malware Protection Service',
    setupDescription: 'Xvirus Anti-Malware Setup',
    version: '8.0.0',
    publisher: 'Xvirus',
    copyright: '© 2026 Xvirus',
  },
  fw: {
    mode: 'fw',
    name: 'Xvirus Firewall',
    installFolder: 'Xvirus Firewall',
    uiExeName: 'XvirusFW.exe',
    serviceName: 'XvirusFirewallService',
    serviceDescription: 'Xvirus Firewall Protection Service',
    setupDescription: 'Xvirus Firewall Setup',
    version: '5.0.0',
    publisher: 'Xvirus',
    copyright: '© 2026 Xvirus',
  },
};

/** Resolve product metadata for a build mode, exiting with a clear error on bad input. */
export function getProduct(mode) {
  const product = PRODUCTS[mode];
  if (!product) {
    console.error(`Invalid mode '${mode}'. Use: am | fw`);
    process.exit(1);
  }
  return product;
}

/** rcedit file-version / product-version expect a 4-part string. */
export function fourPartVersion(version) {
  const parts = String(version).split('.');
  while (parts.length < 4) parts.push('0');
  return parts.slice(0, 4).join('.');
}
