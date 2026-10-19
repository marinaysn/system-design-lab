import { InjectionToken } from '@angular/core';

/**
 * Base URL for the auth API. Empty means "same origin", which works with the
 * dev proxy (proxy.conf.js forwards /api to the backend). Override it by providing
 * this token, e.g. to call a deployed backend directly.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '',
});
