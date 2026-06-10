// Dashboard bootstrap and orchestration.
import { api, toast, fmt, tzOffsetMinutes } from './api.js';
import { initLive, joinDevice } from './live.js';
import { initCharts, seedLive, pushLivePoint, clearLive, refreshHistory } from './charts.js';
import { initControl, applyRelayState } from './control.js';
import { confirmDialog, promptDialog } from './modal.js';

const state = {
  devices: [],
  deviceId: null,
  range: 'day'
};

const el = id => document.getElementById(id);

// ---------------------------------------------------------------------------
// Boot
// ---------------------------------------------------------------------------

async function boot() {
  const me = await api('/api/auth/me'); // 401 redirects to login

  el('user-email').textContent = me.email;
  el('admin-link').classList.toggle('hidden', !me.isAdmin);
  el('btn-logout').addEventListener('click', logout);

  initCharts();
  initControl(() => state.deviceId);
  wireClaimForms();
  wireDeviceActions();
  wireRangeTabs();

  await initLive({ onTelemetry, onStatus: onDeviceStatus, onVoltageEvent });
  await reloadDevices();

  api('/api/system/version')
    .then(v => { el('app-version').textContent = 'v' + v.version; })
    .catch(() => {});

  // Polling safety net: even if the real-time channel drops, the dashboard
  // refreshes its aggregates every minute.
  setInterval(() => {
    if (state.deviceId) {
      refreshSummary().catch(() => {});
      refreshHistory(state.deviceId, state.range).catch(() => {});
    }
  }, 60000);
}

async function reloadDevices() {
  state.devices = await api('/api/devices');

  const hasDevices = state.devices.length > 0;
  el('empty-state').classList.toggle('hidden', hasDevices);
  el('dashboard').classList.toggle('hidden', !hasDevices);

  if (!hasDevices) {
    state.deviceId = null;
    return;
  }

  const remembered = localStorage.getItem('stabhub.device');
  const device = state.devices.find(d => d.id === remembered) ?? state.devices[0];

  renderDeviceSelect();
  await selectDevice(device.id);
}

function renderDeviceSelect() {
  const select = el('device-select');
  select.classList.toggle('hidden', state.devices.length < 2);
  select.innerHTML = '';

  for (const device of state.devices) {
    const option = document.createElement('option');
    option.value = device.id;
    option.textContent = device.name;
    select.appendChild(option);
  }

  select.value = state.deviceId ?? '';
  select.onchange = () => selectDevice(select.value);
}

async function selectDevice(deviceId) {
  state.deviceId = deviceId;
  localStorage.setItem('stabhub.device', deviceId);
  el('device-select').value = deviceId;

  const device = state.devices.find(d => d.id === deviceId);
  renderDeviceCard(device);
  clearLive();

  await Promise.allSettled([
    seedLiveChart(),
    refreshLatest(),
    refreshSummary(),
    refreshHistory(deviceId, state.range),
    refreshEvents()
  ]);
}

// ---------------------------------------------------------------------------
// Rendering
// ---------------------------------------------------------------------------

function renderDeviceCard(device) {
  if (!device) return;

  el('device-name').textContent = device.name;
  el('device-id').textContent = device.id;
  el('device-fw').textContent = device.firmwareVersion ?? '--';
  el('device-last-seen').textContent = device.lastSeenUtc ? fmt.dateTime(device.lastSeenUtc) : 'never';
  setOnlinePill(device.isOnline);
  applyRelayState(device.outputOn);
}

function setOnlinePill(isOnline) {
  const pill = el('device-online');
  pill.textContent = isOnline ? 'online' : 'offline';
  pill.className = `pill ${isOnline ? 'online' : 'offline'}`;
}

function renderStats(t) {
  el('stat-vin').textContent = fmt.volts(t.voltageIn);
  el('stat-vout').textContent = fmt.volts(t.voltageOut);
  el('stat-power').textContent = fmt.watts(t.powerWatts);
  el('stat-current').textContent = fmt.amps(t.currentAmps);
  el('last-update').textContent = fmt.time(t.timestampUtc);

  const flag = el('vin-flag');
  if (t.voltageIn <= 215) {
    flag.textContent = 'undervoltage';
    flag.className = 'pill warn';
  } else if (t.voltageIn >= 240) {
    flag.textContent = 'overvoltage';
    flag.className = 'pill offline';
  } else {
    flag.textContent = 'grid normal';
    flag.className = 'pill neutral';
  }
}

async function refreshLatest() {
  const latest = await api(`/api/devices/${state.deviceId}/telemetry/latest`);
  if (latest) renderStats(latest);
}

async function seedLiveChart() {
  const samples = await api(`/api/devices/${state.deviceId}/telemetry/recent?minutes=60`);
  seedLive(samples);
}

async function refreshSummary() {
  const summary = await api(`/api/devices/${state.deviceId}/summary?tz=${tzOffsetMinutes}`);
  el('stat-today').textContent = fmt.kwh(summary.todayKwh);
  el('sum-today').textContent = fmt.kwh(summary.todayKwh) + ' kWh';
  el('sum-week').textContent = fmt.kwh(summary.last7DaysKwh) + ' kWh';
  el('sum-month').textContent = fmt.kwh(summary.last30DaysKwh) + ' kWh';
}

async function refreshEvents() {
  const events = await api(`/api/devices/${state.deviceId}/events?take=50`);
  const body = el('events-body');
  body.innerHTML = '';

  if (!events.length) {
    body.innerHTML = '<tr><td colspan="6" class="empty-row">No events recorded</td></tr>';
    return;
  }

  for (const ev of events) {
    const row = document.createElement('tr');
    const isUnder = ev.type === 'undervoltage';

    row.innerHTML = `
      <td><span class="pill ${isUnder ? 'badge-under warn' : 'badge-over offline'}">${ev.type}</span></td>
      <td>${fmt.dateTime(ev.startedAtUtc)}</td>
      <td>${fmt.duration(ev.startedAtUtc, ev.endedAtUtc)}</td>
      <td>${Math.round(ev.extremeVoltage)} V</td>
      <td>${ev.sampleCount}</td>
      <td>${ev.isOpen ? '<span class="pill warn">ongoing</span>' : '<span class="pill neutral">closed</span>'}</td>`;

    body.appendChild(row);
  }
}

// ---------------------------------------------------------------------------
// Real-time handlers
// ---------------------------------------------------------------------------

function onTelemetry(t) {
  if (t.deviceId !== state.deviceId) return;

  renderStats(t);
  pushLivePoint(t);
  applyRelayState(t.outputOn);
  setOnlinePill(true);
}

function onDeviceStatus(status) {
  if (status.deviceId !== state.deviceId) return;

  setOnlinePill(status.isOnline);
  applyRelayState(status.outputOn);

  if (!status.isOnline) {
    toast('Device went offline.', 'warn');
  }
}

function onVoltageEvent(ev) {
  if (ev.deviceId !== state.deviceId) return;

  if (ev.isOpen) {
    toast(`${ev.type === 'undervoltage' ? 'Undervoltage' : 'Overvoltage'} detected: ${Math.round(ev.extremeVoltage)} V at input!`, 'error', 8000);
  } else {
    toast('Voltage event ended - input back to normal.', 'ok');
  }

  refreshEvents().catch(() => {});
}

// ---------------------------------------------------------------------------
// Actions
// ---------------------------------------------------------------------------

function wireClaimForms() {
  el('claim-form-empty').addEventListener('submit', event => {
    event.preventDefault();
    claim(el('claim-code-empty').value);
  });

  el('btn-add-device').addEventListener('click', async () => {
    const code = await promptDialog({
      title: 'Add a device',
      text: 'Enter the pairing code shown on the OLED display of the stabilizer.',
      confirmText: 'Link device'
    });

    if (code) await claim(code);
  });
}

async function claim(code) {
  try {
    const device = await api('/api/devices/claim', {
      method: 'POST',
      body: { pairingCode: code.trim().toUpperCase() }
    });

    toast(`Device "${device.name}" linked to your account.`, 'ok');
    await joinDevice(device.id);
    await reloadDevices();
    await selectDevice(device.id);
  } catch (err) {
    toast(err.message, 'error');
  }
}

function wireDeviceActions() {
  el('btn-rename').addEventListener('click', async () => {
    const device = state.devices.find(d => d.id === state.deviceId);
    const name = await promptDialog({
      title: 'Rename device',
      text: 'Choose a friendly name (e.g. "Boiler room stabilizer").',
      initialValue: device?.name ?? ''
    });

    if (!name) return;

    try {
      const updated = await api(`/api/devices/${state.deviceId}`, { method: 'PUT', body: { name } });
      toast('Device renamed.', 'ok');
      const index = state.devices.findIndex(d => d.id === updated.id);
      if (index >= 0) state.devices[index] = updated;
      renderDeviceSelect();
      renderDeviceCard(updated);
    } catch (err) {
      toast(err.message, 'error');
    }
  });

  el('btn-release').addEventListener('click', async () => {
    const confirmed = await confirmDialog({
      title: 'Unlink this device?',
      text: 'The device returns to "unclaimed" and shows a new pairing code on its display. You will lose access until someone claims it again.',
      confirmText: 'Unlink',
      danger: true
    });

    if (!confirmed) return;

    try {
      await api(`/api/devices/${state.deviceId}`, { method: 'DELETE' });
      toast('Device unlinked.', 'ok');
      localStorage.removeItem('stabhub.device');
      await reloadDevices();
    } catch (err) {
      toast(err.message, 'error');
    }
  });
}

function wireRangeTabs() {
  const tabs = el('range-tabs');

  tabs.querySelectorAll('button').forEach(button => {
    button.addEventListener('click', async () => {
      tabs.querySelectorAll('button').forEach(b => b.classList.remove('active'));
      button.classList.add('active');
      state.range = button.dataset.range;
      await refreshHistory(state.deviceId, state.range).catch(err => toast(err.message, 'error'));
    });
  });
}

async function logout() {
  try {
    await api('/api/auth/logout', { method: 'POST' });
  } finally {
    location.href = '/login.html';
  }
}

boot().catch(err => {
  if (err?.status !== 401) toast(err.message ?? 'Failed to load the dashboard.', 'error');
});
