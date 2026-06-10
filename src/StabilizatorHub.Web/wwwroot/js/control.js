// Relay (SSR) remote control with confirmation and pending state.
// The device is the source of truth: the switch settles only when the new
// state comes back through telemetry/status (or reverts on timeout).
import { api, toast } from './api.js';
import { confirmDialog } from './modal.js';

const PENDING_TIMEOUT_MS = 20000;

let getDeviceId = null;
let pendingTimer = null;
let lastKnownState = false;

const relaySwitch = () => document.getElementById('relay-switch');
const relayLabel = () => document.getElementById('relay-label');

export function initControl(deviceIdProvider) {
  getDeviceId = deviceIdProvider;

  relaySwitch().addEventListener('change', async event => {
    const wantedOn = event.target.checked;

    // Error prevention: switching the output affects a real appliance.
    const confirmed = await confirmDialog({
      title: wantedOn ? 'Turn output ON?' : 'Turn output OFF?',
      text: wantedOn
        ? 'The SSR closes and the connected appliance receives power.'
        : 'The SSR opens and the connected appliance loses power.',
      confirmText: wantedOn ? 'Turn ON' : 'Turn OFF'
    });

    if (!confirmed) {
      event.target.checked = lastKnownState;
      return;
    }

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
  sw.disabled = pending;
  sw.checked = switchState;
  relayLabel().textContent = pending
    ? 'Waiting for device...'
    : switchState ? 'Output ON' : 'Output OFF';
}
