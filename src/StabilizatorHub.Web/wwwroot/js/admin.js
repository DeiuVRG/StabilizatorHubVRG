// Admin page: software update, devices overview, encrypted logs, audit trail.
import { api, toast, fmt } from './api.js';

const el = id => document.getElementById(id);

async function boot() {
  const me = await api('/api/auth/me');

  if (!me.isAdmin) {
    location.href = '/';
    return;
  }

  el('user-email').textContent = me.email;
  el('btn-logout').addEventListener('click', async () => {
    try { await api('/api/auth/logout', { method: 'POST' }); } finally { location.href = '/login.html'; }
  });

  api('/api/system/version').then(v => { el('sys-version').textContent = v.version; }).catch(() => {});

  el('btn-check-update').addEventListener('click', checkUpdate);
  el('btn-apply-update').addEventListener('click', applyUpdate);
  el('btn-refresh-audit').addEventListener('click', loadAudit);

  await Promise.allSettled([loadDevices(), loadLogs(), loadAudit()]);
}

async function checkUpdate() {
  const button = el('btn-check-update');
  button.disabled = true;
  button.textContent = 'Checking...';

  try {
    const info = await api('/api/system/update/check', { method: 'POST' });
    const result = el('update-result');

    if (info.error) {
      result.textContent = info.error;
      el('btn-apply-update').classList.add('hidden');
    } else if (info.updateAvailable) {
      result.textContent = `Update available: ${info.latestVersion} (running ${info.currentVersion}).`;
      el('btn-apply-update').classList.remove('hidden');
    } else {
      result.textContent = `You are up to date (${info.currentVersion}).`;
      el('btn-apply-update').classList.add('hidden');
    }
  } catch (err) {
    toast(err.message, 'error');
  } finally {
    button.disabled = false;
    button.textContent = 'Check for updates';
  }
}

async function applyUpdate() {
  if (!confirm('Install the update now? The service restarts and the dashboard will be unavailable for about a minute.')) {
    return;
  }

  try {
    const result = await api('/api/system/update/apply', { method: 'POST' });
    toast(result.message ?? 'Update requested.', 'ok', 8000);
  } catch (err) {
    toast(err.message, 'error');
  }
}

async function loadDevices() {
  const devices = await api('/api/system/devices');
  const body = el('admin-devices-body');
  body.innerHTML = '';

  if (!devices.length) {
    body.innerHTML = '<tr><td colspan="6" class="empty-row">No devices have announced themselves yet</td></tr>';
    return;
  }

  for (const device of devices) {
    const row = document.createElement('tr');
    row.innerHTML = `
      <td>${device.id}</td>
      <td>${device.name}</td>
      <td><span class="pill ${device.isOnline ? 'online' : 'offline'}">${device.isOnline ? 'online' : 'offline'}</span></td>
      <td>${device.isClaimed ? '<span class="pill neutral">claimed</span>' : '<span class="pill warn">unclaimed</span>'}</td>
      <td>${device.firmwareVersion ?? '--'}</td>
      <td>${device.lastSeenUtc ? fmt.dateTime(device.lastSeenUtc) : 'never'}</td>`;
    body.appendChild(row);
  }
}

async function loadLogs() {
  const dates = await api('/api/system/logs');
  const host = el('log-list');
  host.innerHTML = '';

  if (!dates.length) {
    host.innerHTML = '<span class="hint">No encrypted logs yet.</span>';
    return;
  }

  for (const date of dates) {
    const link = document.createElement('a');
    link.className = 'btn small';
    link.href = `/api/system/logs/${date}`;
    link.textContent = date;
    link.title = 'Download decrypted CSV';
    host.appendChild(link);
  }
}

async function loadAudit() {
  const entries = await api('/api/system/audit?take=100');
  const body = el('audit-body');
  body.innerHTML = '';

  if (!entries.length) {
    body.innerHTML = '<tr><td colspan="6" class="empty-row">Audit trail is empty</td></tr>';
    return;
  }

  for (const entry of entries) {
    const row = document.createElement('tr');
    row.innerHTML = `
      <td>${fmt.dateTime(entry.timestampUtc)}</td>
      <td>${entry.action}</td>
      <td>${entry.userEmail ?? '--'}</td>
      <td>${entry.deviceId ?? '--'}</td>
      <td>${entry.details ?? ''}</td>
      <td>${entry.ipAddress ?? ''}</td>`;
    body.appendChild(row);
  }
}

boot().catch(err => {
  if (err?.status !== 401) toast(err.message ?? 'Failed to load the admin page.', 'error');
});
