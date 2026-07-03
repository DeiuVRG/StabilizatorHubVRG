// Relay (SSR) remote control with confirmation and pending state.
// The device is the source of truth: the switch settles only when the new
// state comes back through telemetry/status (or reverts on timeout).
import { api, toast } from './api.js';

const PENDING_TIMEOUT_MS = 20000;

let getDeviceId = null;
let pendingTimer = null;
let lastKnownState = false;
let demoMode = false;

const relaySwitch = () => document.getElementById('relay-switch');
const relayLabel = () => document.getElementById('relay-label');

/** Demo sessions are view-only: the switch stays disabled but keeps showing the real state. */
export function setDemoMode(enabled) {
  demoMode = enabled;
}

export function initControl(deviceIdProvider) {
  getDeviceId = deviceIdProvider;

  relaySwitch().addEventListener('change', async event => {
    const wantedOn = event.target.checked;

    // No confirmation dialog: the switch acts immediately and settles on the
    // real device state (or reverts on timeout).
    setPending(true, wantedOn);

    try {
      await api(`/api/devices/${getDeviceId()}/control`, { method: 'POST', body: { on: wantedOn } });

      pendingTimer = setTimeout(() => {
        // No confirmation arrived: roll the UI back to reality.
        setPending(false, lastKnownState);
        toast('The device did not confirm the command. Check that it is online.', 'warn');
      }, PENDING_TIMEOUT_MS);
    } catch (err) {
      setPending(false, lastKnownState);
      toast(err.message, 'error');
    }
  });
}

/** Called whenever fresh device state arrives (telemetry or status push). */
export function applyRelayState(outputOn) {
  lastKnownState = outputOn;

  if (pendingTimer) {
    clearTimeout(pendingTimer);
    pendingTimer = null;
  }

  setPending(false, outputOn);
}

function setPending(pending, switchState) {
  const sw = relaySwitch();
  sw.disabled = pending || demoMode;
  sw.checked = switchState;
  relayLabel().textContent = demoMode
    ? (switchState ? 'Output ON (demo)' : 'Output OFF (demo)')
    : pending
      ? 'Waiting for device...'
      : switchState ? 'Output ON' : 'Output OFF';
}
