// Core API helper: fetch wrapper with cookie auth, CSRF header and
// uniform error handling. Every other module talks to the backend through this.

export class ApiError extends Error {
  constructor(message, status) {
    super(message);
    this.status = status;
  }
}

export function getCookie(name) {
  const match = document.cookie.split('; ').find(c => c.startsWith(name + '='));
  return match ? decodeURIComponent(match.slice(name.length + 1)) : null;
}

export async function api(path, { method = 'GET', body } = {}) {
  const headers = { Accept: 'application/json' };

  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (method !== 'GET') headers['X-XSRF-TOKEN'] = getCookie('XSRF-TOKEN') ?? '';

  const response = await fetch(path, {
    method,
    headers,
    credentials: 'same-origin',
    body: body !== undefined ? JSON.stringify(body) : undefined
  });

  if (response.status === 401 && !location.pathname.endsWith('/login.html')) {
    location.href = '/login.html';
    throw new ApiError('Not authenticated', 401);
  }

  let data = null;
  const text = await response.text();
  if (text) {
    try { data = JSON.parse(text); } catch { data = null; }
  }

  if (!response.ok) {
    throw new ApiError(data?.error ?? `Request failed (${response.status})`, response.status);
  }

  return data;
}

// Offset in minutes EAST of UTC, as the backend expects
// (JS getTimezoneOffset is minutes behind UTC, hence the sign flip).
export const tzOffsetMinutes = -new Date().getTimezoneOffset();

// ---------------------------------------------------------------------------
// Toast notifications (Nielsen: visibility of system status / feedback)
// ---------------------------------------------------------------------------

export function toast(message, kind = 'info', timeoutMs = 4500) {
  const host = document.getElementById('toasts');
  if (!host) return;

  const el = document.createElement('div');
  el.className = `toast ${kind}`;
  el.textContent = message;
  host.appendChild(el);

  setTimeout(() => el.remove(), timeoutMs);
}

// ---------------------------------------------------------------------------
// Formatting helpers
// ---------------------------------------------------------------------------

export const fmt = {
  volts: v => (v == null ? '--' : Math.round(v).toString()),
  watts: v => (v == null ? '--' : Math.round(v).toString()),
  amps: v => (v == null ? '--' : v.toFixed(2)),
  kwh: v => (v == null ? '--' : v.toFixed(v >= 100 ? 0 : v >= 10 ? 1 : 2)),

  time: iso => new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }),

  dateTime: iso => new Date(iso).toLocaleString([], {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
  }),

  duration(startIso, endIso) {
    const ms = (endIso ? new Date(endIso) : new Date()) - new Date(startIso);
    const minutes = Math.max(0, Math.round(ms / 60000));
    if (minutes < 1) return '<1 min';
    if (minutes < 60) return `${minutes} min`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours} h ${minutes % 60} min`;
    return `${Math.floor(hours / 24)} d ${hours % 24} h`;
  }
};
